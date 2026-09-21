import { randomUUID } from "node:crypto";
import type { AiProvider, FrontendProvider, ProviderActionPlan, DeploymentResult } from "@orrbit/provider-adapters";

export type ValidationResult = "passed" | "failed" | "not_run";

export type DevelopmentChangeRequest = {
  id: string;
  projectId: string;
  prompt: string;
  summary: string;
  impactAreas: string[];
  risk: "medium" | "high";
  status: "planned" | "validation_recorded" | "preview_ready" | "approved" | "cancelled";
  aiPlan: ProviderActionPlan;
  validation: {
    typecheck: ValidationResult;
    tests: ValidationResult;
    build: ValidationResult;
    health: ValidationResult;
    evidenceReference: string | null;
    passed: boolean;
  } | null;
  preview: DeploymentResult | null;
  approval: {
    approved: boolean;
    approvedAt: string | null;
  };
  protections: {
    developmentOnly: true;
    productionExecutionLocked: true;
    providerExecutionGated: true;
  };
  createdAt: string;
  updatedAt: string;
};

function inferImpactAreas(prompt: string) {
  const text = prompt.toLowerCase();
  const areas = new Set<string>();
  if (/ui|screen|page|layout|css|design|frontend/.test(text)) areas.add("frontend");
  if (/api|backend|server|endpoint/.test(text)) areas.add("backend");
  if (/database|table|schema|migration|sql/.test(text)) areas.add("database");
  if (/auth|login|permission|role|security/.test(text)) areas.add("security");
  if (/payment|billing|invoice|razorpay/.test(text)) areas.add("payments");
  if (/test|qa|validation/.test(text)) areas.add("quality");
  if (!areas.size) areas.add("application");
  return [...areas];
}

function validationResult(value: string | undefined, field: string): ValidationResult {
  if (!["passed","failed","not_run"].includes(value ?? "")) throw new Error(`invalid_${field}`);
  return value as ValidationResult;
}

export async function createDevelopmentChange(input: {
  projectId: string; prompt?: string;
}, aiProvider: AiProvider): Promise<DevelopmentChangeRequest> {
  const prompt = input.prompt?.trim() ?? "";
  if (prompt.length < 10 || prompt.length > 3000) throw new Error("change_prompt_10_to_3000_characters");
  const impactAreas = inferImpactAreas(prompt);
  const risk = impactAreas.some((x) => ["database","security","payments"].includes(x)) ? "high" : "medium";
  const aiPlan = await aiProvider.planCodeChange(input.projectId, prompt);
  const now = new Date().toISOString();
  return {
    id: randomUUID(), projectId: input.projectId, prompt,
    summary: prompt.slice(0, 180), impactAreas, risk,
    status: "planned", aiPlan, validation: null, preview: null,
    approval: { approved: false, approvedAt: null },
    protections: { developmentOnly: true, productionExecutionLocked: true, providerExecutionGated: true },
    createdAt: now, updatedAt: now
  };
}

export function recordDevelopmentValidation(change: DevelopmentChangeRequest, input: {
  typecheck?: string; tests?: string; build?: string; health?: string; evidenceReference?: string;
}) {
  if (!["planned","validation_recorded"].includes(change.status)) throw new Error("change_not_validation_ready");
  const typecheck = validationResult(input.typecheck, "typecheck");
  const tests = validationResult(input.tests, "tests");
  const build = validationResult(input.build, "build");
  const health = validationResult(input.health, "health");
  const evidenceReference = input.evidenceReference?.trim() || null;
  const passed = [typecheck, tests, build, health].every((x) => x === "passed") && Boolean(evidenceReference);
  return {
    ...change,
    status: "validation_recorded" as const,
    validation: { typecheck, tests, build, health, evidenceReference, passed },
    updatedAt: new Date().toISOString()
  };
}

export async function prepareDevelopmentPreview(change: DevelopmentChangeRequest, provider: FrontendProvider) {
  if (!change.validation?.passed) throw new Error("passing_validation_required_before_preview");
  const preview = await provider.createPreview(change.projectId, `change-${change.id}`);
  return { ...change, status: "preview_ready" as const, preview, updatedAt: new Date().toISOString() };
}

export function approveDevelopmentChange(change: DevelopmentChangeRequest) {
  if (change.status !== "preview_ready" || !change.validation?.passed || !change.preview) throw new Error("preview_and_validation_required_before_approval");
  const now = new Date().toISOString();
  return { ...change, status: "approved" as const, approval: { approved: true, approvedAt: now }, updatedAt: now };
}

export function cancelDevelopmentChange(change: DevelopmentChangeRequest) {
  return { ...change, status: "cancelled" as const, updatedAt: new Date().toISOString() };
}
