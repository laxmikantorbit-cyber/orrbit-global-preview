# Commerce Foundation Verification

Date: 2026-09-15
Machine: DESKTOP-FOFADB8
Runtime: PostgreSQL 18.6, isolated user-space cluster on 127.0.0.1:55432
Database: `businessos_commerce`; latest isolated proof database: `businessos_commerce_iso_20260914_1850`; latest API persistence smoke database: `businessos_commerce_api_pg_20260914_2226`

## Scope

Verified the Quote -> Order -> Subscription foundation and Subscription Renewal extension against the existing tenant, Customer 360, CRM/Sales, Catalog, Payments, and Licensing boundaries.

## Application verification

- Full solution regression: 126/126 tests passed.
- Commerce tests: 13/13 passed.
- Application activation bridge tests: 3/3 passed.
- API activation/checkout/webhook/Razorpay order/provider-route/checkout-success/reconciliation/payment-ledger/admin-status/manual-reconcile tests: 29/29 passed.
- Tenancy/authentication/role-authorization tests: 9/9 passed.
- PostgreSQL API persistence smoke: initial activation, lookup, renewal extension and cross-tenant read block passed.
- PostgreSQL checkout smoke: checkout order -> Razorpay order id persisted -> captured payment -> subscription/license activation passed.
- PostgreSQL renewal checkout smoke: renewal checkout order -> Razorpay order id persisted -> captured payment -> same subscription extension passed.
- PostgreSQL webhook activation smoke: pending order -> captured payment -> subscription/license activation passed.
- PostgreSQL webhook renewal smoke: pending renewal order -> captured payment -> same subscription extension passed.
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
