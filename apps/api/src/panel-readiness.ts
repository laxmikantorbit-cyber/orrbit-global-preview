export type PanelModuleStatus = "ready" | "partial" | "pending";

export type PanelModuleReadiness = {
  key: string;
  name: string;
  status: PanelModuleStatus;
  summary: string;
};

const panelModules: PanelModuleReadiness[] = [
  { key: "owner-access", name: "Authentication & owner access", status: "ready", summary: "First-run owner setup, scrypt password hashing, HttpOnly sessions, logout and login throttling are implemented." },
  { key: "project-registry", name: "Project registry & environments", status: "ready", summary: "Protected project registry and environment configuration are available." },
  { key: "manual-onboarding", name: "Manual project onboarding", status: "ready", summary: "Projects can be registered manually with validation and audit evidence." },
  { key: "ai-onboarding", name: "AI-assisted onboarding", status: "ready", summary: "Plan-before-apply onboarding is implemented in development-safe mode." },
  { key: "existing-import", name: "Existing project import", status: "ready", summary: "Generic source planning, route/module capture, isolated archive acquisition, Docker-only build/preview, parity assessment and synthetic QA gating are implemented; real imports still require explicit owner unlock." },
  { key: "ai-dev-request", name: "AI development request planning", status: "ready", summary: "AI change requests now include impact/risk planning, persisted lifecycle state and budget-aware automation guardrails." },
  { key: "git-workspaces", name: "Git branches & workspaces", status: "ready", summary: "Protected feature workspace lifecycle, safe branch naming, persisted provider plans and review planning are implemented." },
  { key: "build-test", name: "Build & test validation", status: "ready", summary: "Project change workflows require recorded typecheck, tests, build and health evidence before preview." },
  { key: "preview", name: "Preview deployments & revisions", status: "ready", summary: "Generic development preview revisions are available after passing validation, using gated dry-run provider adapters." },
  { key: "approval-deploy", name: "Approval & controlled deployment", status: "ready", summary: "Owner approval follows passing validation and preview evidence; deployment execution remains hard-locked until explicitly enabled in a future safe phase." },
  { key: "health-release", name: "Health verification & release evidence", status: "ready", summary: "Release evidence bundles require source revision, build, tests and health verification; deployment execution remains locked." },
  { key: "rollback-history", name: "Rollback & version history", status: "ready", summary: "Verified release evidence feeds version history; rollback plans require approval while execution remains hard-locked." },
  { key: "domains-dns", name: "Domains & DNS proposals", status: "ready", summary: "Typed DNS create/update/delete proposals, approval/cancel lifecycle, restore-point requirement and hard execution lock are implemented." },
  { key: "secret-references", name: "Secret references", status: "ready", summary: "Environment-specific provider references are persisted while secret values are explicitly rejected and never stored." },
  { key: "audit-jobs", name: "Audit log & job history", status: "ready", summary: "Project job history and auditable events are persisted and surfaced." },
  { key: "cost-budgets", name: "AI/cloud cost tracking & budgets", status: "ready", summary: "Per-project budgets, usage ledger, warning thresholds and over-budget automation blocking are implemented." }
];

export function getPanelReadiness() {
  const ready = panelModules.filter((item) => item.status === "ready").length;
  const partial = panelModules.filter((item) => item.status === "partial").length;
  const pending = panelModules.filter((item) => item.status === "pending").length;
  const total = panelModules.length;
  const panelComplete = ready === total;
  const buildProgressPercent = Math.round(((ready + partial * 0.5) / total) * 100);
  return {
    panelComplete,
    realImportUnlocked: panelComplete && process.env.CONTROL_PANEL_COMPLETE === "true",
    completion: { ready, partial, pending, total, buildProgressPercent },
    standingRule: "Panel must be 100% complete before any real SaaS/website import, transfer or migration.",
    modules: panelModules
  };
}

export function isSyntheticFixtureHeader(value: unknown) {
  return typeof value === "string" && value.trim().toLowerCase() === "synthetic";
}
