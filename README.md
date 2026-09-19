# oRRbit AI Control Plane

Private control platform for managing oRRbit websites and SaaS products.

## Primary outcomes
- Add/import websites and SaaS projects manually or through AI prompts.
- Develop changes in isolated branches, run validation, preview, approve, deploy and roll back.
- Keep hosted applications independent from the control plane runtime.
- Use GitHub as source of truth and provider adapters for infrastructure.
- Keep production, DNS, secrets, payments and destructive database actions protected.

## Initial pilots
1. Martial Arts ERP — first development-mode SaaS pilot.
2. orrbit.in — website onboarding.
3. orrbitrepair.com — website + API onboarding.

## V1 rule
AI may analyse, plan, build and test. Production-impacting operations require the platform policy engine and explicit approval.

## Control-plane persistence
Set `CONTROL_DATABASE_URL` to use PostgreSQL for projects, AI provisioning plans, jobs and audit events.
Without it, local development falls back to an in-memory registry. Apply `infra/db/0001_control_plane.sql`
and then `infra/db/0002_project_source_and_plans.sql` to a dedicated control-plane database.

See docs/V1_SPEC.md, docs/SECURITY_POLICY.md and docs/PROJECT_MANIFEST_SPEC.md.