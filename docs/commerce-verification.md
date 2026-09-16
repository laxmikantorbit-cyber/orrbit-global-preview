# Commerce Foundation Verification

Date: 2026-09-16
Machine: DESKTOP-FOFADB8
Runtime: local PostgreSQL proof environments plus Neon PostgreSQL free-staging on the dedicated BusinessOS Commerce project.
Database: local Commerce proof databases plus Neon `businessos`; the Render secondary API uses Postgres while the primary free-staging API remains InMemory.

## Scope

Verified the Quote -> Order -> Subscription foundation and Subscription Renewal extension against the existing tenant, Customer 360, CRM/Sales, Catalog, Payments, and Licensing boundaries.

## Application verification

- Full solution regression: 207/207 tests passed.
- Commerce tests: 13/13 passed.
- Application activation bridge tests: 3/3 passed.
- API tests: 51/51 passed, including persistence, checkout, webhook, reconciliation, payment-ledger and admin flows.
- CRM tests: 19/19 passed after the parallel role/ownership/runtime-role persistence hardening.
- Tenancy/authentication/role-authorization/readiness/concurrency tests: 56/56 passed.
- PostgreSQL API persistence smoke: initial activation, lookup, renewal extension and cross-tenant read block passed.
- PostgreSQL checkout smoke: checkout order -> Razorpay order id persisted -> captured payment -> subscription/license activation passed.
- PostgreSQL renewal checkout smoke: renewal checkout order -> Razorpay order id persisted -> captured payment -> same subscription extension passed.
- PostgreSQL webhook activation smoke: pending order -> captured payment -> subscription/license activation passed.
- PostgreSQL webhook renewal smoke: pending renewal order -> captured payment -> same subscription extension passed.
- Neon free-staging smoke: subscription, renewal, provider route, payment and payment-event rows persisted and survived redeploy.
- Restricted runtime-role proof: effective database role `businessos_rls`, `BYPASSRLS=false`, with Tenant A/Tenant B isolation verified.
- CRM runtime schema proof: `businessos_crm` stays owner-provisioned; `businessos_rls` has USAGE + table DML but no schema CREATE, and staging runtime validates instead of attempting DDL. Owner provisioning is captured in `infra/businessos-crm-runtime-schema.sql`.
- Provider-order concurrency proof: simultaneous recurring webhook, ordinary payment webhook, webhook-vs-checkout-reconcile, and webhook-vs-admin-reconcile paths converge on one activation/renewal and one payment event.
- Release build: 0 warnings, 0 errors.
- Validity starts from captured payment date, not activation date.
- Quote commercial snapshot is carried into Order and Subscription without rereading mutable plan pricing.

## PostgreSQL tenant-isolation verification

Existing read-isolation proof:
- PASS:NO_CONTEXT
- PASS:TENANT_A
- PASS:TENANT_B

Commerce negative attack matrix:
- PASS:QUOTE_CROSS_ORG
- PASS:QUOTE_CROSS_PLAN_VERSION
- PASS:QUOTE_CROSS_OPPORTUNITY
- PASS:ORDER_CROSS_QUOTE
- PASS:SUBSCRIPTION_CROSS_ORDER
- PASS:SUBSCRIPTION_CROSS_PLAN_VERSION
- PASS:WRONG_TENANT_RLS_INSERT
- PASS:DUPLICATE_PAYMENT_ID
- PASS:TENANT_A_UPDATE_B=0
- PASS:TENANT_A_DELETE_B=0
- PASS:REJECTED_ROWS_ABSENT=0

The database uses FORCE ROW LEVEL SECURITY on Commerce tables plus same-tenant composite foreign keys. The payment index prevents duplicate non-null PaymentId values within the same tenant.

## API activation wiring verification

Backend endpoints added under `/api/commerce`:
- `POST /api/commerce/checkout/initial`
- `POST /api/commerce/subscriptions/{subscriptionId}/checkout/renewal`
- `POST /api/commerce/activations/initial`
- `POST /api/commerce/subscriptions/{subscriptionId}/renewals`
- `GET /api/commerce/subscriptions/{subscriptionId}`

Verified initial activation smoke response returned a same-tenant subscription and license with validity `2026-09-14` to `2027-09-13`. API store tests verify same-tenant lookup, other-tenant isolation, renewal extension to `2028-09-13`, and entitlement synchronization. API now selects the PostgreSQL-backed store when `ConnectionStrings:Commerce` is configured, and uses the in-memory store only as local fallback.

## Renewal / Subscription Extension verification

Application rules verified:
- Early renewal extends from the existing expiry date.
- Renewal after expiry starts a new paid term from the renewal payment date.
- Renewal keeps the same subscription id; no replacement subscription/license is created.
- Renewal can move to a newer PlanVersion and refresh entitlement limits.
- Captured payment is mandatory before renewal activation.
- Cross-organisation and different-plan renewals are rejected.
- One-time billing cannot be used as a renewable term.

Persistence verification:
- `commerce_subscription_renewals` is tenant-scoped with FORCE RLS.
- Same-tenant FKs protect subscription, order and PlanVersion relationships.
- One renewal record per order is enforced.
- PASS:RENEWAL_WRONG_TENANT_RLS
- PASS:RENEWAL_CROSS_SUBSCRIPTION
- PASS:RENEWAL_CROSS_ORDER
- PASS:RENEWAL_CROSS_PLAN_VERSION
- PASS:RENEWAL_DUPLICATE_ORDER
- PASS:RENEWAL_TENANT_A_UPDATE_B=0
- PASS:RENEWAL_TENANT_A_DELETE_B=0
- Negative proof runs inside a transaction and rolls back its temporary proof order.

Reusable proof: `infra/commerce-renewal-negative-proof.sql`.

## Razorpay webhook activation verification

Webhook endpoint added under `/api/payments`:
- `POST /api/payments/webhooks/razorpay`

Verification completed:
- Razorpay signature verification is required through `X-Razorpay-Signature`.
- Webhook route is excluded from tenant authentication middleware because Razorpay will not send tenant API headers.
- Tenant, commerce order, product and subscription routing are read from signed webhook notes.
- Captured payment can activate a previously pending Commerce order.
- Captured renewal payment can extend the same existing subscription.
- Duplicate captured webhook processing returns the existing subscription or renewal instead of creating another row.

## Checkout order creation verification

Checkout endpoints create pending Commerce orders before payment:
- `POST /api/commerce/checkout/initial`
- `POST /api/commerce/subscriptions/{subscriptionId}/checkout/renewal`

Verification completed:
- Initial checkout returns a pending internal Commerce order id and Razorpay notes.
- Renewal checkout returns the same note contract plus `subscriptionId`.
- Webhook activation uses the signed notes to match tenant, order, product and subscription.
- In-memory fallback and PostgreSQL store both support checkout -> captured payment -> activation.

## Razorpay order creation verification

Checkout endpoints now use the real Razorpay Orders API client boundary:
- `POST https://api.razorpay.com/v1/orders`
- HTTP Basic auth is applied inside the server-side client only.
- Checkout responses expose the Razorpay order id and public key id only; the key secret is never returned.
- The order request sends amount in currency subunits, currency, receipt and signed activation notes.
- Receipt uses the internal Commerce order id, shortened to the Razorpay receipt length constraint.
- Unit tests verify HTTP method/path, Basic auth presence, amount/currency/receipt/notes serialization and returned `razorpay_order_id` mapping.

## Razorpay order id persistence verification

Commerce orders now persist the provider order id after Razorpay order creation:
- `commerce_orders.razorpay_order_id` stores the Razorpay order reference.
- A tenant-scoped unique index prevents two Commerce orders from sharing the same Razorpay order id.
- Webhook activation can resolve the internal Commerce order from the Razorpay payment `order_id` when the internal order note is absent or malformed.

## Provider order route recovery verification

A dedicated provider routing table now supports note-free Razorpay webhook recovery:
- `commerce_provider_order_routes` maps provider + provider order id to tenant, Commerce order, product code and optional subscription id.
- The table is intentionally outside tenant RLS so the signed webhook can discover the tenant before entering Commerce's FORCE RLS path.
- The route table has owner/grant setup for `bos_owner` and `bos_app`.
- Webhook parsing allows missing notes and keeps the provider Razorpay `order_id` as a recoverable order reference.
- If webhook notes are present, conflicting tenant, subscription or product values are rejected.
- After route recovery, activation still happens through the normal tenant-scoped Commerce store.
- In-memory and PostgreSQL tests verify provider route lookup and note-free captured payment activation from Razorpay order id.

## Razorpay checkout success verification

Frontend payment-success verification is now available:
- `POST /api/payments/checkout/razorpay/verify`
- The endpoint verifies `razorpay_order_id`, `razorpay_payment_id` and `razorpay_signature` using the server-side Razorpay key secret.
- It uses `order_id|payment_id` HMAC verification and does not expose the key secret.
- The endpoint resolves the provider order route and returns whether activation is still pending webhook processing or already activated.
- It does not fulfil/activate by itself; fulfilment remains webhook-driven.
- In-memory and PostgreSQL tests verify pending and activated status lookup for initial and renewal checkout flows.

## Razorpay payment fetch reconciliation verification

A delayed-webhook recovery endpoint now verifies Checkout signature, fetches provider payment state server-side, and reconciles safely:
- `POST /api/payments/checkout/razorpay/reconcile`.
- The endpoint fetches `GET /v1/payments/{razorpay_payment_id}` using server-side Razorpay credentials.
- It rejects provider payment responses whose payment id or order id does not match the signed checkout payload.
- Pending or failed provider payments are recorded without activating entitlements.
- Captured provider payments are passed through the same idempotent PaymentProcessor and Commerce activation path used by webhooks.
- Initial purchase and renewal reconciliation continue to use provider-order routes plus tenant-scoped Commerce activation.
- Tests verify payment fetch HTTP shape, Basic auth, captured mapping, in-memory reconciliation and PostgreSQL status lookup before/after activation.

## Persistent payment ledger verification

Payment webhook and reconciliation processing now uses `IPaymentEventStore`:
- PostgreSQL mode persists provider payment records in `payment_gateway_records`.
- Provider event ids are persisted in `payment_gateway_events` for restart-safe idempotency.
- Duplicate event replay after a new store instance returns the existing payment instead of creating a second payment.
- Pending payment records can be upgraded to captured when Razorpay later confirms capture.
- Reusing the same provider payment id for a different provider order is rejected.
- A provider-order concurrency gate serializes the complete payment-processing/activation critical section for webhook, checkout reconciliation, admin reconciliation and authenticated FreeTesting capture flows targeting the same provider order.
- InMemory uses a keyed semaphore; PostgreSQL uses a session-level advisory lock keyed by provider + provider order id and held until activation/reconciliation completes.
- Concurrent integration tests prove duplicate webhook delivery and webhook-vs-checkout-reconcile converge on one activation/subscription with exactly one non-duplicate payment event.
- Local fallback keeps the existing in-memory `PaymentProcessor` behavior for development.

## Commerce admin status verification

Admin status endpoint added under `/api/commerce/admin`:
- `GET /api/commerce/admin/status`

Verification completed:
- Endpoint is tenant-protected through the normal POC API key middleware.
- Response includes pending orders, linked provider order ids, payment ledger rows, activations, renewals and reconciliation counters.
- Reconciliation status identifies awaiting payment, payment pending, payment failed, captured pending activation, initial activation completed and renewal completed.
- In-memory tests verify pending order plus payment ledger visibility and post-activation dashboard state.
- PostgreSQL smoke verifies tenant-scoped admin order snapshot plus linked payment ledger lookup.

## Admin manual reconciliation verification

Admin endpoint added under `/api/commerce/admin`:
- `POST /api/commerce/admin/razorpay/orders/{razorpayOrderId}/reconcile`

Verification completed:
- Endpoint is tenant-protected through the existing admin/tenant middleware.
- Admin reconciliation resolves the existing provider order route before any activation.
- Cross-tenant Razorpay order routes are hidden from the requesting tenant.
- Server-side Razorpay order-payment fetch uses `GET /v1/orders/{orderId}/payments`.
- Captured payment is preferred when multiple provider payments are returned.
- Pending/failed provider payments are recorded in the payment ledger without activation.
- Captured provider payment activates the initial or renewal order through the existing idempotent Commerce path.
- API and PostgreSQL smoke tests verify the order-payment fetch and manual reconciliation support path.

## Production authentication and admin role verification

Authentication/authorization checkpoint completed:
- Production mode can authenticate using configured Bearer tokens under `BusinessOS:Auth:BearerTokens`.
- POC API keys remain available only in Development, or when explicitly enabled by `BusinessOS:Auth:AllowPocApiKeys=true`.
- Production tests verify POC keys are rejected outside Development.
- Configured Bearer token tests verify tenant resolution without hard-coded source-code credentials.
- Commerce admin endpoints now require one of: Owner, Admin, FinanceAdmin or BillingAdmin.
- Non-admin tenant roles are rejected with HTTP 403 for Commerce admin status and reconciliation routes.
- `infra/identity-proof-setup.sql` grants only required identity SELECT access to `bos_app` for PostgreSQL identity lookup.

## Production readiness verification

Readiness endpoint added:
- `GET /health/ready`

Verification completed:
- The endpoint is anonymous and returns no secret values.
- Production readiness fails with HTTP 503 when required Commerce, Identity, Razorpay or Bearer-token configuration is missing.
- Production readiness fails when POC API keys are explicitly enabled.
- Production readiness returns HTTP 200 only when all required external configuration is present and no unsafe production auth flags are enabled.

## Free staging readiness mode

Two Render-hosted `Staging` / `FreeTesting` modes are verified:
- Primary API: `BusinessOS:StorageMode=InMemory` with `BusinessOS:Payments:Mode=RazorpayTestPending` for lightweight public UI testing.
- Secondary API: `BusinessOS:StorageMode=Postgres` backed by the dedicated Neon BusinessOS database, with `BusinessOS:Storage:RuntimeRole=businessos_rls` for restricted RLS-enforced runtime access.

This is intentionally for development and final testing before paid production migration. Live Razorpay credentials are not required. When `StorageMode=Postgres`, `/health/ready` now requires `ConnectionStrings:Commerce`; a Postgres-labelled deployment cannot silently fall back to InMemory while reporting ready.

Production readiness remains strict: when hosted as `Production`, the API still requires Commerce and Identity connection strings, Razorpay key id, Razorpay key secret, Razorpay webhook secret, and configured bearer tokens. FreeTesting-only smoke endpoints stay disabled in Production, and the Postgres commerce smoke additionally requires the explicit `BusinessOS:Testing:EnablePostgresSmoke=true` kill-switch.

Verified by the FreeTesting InMemory/Postgres readiness tests, missing-Commerce-connection regression, production missing-config/unsafe-POC checks, and live Neon persistence/redeploy proof.

## Free staging Razorpay order simulation

FreeTesting checkout support added for staging development without live Razorpay keys:
- When `BusinessOS:Payments:Mode=RazorpayTestPending` and the environment is not Production, checkout order creation uses an in-process Razorpay order simulator.
- The simulator returns `order_free_test_*` provider order ids and keeps the same note contract used by live Razorpay orders.
- The checkout response uses public key id `rzp_test_free_testing` only when no configured Razorpay key id exists.
- Production never uses this simulator, even if the payment mode flag is accidentally set.
- Regression tests verify both staging simulator behavior and production strictness.
- The reusable free-staging smoke script checks health, readiness, simulated checkout, activation, subscription lookup and admin status, and accepts `-ExpectedStorageMode InMemory|Postgres` so the same checks can target either staging API.

## FreeTesting payment capture simulator verification

A staging-only capture endpoint is now available for full free-platform checkout testing:
- `POST /api/testing/payments/razorpay/orders/{razorpayOrderId}/capture`

Verification completed:
- Endpoint is enabled only outside Production when `BusinessOS:DeploymentMode=FreeTesting` and `BusinessOS:Payments:Mode=RazorpayTestPending`.
- Production returns not found for the simulator endpoint even if the FreeTesting payment mode flag is set.
- The simulator records a captured payment through the same `IPaymentEventStore` boundary used by webhook/reconciliation flows.
- Captured initial checkout orders activate through the existing tenant-scoped Commerce activation path.
- The reusable `scripts/free-staging-smoke.ps1` now verifies checkout, simulated capture, subscription lookup and admin status end-to-end.

## Free Staging Browser Checkout Test Page

A staging-only browser test page is available at `/testing/free-checkout` when
`ASPNETCORE_ENVIRONMENT=Staging`, `BusinessOS:DeploymentMode=FreeTesting`, and
`BusinessOS:Payments:Mode=RazorpayTestPending`.

The page contains no bearer token or server secret. Testers must paste a staging
bearer token manually. The page then calls protected APIs for checkout, simulated
capture, subscription lookup, and admin status.

Production returns 404 for this page, even if a free-testing payment mode value
is accidentally configured.

## Subscription entitlement status and period-end cancellation

Secure entitlement status support is available at:
- `GET /api/commerce/subscriptions/{subscriptionId}/entitlement`
- `POST /api/commerce/subscriptions/{subscriptionId}/cancel-at-period-end`

The entitlement response maps tenant, organisation, subscription, license, product,
plan and plan-version identity together with the current entitlement snapshot.

Status evaluation rules:
- Paid term before `ValidUntil`: `Active`.
- Uncancelled subscription after expiry: `Grace` for 7 days with renewal status `payment_pending`.
- After the 7-day grace window: `Expired` with renewal status `renewal_required`.
- Cancel-at-period-end disables auto-renewal but keeps the already-paid term `Active` through `ValidUntil`.
- A cancelled subscription expires at term end without entering renewal grace.

Cancellation is idempotent, tenant-scoped and restricted to Commerce admin roles.
Cancelled subscriptions cannot create a renewal checkout order unless reactivation
support is explicitly added later.

Verification covers evaluator boundaries, tenant isolation, license mapping,
in-memory persistence, PostgreSQL persistence path and authenticated API flow.

## Website CORS for free staging

The API uses a restricted CORS policy for browser/frontend integration.
Default allowed production website origins:

- `https://orrbitrepair.com`
- `https://www.orrbitrepair.com`

Non-production defaults also include the staging API origin and local dev ports for testing.
The policy allows `GET`, `POST`, `OPTIONS`, and request headers including `Authorization` and `Content-Type`.
It does not use wildcard origins and does not enable browser credentials.

For custom origins, set `BusinessOS:Cors:AllowedOrigins` / `BusinessOS__Cors__AllowedOrigins__0` style configuration.

## Razorpay AutoPay free-staging foundation

AutoPay setup is tenant-scoped and Commerce-admin protected:
- `POST /api/commerce/subscriptions/{subscriptionId}/autopay/setup`
- repeated setup returns the existing provider binding instead of creating duplicates
- FreeTesting uses synthetic `plan_free_test_annual` and `sub_free_test_*` identifiers
- Production requires configured Razorpay key/secret, provider plan id and billing-cycle count

Provider subscription mappings persist the internal tenant/subscription, Razorpay subscription
and plan ids, scheduled start, total cycle count, provider state, authorization URL and
auto-renew/cancellation flags. Signed `subscription.*` webhooks update this provider state.
The entitlement endpoint overlays that provider state without shortening paid access: `pending`
remains auto-renew eligible, while `halted`/terminal provider states expose `autoRenewEnabled=false`.
The paid term and existing 7-day post-`ValidUntil` grace calculation remain authoritative.

Period-end cancellation preserves BusinessOS entitlement through `ValidUntil`, while an
existing Razorpay AutoPay mandate is cancelled immediately so no future provider debit can
occur. The internal entitlement then expires at its paid term boundary.
