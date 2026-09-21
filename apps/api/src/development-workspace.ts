import { randomUUID } from "node:crypto";
import type { ProviderActionPlan, SourceControlProvider } from "@orrbit/provider-adapters";

export type DevelopmentWorkspaceStatus =
  | "planned"
  | "branch_plan_ready"
  | "review_plan_ready"
  | "cancelled";

export type DevelopmentWorkspace = {
  id: string;
  projectId: string;
  repositoryFullName: string;
  baseBranch: string;
  branchName: string;
  requestSummary: string;
  status: DevelopmentWorkspaceStatus;
  branchPlan: ProviderActionPlan | null;
  reviewPlan: ProviderActionPlan | null;
  actualBranchCreated: boolean;
  protections: {
    developmentOnly: true;
    productionBranchLocked: true;
    directMainWriteDisabled: true;
    providerExecutionGated: true;
  };
  createdAt: string;
  updatedAt: string;
};

function slug(value: string) {
  const normalized = value.toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 42);
  return normalized || "change";
}

function safeBaseBranch(value: string) {
  const trimmed = value.trim();
  if (!trimmed || trimmed.length > 120) throw new Error("invalid_base_branch");
  if (trimmed.startsWith("-") || trimmed.includes("..") || /[~^:?*\[\\\s]/.test(trimmed)) {
    throw new Error("invalid_base_branch");
  }
  return trimmed;
}

export function createDevelopmentWorkspace(input: {
  projectId: string;
  repositoryFullName?: string;
  defaultBranch?: string;
  requestSummary?: string;
  baseBranch?: string;
}): DevelopmentWorkspace {
  if (!input.repositoryFullName?.trim()) throw new Error("repository_required_for_git_workspace");
  const requestSummary = input.requestSummary?.trim() ?? "";
  if (requestSummary.length < 5 || requestSummary.length > 240) throw new Error("workspace_summary_5_to_240_characters");
  const baseBranch = safeBaseBranch(input.baseBranch?.trim() || input.defaultBranch?.trim() || "main");
  const id = randomUUID();
  const now = new Date().toISOString();
  return {
    id,
    projectId: input.projectId,
    repositoryFullName: input.repositoryFullName.trim(),
    baseBranch,
    branchName: `orrbit/${slug(requestSummary)}-${id.slice(0, 8)}`,
    requestSummary,
    status: "planned",
    branchPlan: null,
    reviewPlan: null,
    actualBranchCreated: false,
    protections: {
      developmentOnly: true,
      productionBranchLocked: true,
      directMainWriteDisabled: true,
      providerExecutionGated: true
    },
    createdAt: now,
    updatedAt: now
  };
}

export async function prepareBranchPlan(workspace: DevelopmentWorkspace, provider: SourceControlProvider) {
  if (workspace.status !== "planned") throw new Error("workspace_branch_plan_already_prepared");
  const branchPlan = await provider.createFeatureBranch(workspace.projectId, workspace.baseBranch, workspace.branchName);
  return {
    ...workspace,
    status: "branch_plan_ready" as const,
    branchPlan,
    actualBranchCreated: false,
    updatedAt: new Date().toISOString()
  };
}

export async function prepareReviewPlan(workspace: DevelopmentWorkspace, provider: SourceControlProvider) {
  if (workspace.status !== "branch_plan_ready") throw new Error("branch_plan_required_before_review_plan");
  const reviewPlan = await provider.openChangeRequest(workspace.projectId, workspace.branchName);
  return {
    ...workspace,
    status: "review_plan_ready" as const,
    reviewPlan,
    actualBranchCreated: false,
    updatedAt: new Date().toISOString()
  };
}

export function cancelDevelopmentWorkspace(workspace: DevelopmentWorkspace) {
  if (workspace.status === "cancelled") return workspace;
  return {
    ...workspace,
    status: "cancelled" as const,
    actualBranchCreated: false,
    updatedAt: new Date().toISOString()
  };
}
