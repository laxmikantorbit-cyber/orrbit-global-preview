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
