import { randomUUID } from "node:crypto";
import Fastify from "fastify";
import { Pool } from "pg";
import { createLocalProvisioningPlan, type ProvisioningPlan } from "@orrbit/ai-orchestrator";
import { classifyRisk, requiresApproval } from "@orrbit/policy-engine";
import { projectCreateSchema } from "@orrbit/project-manifest";
import { MemoryProjectRegistry, PostgresProjectRegistry } from "@orrbit/project-registry";

const app = Fastify({ logger: true });
const databaseUrl = process.env.CONTROL_DATABASE_URL?.trim();
const pool = databaseUrl ? new Pool({ connectionString: databaseUrl }) : null;
const registry = pool ? new PostgresProjectRegistry(pool) : new MemoryProjectRegistry();
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

async function savePlan(plan: ProvisioningPlan) {
  if (!pool) return plans.set(plan.id, plan);
  await pool.query(
    `INSERT INTO provisioning_plans (id, prompt, plan_data)
     VALUES ($1,$2,$3::jsonb)
     ON CONFLICT (id) DO UPDATE SET prompt=EXCLUDED.prompt, plan_data=EXCLUDED.plan_data`,
    [plan.id, plan.prompt, JSON.stringify(plan)]
  );
}

async function getPlan(id: string): Promise<ProvisioningPlan | undefined> {
  if (!pool) return plans.get(id);
  const result = await pool.query<{ plan_data: ProvisioningPlan }>(
    "SELECT plan_data FROM provisioning_plans WHERE id=$1", [id]
  );
  return result.rows[0]?.plan_data;
}

async function markPlanApproved(id: string) {
  if (!pool) return;
  await pool.query(
    "UPDATE provisioning_plans SET status='approved', approved_at=NOW() WHERE id=$1", [id]
  );
}

async function saveJob(job: OnboardingJob, requestSummary: string) {
  if (!pool) {
    jobs.set(job.id, job);
    return;
  }
  await pool.query(
    `INSERT INTO jobs
      (id, project_id, job_type, state, risk_level, requested_by, request_summary,
       current_stage, plan_id, evidence)
     VALUES ($1,$2,'project_onboarding',$3,$4,$5,$6,'registered',$7,$8::jsonb)`,
    [job.id, job.projectId, job.state, job.risk, job.requestedBy,
      requestSummary, job.planId, JSON.stringify(job.evidence)]
  );
}

async function listJobs(): Promise<OnboardingJob[]> {
  if (!pool) return [...jobs.values()];
  const result = await pool.query(
    `SELECT id, plan_id, project_id, state, risk_level, requested_by, evidence, created_at
     FROM jobs ORDER BY created_at DESC LIMIT 100`
  );
  return result.rows.map((row) => ({
    id: row.id, planId: row.plan_id, projectId: row.project_id,
    state: row.state, risk: row.risk_level, requestedBy: row.requested_by,
    evidence: row.evidence ?? [], createdAt: new Date(row.created_at).toISOString()
  })) as OnboardingJob[];
}

async function audit(projectId: string | null, eventType: string, eventData: object) {
  if (!pool) return;
  await pool.query(
    `INSERT INTO audit_events (id, project_id, actor, event_type, event_data)
     VALUES ($1,$2,'owner',$3,$4::jsonb)`,
    [randomUUID(), projectId, eventType, JSON.stringify(eventData)]
  );
}

app.get("/api/health", async () => {
  let database = pool ? "postgres" : "memory";
  if (pool) {
    try { await pool.query("SELECT 1"); }
    catch { database = "postgres_unavailable"; }
  }
  return {
    status: database === "postgres_unavailable" ? "degraded" : "healthy",
    service: "orrbit-ai-control-plane",
    version: "0.1.0",
    persistence: database
  };
});

app.get("/api/projects", async () => ({ projects: await registry.list() }));

app.get<{ Params: { id: string } }>("/api/projects/:id", async (request, reply) => {
  const project = await registry.get(request.params.id);
  if (!project) return reply.code(404).send({ error: "project_not_found" });
  return project;
});

app.post("/api/projects", async (request, reply) => {
  const parsed = projectCreateSchema.safeParse(request.body);
  if (!parsed.success) return reply.code(400).send({ error: "invalid_project", issues: parsed.error.issues });
  const project = await registry.create(parsed.data);
  await audit(project.id, "project_created_manual", { type: project.type, sourceMode: project.sourceMode });
  return reply.code(201).send(project);
});

app.post<{ Body: { prompt: string } }>("/api/project-plans", async (request, reply) => {
  const prompt = request.body?.prompt?.trim();
  if (!prompt || prompt.length < 10) return reply.code(400).send({ error: "prompt_too_short" });
  const plan = createLocalProvisioningPlan(prompt);
  await savePlan(plan);
  await audit(null, "project_plan_created", { planId: plan.id, risk: plan.risk, target: plan.targetEnvironment });
  return reply.code(201).send(plan);
});

app.get<{ Params: { id: string } }>("/api/project-plans/:id", async (request, reply) => {
  const plan = await getPlan(request.params.id);
  if (!plan) return reply.code(404).send({ error: "plan_not_found" });
  return plan;
});

app.post<{
  Params: { id: string };
  Body: { name?: string; repository?: { fullName: string; defaultBranch?: string } };
}>("/api/project-plans/:id/approve", async (request, reply) => {
  const plan = await getPlan(request.params.id);
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

  const job: OnboardingJob = {
    id: randomUUID(), planId: plan.id, projectId: project.id,
    state: "succeeded", risk: plan.risk, requestedBy: "owner",
    evidence: ["owner_approval", "development_only_policy", "project_registry_record"],
    createdAt: new Date().toISOString()
  };
  await markPlanApproved(plan.id);
  await saveJob(job, plan.prompt);
  await audit(project.id, "project_plan_approved", { planId: plan.id, jobId: job.id, evidence: job.evidence });
  return reply.code(201).send({ approved: true, project, job });
});

app.get("/api/jobs", async () => ({ jobs: await listJobs() }));

app.post<{ Body: { environment: "development" | "staging" | "production"; action: string; destructive?: boolean } }>(
  "/api/policy/evaluate",
  async (request) => ({ risk: classifyRisk(request.body), requiresApproval: requiresApproval(request.body) })
);

const port = Number(process.env.PORT ?? 8080);
await app.listen({ port, host: "0.0.0.0" });
