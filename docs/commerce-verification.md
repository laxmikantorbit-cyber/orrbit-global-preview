# Commerce Foundation Verification

Date: 2026-09-14
Machine: DESKTOP-FOFADB8
Runtime: PostgreSQL 18.6, isolated user-space cluster on 127.0.0.1:55432
Database: `businessos_commerce`; latest isolated proof database: `businessos_commerce_iso_20260914_1850`

## Scope

Verified the Quote -> Order -> Subscription foundation and Subscription Renewal extension against the existing tenant, Customer 360, CRM/Sales, Catalog, Payments, and Licensing boundaries.

## Application verification

- Full solution regression: 93/93 tests passed.
- Commerce tests: 13/13 passed.
- Application activation bridge tests: 3/3 passed.
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
