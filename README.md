# oRRbit AI Control Plane

Private control platform for managing oRRbit websites and SaaS products.

## Primary outcomes
- Add/import websites and SaaS projects manually or through AI prompts.
- Develop changes in isolated branches, run validation, preview, approve, deploy and roll back.
- Keep hosted applications independent from the control plane runtime.
- Use GitHub as source of truth and provider adapters for infrastructure.
- Keep production, DNS, secrets, payments and destructive database actions protected.

## Post-completion pilots
These remain locked until the panel completion gate reaches 100% and real import is explicitly unlocked.
1. Martial Arts ERP — first development-mode SaaS pilot.
2. orrbit.in — website onboarding.
3. orrbitrepair.com — website + API onboarding.

## Permanent panel-first rule
Complete the Control Plane panel to 100% before any real SaaS/website import, transfer or migration. Before that point, only explicitly marked synthetic fixtures may exercise import mechanics for QA. See `docs/PANEL_COMPLETION_RULE.md`.

## V1 rule
AI may analyse, plan, build and test. Production-impacting operations require the platform policy engine and explicit approval.

## Control-plane persistence
Set `CONTROL_DATABASE_URL` to use PostgreSQL for projects, plans, import workspaces, source acquisitions, source builds, preview executions, jobs and audit events.
Without it, local development falls back to in-memory stores. Apply the control-plane migrations in this order:
`0001_control_plane.sql` → `0002_import_engine.sql` → `0002_project_source_and_plans.sql` →
`0003_import_workspaces.sql` → `0004_import_execution_jobs.sql` → `0005_source_acquisitions.sql` → `0006_source_build_jobs.sql` → `0007_owner_auth.sql` → `0008_development_workspaces.sql`.
Source ZIPs are accepted only through the panel and extracted under the isolated control-plane `runtime/import-inbox`; production application files are never used as the extraction target. Actual source builds require Docker: dependency lifecycle scripts are disabled, the build stage runs after the sandbox network is disconnected, and there is no host-execution fallback. Safe static build output can be previewed from an isolated localhost-only container.

See docs/V1_SPEC.md, docs/SECURITY_POLICY.md and docs/PROJECT_MANIFEST_SPEC.md.