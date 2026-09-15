# Commerce Production Runbook

Date: 2026-09-15
Machine checkpoint: DESKTOP-FOFADB8

## Required production configuration

Use environment variables or a secrets manager. Do not commit real values.

Required keys:
- `ConnectionStrings__Commerce`
- `ConnectionStrings__Identity`
- `Payments__RazorpayKeyId`
- `Payments__RazorpayKeySecret`
- `Payments__RazorpayWebhookSecret`
- `BusinessOS__Auth__BearerTokens__0__Token`
- `BusinessOS__Auth__BearerTokens__0__Subject`
- `BusinessOS__Auth__BearerTokens__0__TenantCode`

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
3. Confirm `/health/ready` response does not list missing configuration.
4. Confirm `BusinessOS:Auth:AllowPocApiKeys` is not enabled in Production.
5. Confirm production API calls use `Authorization: Bearer <token>`.
6. Confirm POC header `X-POC-Api-Key` is rejected in Production.
7. Confirm Commerce admin endpoints return HTTP 403 for non-admin roles.
8. Confirm Razorpay webhook signature rejection works with an invalid signature.
9. Run the Release test suite before publishing a build.

## Post-go-live operating notes

- Webhook fulfilment remains the source of truth for activation.
- Checkout verification confirms frontend success but does not activate entitlements.
- Checkout reconciliation can recover delayed webhooks after signed checkout success.
- Admin manual reconciliation can recover by Razorpay order id when frontend data is unavailable.
- Payment processing is idempotent through the persistent payment ledger.
- Commerce activations and renewals remain tenant-scoped behind FORCE RLS.
