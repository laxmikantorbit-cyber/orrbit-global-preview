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
  { key: "existing-import", name: "Existing project import", status: "partial", summary: "Planning, capture, acquisition and sandbox build foundations exist; real imports remain locked." },
  { key: "ai-dev-request", name: "AI development request planning", status: "partial", summary: "Planning primitives exist, but full change-request impact workflow is not complete." },
  { key: "git-workspaces", name: "Git branches & workspaces", status: "pending", summary: "Managed feature branch/workspace lifecycle is still required." },
  { key: "build-test", name: "Build & test validation", status: "partial", summary: "Isolated source build exists; project-wide validation orchestration is still required." },
  { key: "preview", name: "Preview deployments & revisions", status: "partial", summary: "Safe local preview exists; generic preview/revision workflow is still required." },
  { key: "approval-deploy", name: "Approval & controlled deployment", status: "partial", summary: "Approval primitives exist while real deployment execution remains intentionally locked." },
  { key: "health-release", name: "Health verification & release evidence", status: "pending", summary: "Project health checks and release evidence bundles are still required." },
  { key: "rollback-history", name: "Rollback & version history", status: "pending", summary: "Release version ledger and safe rollback workflow are still required." },
  { key: "domains-dns", name: "Domains & DNS proposals", status: "pending", summary: "Typed domain/DNS proposal and approval workflow is still required." },
  { key: "secret-references", name: "Secret references", status: "pending", summary: "Provider-backed secret references must be added without exposing secret values." },
  { key: "audit-jobs", name: "Audit log & job history", status: "ready", summary: "Project job history and auditable events are persisted and surfaced." },
  { key: "cost-budgets", name: "AI/cloud cost tracking & budgets", status: "pending", summary: "Usage ledger, budget policy and alerts are still required." }
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
