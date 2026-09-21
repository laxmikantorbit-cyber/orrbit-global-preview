import { randomUUID } from "node:crypto";

export type VersionLedgerEntry = {
  id: string;
  projectId: string;
  environment: "development" | "staging" | "production";
  sourceRevision: string;
  releaseEvidenceId: string;
  deploymentIdentifier: string | null;
  healthVerified: boolean;
  createdAt: string;
};

export type RollbackPlan = {
  id: string;
  projectId: string;
  environment: "development" | "staging" | "production";
  fromVersionId: string;
  toVersionId: string;
  status: "planned" | "approved" | "cancelled";
  risk: "high";
  requiresApproval: true;
  executionAllowed: false;
  restorePointRequired: true;
  protections: { rollbackExecutionLocked: true; productionProtected: true };
  createdAt: string;
  updatedAt: string;
};

export function createVersionLedgerEntry(input: {
  projectId: string;
  environment: string;
  sourceRevision: string;
  releaseEvidenceId: string;
  deploymentIdentifier?: string | null;
  healthVerified: boolean;
}): VersionLedgerEntry {
  if (!["development","staging","production"].includes(input.environment)) throw new Error("invalid_version_environment");
  if (!input.sourceRevision.trim() || !input.releaseEvidenceId.trim()) throw new Error("version_evidence_required");
  return {
    id: randomUUID(), projectId: input.projectId,
    environment: input.environment as VersionLedgerEntry["environment"],
    sourceRevision: input.sourceRevision.trim(),
    releaseEvidenceId: input.releaseEvidenceId.trim(),
    deploymentIdentifier: input.deploymentIdentifier?.trim() || null,
    healthVerified: input.healthVerified,
    createdAt: new Date().toISOString()
  };
}

export function createRollbackPlan(input: {
  projectId: string; environment: string; fromVersionId: string; toVersionId: string;
}): RollbackPlan {
  if (!["development","staging","production"].includes(input.environment)) throw new Error("invalid_rollback_environment");
  if (!input.fromVersionId || !input.toVersionId || input.fromVersionId === input.toVersionId) throw new Error("invalid_rollback_versions");
  const now = new Date().toISOString();
  return {
    id: randomUUID(), projectId: input.projectId,
    environment: input.environment as RollbackPlan["environment"],
    fromVersionId: input.fromVersionId, toVersionId: input.toVersionId,
    status: "planned", risk: "high", requiresApproval: true, executionAllowed: false,
    restorePointRequired: true,
    protections: { rollbackExecutionLocked: true, productionProtected: true },
    createdAt: now, updatedAt: now
  };
}

export function approveRollbackPlan(plan: RollbackPlan) {
  if (plan.status !== "planned") throw new Error("rollback_plan_not_approvable");
  return { ...plan, status: "approved" as const, executionAllowed: false as const, updatedAt: new Date().toISOString() };
}

export function cancelRollbackPlan(plan: RollbackPlan) {
  return { ...plan, status: "cancelled" as const, executionAllowed: false as const, updatedAt: new Date().toISOString() };
}
