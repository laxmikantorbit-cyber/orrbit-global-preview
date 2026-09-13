# Sales / Opportunity Verification

Verified on DESKTOP-2AH9MPI using .NET 10.0.400 and isolated PostgreSQL 16 on localhost port 55432.

## Application regression
- Sales tests: 9/9 PASS
- CRM: 7/7 PASS
- Customer 360: 10/10 PASS
- Identity: 6/6 PASS
- Licensing: 20/20 PASS
- Payments: 9/9 PASS
- Tenancy/API: 5/5 PASS
- Full solution: 66/66 PASS
- Release build: 0 warnings, 0 errors

## PostgreSQL verification
- No tenant context sees zero opportunities.
- Tenant A sees only its opportunity; Tenant B sees only its opportunity.
- Cross-tenant UPDATE and DELETE affect zero rows.
- Wrong-tenant INSERT is rejected by FORCE RLS.
- Cross-tenant Organisation reference is rejected by composite FK.
- Cross-tenant originating Lead reference is rejected by composite FK.
- Cross-tenant Owner membership reference is rejected by composite FK.
- All rejected negative-test rows were verified absent.
