# CRM Foundation Verification

Verified on DESKTOP-2AH9MPI using .NET 10.0.400 and an isolated PostgreSQL 16 cluster on localhost port 55432.

## Domain and application
- Lead is a permanent entity linked to OrganisationId; it is not customer identity.
- Lead lifecycle and closed-state rules are enforced.
- Commercial attribution dimensions remain separate.
- Repository is tenant scoped and rejects duplicate Lead IDs.
- BusinessOS.Crm.Tests: 7/7 PASS.
- Full solution regression: 57/57 PASS.
- Release build: 0 warnings, 0 errors.

## PostgreSQL proof
- No tenant context sees zero CRM leads.
- Tenant A sees only Alpha Lead.
- Tenant B sees only Beta Lead.
- Tenant A cross-tenant UPDATE affects zero rows.
- Tenant A cross-tenant DELETE affects zero rows.
- Tenant A inserting a Tenant B lead is rejected by RLS.
- Cross-tenant Organisation linkage is rejected by composite FK (tenant_id, organisation_id).
- Both rejected negative-test rows were verified absent.

The isolated PostgreSQL cluster was stopped after verification. Production target remains PostgreSQL 18 and must repeat these checks in staging.
