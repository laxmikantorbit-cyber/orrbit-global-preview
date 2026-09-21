import { randomUUID } from "node:crypto";

export type EvidenceResult = "passed" | "failed" | "not_run";

export type ReleaseEvidenceBundle = {
  id: string;
  projectId: string;
  environment: "development" | "staging" | "production";
  sourceRevision: string;
  buildResult: EvidenceResult;
  testResult: EvidenceResult;
  healthResult: EvidenceResult;
  deploymentIdentifier: string | null;
  healthReference: string;
  status: "draft" | "verified" | "rejected";
  complete: boolean;
  blockers: string[];
  protections: {
    evidenceOnly: true;
    deploymentExecutionDisabled: true;
    productionReleaseLocked: true;
  };
  createdAt: string;
  updatedAt: string;
};

function safeText(value: string | undefined, field: string, max = 300) {
  const text = value?.trim() ?? "";
  if (!text || text.length > max) throw new Error(`invalid_${field}`);
  return text;
}

function evidenceResult(value: string | undefined, field: string): EvidenceResult {
  if (!["passed", "failed", "not_run"].includes(value ?? "")) throw new Error(`invalid_${field}`);
  return value as EvidenceResult;
}

export function createReleaseEvidence(input: {
  projectId: string;
  environment?: string;
  sourceRevision?: string;
  buildResult?: string;
  testResult?: string;
  healthResult?: string;
  deploymentIdentifier?: string | null;
  healthReference?: string;
}): ReleaseEvidenceBundle {
  if (!["development", "staging", "production"].includes(input.environment ?? "")) throw new Error("invalid_release_environment");
  const environment = input.environment as "development" | "staging" | "production";
  const sourceRevision = safeText(input.sourceRevision, "source_revision", 200);
  const buildResult = evidenceResult(input.buildResult, "build_result");
  const testResult = evidenceResult(input.testResult, "test_result");
  const healthResult = evidenceResult(input.healthResult, "health_result");
  const healthReference = safeText(input.healthReference, "health_reference", 500);
  const deploymentIdentifier = input.deploymentIdentifier?.trim() || null;
  const blockers: string[] = [];
  if (buildResult !== "passed") blockers.push("build_not_passed");
  if (testResult !== "passed") blockers.push("tests_not_passed");
  if (healthResult !== "passed") blockers.push("health_not_verified");
  if (environment === "production") blockers.push("production_release_locked");
  const complete = blockers.length === 0;
  const now = new Date().toISOString();
  return {
    id: randomUUID(),
    projectId: input.projectId,
    environment,
    sourceRevision,
    buildResult,
    testResult,
    healthResult,
    deploymentIdentifier,
    healthReference,
    status: "draft",
    complete,
    blockers,
    protections: { evidenceOnly: true, deploymentExecutionDisabled: true, productionReleaseLocked: true },
    createdAt: now,
    updatedAt: now
  };
}

export function verifyReleaseEvidence(bundle: ReleaseEvidenceBundle) {
  if (bundle.status !== "draft") throw new Error("release_evidence_not_verifiable");
  if (!bundle.complete) {
    return { ...bundle, status: "rejected" as const, updatedAt: new Date().toISOString() };
  }
  return { ...bundle, status: "verified" as const, updatedAt: new Date().toISOString() };
}
