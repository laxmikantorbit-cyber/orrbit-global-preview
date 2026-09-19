import { randomUUID } from "node:crypto";
import type { EnvironmentName, ProjectType, RiskLevel } from "@orrbit/contracts";

export type ImportSourceType = "chatgpt-sites" | "live-url" | "github-repository" | "zip" | "local-source";
export type ImportPlanStatus = "plan_ready" | "approved" | "cancelled";

export interface ImportRouteInventoryItem {
  path: string;
  kind: "page" | "api" | "asset" | "unknown";
  status: "needs_capture" | "captured" | "manual_review";
  notes?: string;
}

export interface ImportManifestDraft {
  projectName: string;
  projectType: ProjectType;
  targetEnvironment: EnvironmentName;
  productionProtected: boolean;
  environments: EnvironmentName[];
}
export interface ProjectImportPlan {
  id: string;
  sourceType: ImportSourceType;
  sourceRef: string;
  requestedProjectName: string;
  projectType: ProjectType;
  targetEnvironment: EnvironmentName;
  risk: RiskLevel;
  status: ImportPlanStatus;
  routeInventory: ImportRouteInventoryItem[];
  manifestDraft: ImportManifestDraft;
  actions: string[];
  blockedActions: string[];
  createdAt: string;
  updatedAt: string;
}

export interface CreateImportPlanInput {
  sourceType: ImportSourceType;
  sourceRef: string;
  projectName: string;
  projectType: ProjectType;
  targetEnvironment?: EnvironmentName;
  knownRoutes?: string[];
}

function inferRoutes(input: CreateImportPlanInput): ImportRouteInventoryItem[] {
  const rawRoutes = input.knownRoutes?.length
    ? input.knownRoutes
    : ["/", "/login", "/dashboard", "/settings"];
  return rawRoutes.slice(0, 100).map((path) => ({
    path: path.startsWith("/") ? path : `/${path}`,
    kind: path.startsWith("/api") ? "api" : "page",
    status: "needs_capture",
    notes: "Inventory placeholder; actual capture happens in the import workspace stage."
  }));
}

function classifyImportRisk(input: CreateImportPlanInput): RiskLevel {
  if (input.targetEnvironment === "production") return "high";
  if (input.projectType === "saas" || input.projectType === "erp-crm") return "medium";
  return "low";
}
export function createProjectImportPlan(input: CreateImportPlanInput): ProjectImportPlan {
  const targetEnvironment = input.targetEnvironment ?? "development";
  const now = new Date().toISOString();
  const productionProtected = true;
  return {
    id: randomUUID(),
    sourceType: input.sourceType,
    sourceRef: input.sourceRef.trim(),
    requestedProjectName: input.projectName.trim(),
    projectType: input.projectType,
    targetEnvironment,
    risk: classifyImportRisk({ ...input, targetEnvironment }),
    status: "plan_ready",
    routeInventory: inferRoutes(input),
    manifestDraft: {
      projectName: input.projectName.trim(),
      projectType: input.projectType,
      targetEnvironment,
      productionProtected,
      environments: [targetEnvironment]
    },
    actions: [
      "Capture source inventory",
      "Create isolated import workspace",
      "Draft project manifest",
      "Run parity and safety checks before any deploy"
    ],
    blockedActions: [
      "No production deployment",
      "No DNS change",
      "No live payment change",
      "No destructive database action"
    ],
    createdAt: now,
    updatedAt: now
  };
}

export function validateImportSource(input: CreateImportPlanInput): string | null {
  if (!input.sourceRef?.trim()) return "sourceRef_required";
  if (!input.projectName?.trim()) return "projectName_required";
  if (input.targetEnvironment && input.targetEnvironment !== "development") return "only_development_import_enabled_in_v1";
  return null;
}

const martialArtsRoutes = [
  "/", "/login", "/dashboard", "/academies", "/branches", "/students",
  "/guardians", "/instructors", "/attendance", "/memberships", "/fees",
  "/belt-ranks", "/grading-exams", "/class-schedule",
  "/events-tournaments", "/reports", "/settings"
];

export const martialArtsPilotModules = [
  "Academy/Tenant setup",
  "Branch management",
  "Student/member management",
  "Parent/guardian records",
  "Instructor management",
  "Attendance",
  "Membership plans",
  "Fee collection",
  "Belt/rank history",
  "Grading/examination",
  "Class scheduling",
  "Events/tournaments",
  "Reports",
  "Role-based access"
];
export const martialArtsPilotAcceptance = [
  "Create import plan without touching production",
  "Mark source reference as pending when ChatGPT Sites reference is missing",
  "Create development-only protected project after owner approval",
  "Keep production, DNS, live payment and destructive DB actions blocked",
  "Generate route inventory and manifest draft for SaaS development mode"
];

export function createMartialArtsErpPilotPlan(input?: { sourceRef?: string }): ProjectImportPlan & {
  pilot: {
    sourceReferenceStatus: "provided" | "pending";
    modules: string[];
    acceptance: string[];
  };
} {
  const now = new Date().toISOString();
  const sourceRef = input?.sourceRef?.trim() || "PENDING_CHATGPT_SITES_REFERENCE";
  const sourcePending = sourceRef === "PENDING_CHATGPT_SITES_REFERENCE";
  const plan = createProjectImportPlan({
    sourceType: "chatgpt-sites",
    sourceRef,
    projectName: "Martial Arts ERP",
    projectType: "saas",
    targetEnvironment: "development",
    knownRoutes: martialArtsRoutes
  });
  return {
    ...plan,
    actions: [
      "Confirm or attach ChatGPT Sites source reference",
      "Capture current screens/routes/modules as reference inventory",
      "Create isolated development SaaS workspace",
      "Draft multi-tenant SaaS manifest",
      "Run parity/safety checks before any preview or deploy"
    ],
    blockedActions: [...plan.blockedActions, "No customer/tenant production data import", "No production domain connection"],
    routeInventory: plan.routeInventory.map((route) => ({
      ...route,
      notes: sourcePending
        ? "Pilot placeholder; source reference must be confirmed before capture."
        : "Capture from confirmed ChatGPT Sites reference during import workspace stage."
    })),
    pilot: {
      sourceReferenceStatus: sourcePending ? "pending" : "provided",
      modules: martialArtsPilotModules,
      acceptance: martialArtsPilotAcceptance
    }
  };
}

export type ImportWorkspaceStatus = "source_pending" | "capture_ready" | "capturing" | "capture_complete" | "blocked";
export type CaptureStatus = "pending" | "blocked" | "captured" | "manual_review";

export interface CaptureChecklistItem {
  key: string;
  label: string;
  status: CaptureStatus;
  blocker?: string;
}

export interface ModuleCaptureItem {
  name: string;
  status: CaptureStatus;
  notes?: string;
}
export interface ImportDeployGate {
  canDeploy: boolean;
  targetEnvironment: EnvironmentName;
  blockers: string[];
}

export interface ProjectImportWorkspace {
  id: string;
  importPlanId: string;
  projectId?: string;
  projectName: string;
  sourceType: ImportSourceType;
  sourceRef: string;
  sourceReferenceStatus: "provided" | "pending";
  status: ImportWorkspaceStatus;
  routeCapture: ImportRouteInventoryItem[];
  moduleCapture: ModuleCaptureItem[];
  captureChecklist: CaptureChecklistItem[];
  deployGate: ImportDeployGate;
  createdAt: string;
  updatedAt: string;
}
function importSourceIsPending(plan: ProjectImportPlan): boolean {
  return !plan.sourceRef || plan.sourceRef.startsWith("PENDING_");
}

function buildWorkspaceChecklist(sourcePending: boolean): CaptureChecklistItem[] {
  return [
    { key: "source-reference", label: "Confirm ChatGPT Sites source reference", status: sourcePending ? "blocked" : "captured", blocker: sourcePending ? "Source reference is required before capture." : undefined },
    { key: "route-capture", label: "Capture route/page inventory", status: sourcePending ? "blocked" : "pending", blocker: sourcePending ? "Blocked until source reference is confirmed." : undefined },
    { key: "module-capture", label: "Capture SaaS module inventory", status: sourcePending ? "blocked" : "pending", blocker: sourcePending ? "Blocked until source reference is confirmed." : undefined },
    { key: "manifest-review", label: "Review generated project manifest", status: "pending" },
    { key: "no-production-touch", label: "Keep production/DNS/live payment blocked", status: "captured" }
  ];
}
export function createImportWorkspaceFromPlan(plan: ProjectImportPlan, input?: { projectId?: string }): ProjectImportWorkspace {
  const now = new Date().toISOString();
  const sourcePending = importSourceIsPending(plan);
  const blockers = [
    ...(sourcePending ? ["ChatGPT Sites source reference must be confirmed before capture."] : []),
    "Route/page capture is not complete.",
    "Module inventory capture is not complete.",
    "Preview/deploy is disabled until capture checklist is verified."
  ];
  return {
    id: randomUUID(),
    importPlanId: plan.id,
    projectId: input?.projectId,
    projectName: plan.requestedProjectName,
    sourceType: plan.sourceType,
    sourceRef: plan.sourceRef,
    sourceReferenceStatus: sourcePending ? "pending" : "provided",
    status: sourcePending ? "source_pending" : "capture_ready",
    routeCapture: plan.routeInventory.map((route) => ({ ...route, status: sourcePending ? "manual_review" : route.status })),
    moduleCapture: plan.requestedProjectName === "Martial Arts ERP" ? martialArtsPilotModules.map((name) => ({ name, status: sourcePending ? "blocked" : "pending" })) : [],
    captureChecklist: buildWorkspaceChecklist(sourcePending),
    deployGate: { canDeploy: false, targetEnvironment: plan.targetEnvironment, blockers },
    createdAt: now,
    updatedAt: now
  };
}

export function evaluateImportWorkspaceDeployGate(workspace: ProjectImportWorkspace): ImportDeployGate {
  const checklistBlockers = workspace.captureChecklist
    .filter((item) => item.status !== "captured")
    .map((item) => item.blocker || `${item.label} is not complete.`);
  const routeBlocker = workspace.routeCapture.some((route) => route.status !== "captured")
    ? "All route/page capture records must be captured before deploy."
    : null;
  const moduleBlocker = workspace.moduleCapture.some((item) => item.status !== "captured")
    ? "All SaaS module capture records must be captured before deploy."
    : null;
  const sourceBlocker = workspace.sourceReferenceStatus === "pending"
    ? "Source reference must be confirmed before deploy."
    : null;
  const blockers = [...checklistBlockers, routeBlocker, moduleBlocker, sourceBlocker]
    .filter((x): x is string => Boolean(x));
  return {
    canDeploy: blockers.length === 0 && workspace.status === "capture_complete",
    targetEnvironment: "development",
    blockers
  };
}
