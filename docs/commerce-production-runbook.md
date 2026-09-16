# Commerce Production Runbook

Date: 2026-09-16
Machine checkpoint: DESKTOP-FOFADB8

## Required production configuration

Use environment variables or a secrets manager. Do not commit real values.

Required keys:
- `ConnectionStrings__Commerce`
- `ConnectionStrings__Identity`
- `BusinessOS__StorageMode=Postgres`
- `Payments__RazorpayKeyId`
- `Payments__RazorpayKeySecret`
- `Payments__RazorpayWebhookSecret`
- `BusinessOS__Auth__BearerTokens__0__Token`
- `BusinessOS__Auth__BearerTokens__0__Subject`
- `BusinessOS__Auth__BearerTokens__0__TenantCode`

Optional hardening key when the database provider requires a managed login role:
- `BusinessOS__Storage__RuntimeRole=<restricted-no-bypass-role>`

Prefer a direct application login with `NOBYPASSRLS`, `NOCREATEROLE` and `NOCREATEDB`. If the provider-created login cannot drop elevated attributes, grant it membership in a restricted `NOLOGIN` role and configure `BusinessOS__Storage__RuntimeRole` so every opened Commerce/Payments connection executes `SET ROLE` before queries.

Reference template: `src/BusinessOS.Api/appsettings.Production.example.json`.

## Database setup sequence

1. Create or provision PostgreSQL databases for Commerce and Identity.
2. Apply Commerce schema from `infra/commerce-schema.sql`.
3. Apply Commerce owner/grant proof setup from `infra/commerce-proof-setup.sql`.
4. Apply Identity owner/grant setup from `infra/identity-proof-setup.sql`.
5. Seed active tenants, users and tenant memberships before issuing Bearer tokens.
6. Use only active memberships with one of these Commerce admin roles:
   - `Owner`
   - `Admin`
   - `FinanceAdmin`
   - `BillingAdmin`
7. Verify the runtime database identity cannot bypass RLS. If `BusinessOS__Storage__RuntimeRole` is used, verify `current_user` becomes that restricted role after `SET ROLE` and `rolbypassrls=false`.
8. Verify Tenant A/Tenant B read isolation with the same runtime identity used by the API before enabling production traffic.

## Razorpay setup

1. Use live Razorpay key id and key secret only in production secrets.
2. Configure Razorpay webhook URL:
   - `https://<production-host>/api/payments/webhooks/razorpay`
3. Store the Razorpay webhook secret in `Payments__RazorpayWebhookSecret`.
4. Checkout success verification endpoint for frontend:
   - `POST /api/payments/checkout/razorpay/verify`
5. Checkout payment reconciliation endpoint for delayed webhooks:
   - `POST /api/payments/checkout/razorpay/reconcile`
6. Admin manual reconciliation endpoint:
   - `POST /api/commerce/admin/razorpay/orders/{razorpayOrderId}/reconcile`

## Go-live checks

1. Confirm `/health` returns HTTP 200.
2. Confirm `/health/ready` returns HTTP 200.
3. Confirm `/health/ready` response does not list missing configuration and reports `StorageMode=Postgres`.
4. Confirm `BusinessOS:Auth:AllowPocApiKeys` is not enabled in Production.
5. Confirm `BusinessOS:Testing:EnablePostgresSmoke` is absent/false in Production.
6. Confirm production API calls use `Authorization: Bearer <token>`.
7. Confirm POC header `X-POC-Api-Key` is rejected in Production.
8. Confirm Commerce admin endpoints return HTTP 403 for non-admin roles.
9. Confirm Razorpay webhook signature rejection works with an invalid signature.
10. Run concurrency verification for duplicate webhook delivery and webhook-vs-reconciliation on the same provider order; one activation/renewal and one payment event must result.
11. Run the complete Release test suite before publishing a build.

## Post-go-live operating notes

- Webhook fulfilment remains the source of truth for activation.
- Checkout verification confirms frontend success but does not activate entitlements.
- Checkout reconciliation can recover delayed webhooks after signed checkout success.
- Admin manual reconciliation can recover by Razorpay order id when frontend data is unavailable.
- Payment processing is idempotent through the persistent payment ledger.
- Provider-order concurrency gating serializes webhook, checkout reconciliation, admin reconciliation and authenticated testing capture flows for the same Razorpay order. PostgreSQL uses an advisory lock held for the complete payment-processing/activation critical section.
- Commerce activations and renewals remain tenant-scoped behind FORCE RLS.

## Entitlement status operations

Current entitlement endpoint:
- `GET /api/commerce/subscriptions/{subscriptionId}/entitlement`

Period-end cancellation endpoint:
- `POST /api/commerce/subscriptions/{subscriptionId}/cancel-at-period-end`

Cancellation requires a Commerce admin role: Owner, Admin, FinanceAdmin or BillingAdmin.
It must not terminate an already-paid term early. The subscription remains active through
`ValidUntil`, auto-renewal is disabled, and the entitlement becomes expired at period end.

For uncancelled subscriptions, the entitlement API exposes a 7-day grace window after
`ValidUntil`. During grace the renewal status is `payment_pending`; after grace it becomes
`renewal_required`. Razorpay `pending`/`halted` provider states do not shorten an already-paid
term or the grace window. The entitlement response also surfaces the provider state; a halted or
terminal mandate makes `autoRenewEnabled=false` while paid access continues through its normal
term/grace boundary.

## Razorpay AutoPay configuration

Production AutoPay remains disabled until release-ready configuration is supplied. Required
settings are `Payments:RazorpayKeyId`, `Payments:RazorpayKeySecret`, a product-specific
`Payments:RazorpayAutoPay:PlanIds:<ProductCode>` (or fallback
`Payments:RazorpaySubscriptionPlanId`), and `Payments:RazorpayAutoPay:TotalCount`.

AutoPay setup creates a Razorpay Subscription beginning after the current paid `ValidUntil`
term. Subscription webhooks use the existing Razorpay webhook secret and update the stored
provider state. Do not enable live AutoPay before webhook delivery and production database
persistence are both verified.

`cancel-at-period-end` intentionally cancels any linked Razorpay mandate immediately while
keeping BusinessOS access active through the already-paid `ValidUntil`. This separates
billing cancellation from entitlement termination and prevents future unintended charges.
