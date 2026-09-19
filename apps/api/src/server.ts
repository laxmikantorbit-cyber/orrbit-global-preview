import { randomUUID } from "node:crypto";
import Fastify from "fastify";
import { createLocalProvisioningPlan, type ProvisioningPlan } from "@orrbit/ai-orchestrator";
import { classifyRisk, requiresApproval } from "@orrbit/policy-engine";
import { projectCreateSchema } from "@orrbit/project-manifest";
import { MemoryProjectRegistry } from "@orrbit/project-registry";

const app = Fastify({ logger: true });
const registry = new MemoryProjectRegistry();
const plans = new Map<string, ProvisioningPlan>();

type OnboardingJob = {
  id: string;
  planId: string;
  projectId: string;
  state: "succeeded";
  risk: ProvisioningPlan["risk"];
  requestedBy: "owner";
  evidence: string[];
  createdAt: string;
};

const jobs = new Map<string, OnboardingJob>();

app.get("/api/health", async () => ({
  status: "healthy",
  service: "orrbit-ai-control-plane",
  version: "0.1.0"
}));

app.get("/api/projects", async () => ({ projects: await registry.list() }));

app.get<{ Params: { id: string } }>("/api/projects/:id", async (request, reply) => {
  const project = await registry.get(request.params.id);
  if (!project) return reply.code(404).send({ error: "project_not_found" });
  return project;
});

app.post("/api/projects", async (request, reply) => {
  const parsed = projectCreateSchema.safeParse(request.body);
  if (!parsed.success) return reply.code(400).send({ error: "invalid_project", issues: parsed.error.issues });
  return reply.code(201).send(await registry.create(parsed.data));
});

app.post<{ Body: { prompt: string } }>("/api/project-plans", async (request, reply) => {
  const prompt = request.body?.prompt?.trim();
  if (!prompt || prompt.length < 10) return reply.code(400).send({ error: "prompt_too_short" });
  const plan = createLocalProvisioningPlan(prompt);
  plans.set(plan.id, plan);
  return reply.code(201).send(plan);
});

app.get<{ Params: { id: string } }>("/api/project-plans/:id", async (request, reply) => {
  const plan = plans.get(request.params.id);
  if (!plan) return reply.code(404).send({ error: "plan_not_found" });
  return plan;
});

app.post<{
  Params: { id: string };
  Body: { name?: string; repository?: { fullName: string; defaultBranch?: string } };
}>("/api/project-plans/:id/approve", async (request, reply) => {
  const plan = plans.get(request.params.id);
  if (!plan) return reply.code(404).send({ error: "plan_not_found" });
  if (plan.targetEnvironment !== "development") {
    return reply.code(409).send({ error: "non_development_apply_locked", targetEnvironment: plan.targetEnvironment });
  }
  if (plan.sourceMode === "existing-repository" && !request.body?.repository?.fullName) {
    return reply.code(400).send({ error: "repository_required_for_existing_source" });
  }

  const project = await registry.create({
    name: request.body?.name?.trim() || plan.suggestedName,
    type: plan.inferredProjectType,
    sourceMode: plan.sourceMode,
    repository: request.body?.repository,
    lifecycleStatus: "development",
    environments: ["development"],
    productionProtected: true
  });

  const now = new Date().toISOString();
  const job: OnboardingJob = {
    id: randomUUID(), planId: plan.id, projectId: project.id,
    state: "succeeded", risk: plan.risk, requestedBy: "owner",
    evidence: ["owner_approval", "development_only_policy", "project_registry_record"],
    createdAt: now
  };
  jobs.set(job.id, job);
  return reply.code(201).send({ approved: true, project, job });
});

app.get("/api/jobs", async () => ({ jobs: [...jobs.values()] }));

app.post<{ Body: { environment: "development" | "staging" | "production"; action: string; destructive?: boolean } }>(
  "/api/policy/evaluate",
  async (request) => ({ risk: classifyRisk(request.body), requiresApproval: requiresApproval(request.body) })
);

const port = Number(process.env.PORT ?? 8080);
await app.listen({ port, host: "0.0.0.0" });
