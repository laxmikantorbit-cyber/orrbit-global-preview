import { randomUUID } from "node:crypto";
import Fastify from "fastify";
import { Pool } from "pg";
import { createLocalProvisioningPlan, type ProvisioningPlan } from "@orrbit/ai-orchestrator";
import { createMartialArtsErpPilotPlan, createProjectImportPlan, martialArtsPilotAcceptance, martialArtsPilotModules, validateImportSource, type CreateImportPlanInput, type ProjectImportPlan } from "@orrbit/project-importer";
import { classifyRisk, requiresApproval } from "@orrbit/policy-engine";
import { providerCapabilities, DryRunGitHubProvider, DryRunCloudflarePagesProvider, DryRunCloudRunProvider, DryRunDatabaseProvider, DryRunOpenAiProvider, DryRunSecretProvider } from "@orrbit/provider-adapters";
import { buildRuntimeProjectManifest, projectCreateSchema } from "@orrbit/project-manifest";
import {
  MemoryProjectRegistry,
  PostgresProjectRegistry,
  type ProjectEnvironmentName,
  type ProjectEnvironmentStatus,
  type ProjectEnvironmentUpdate
} from "@orrbit/project-registry";

const app = Fastify({ logger: true });
const databaseUrl = process.env.CONTROL_DATABASE_URL?.trim();
const pool = databaseUrl ? new Pool({ connectionString: databaseUrl }) : null;
const registry = pool ? new PostgresProjectRegistry(pool) : new MemoryProjectRegistry();
const plans = new Map<string, ProvisioningPlan>();
const importPlans = new Map<string, ProjectImportPlan>();

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

type AuditEvent = {
  id: string;
  projectId: string | null;
  actor: "owner";
  eventType: string;
  eventData: object;
  createdAt: string;
};

const memoryAuditEvents: AuditEvent[] = [];

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

async function saveImportPlan(plan: ProjectImportPlan) {
  if (!pool) {
    importPlans.set(plan.id, plan);
    return;
  }
  await pool.query(
    `INSERT INTO import_plans
      (id, source_type, source_ref, requested_project_name, project_type, target_environment,
       risk_level, status, route_inventory, manifest_draft, plan_data, created_at, updated_at)
     VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9::jsonb,$10::jsonb,$11::jsonb,$12,$12)
     ON CONFLICT (id) DO UPDATE SET
       status=EXCLUDED.status, route_inventory=EXCLUDED.route_inventory,
       manifest_draft=EXCLUDED.manifest_draft, plan_data=EXCLUDED.plan_data, updated_at=NOW()`,
    [plan.id, plan.sourceType, plan.sourceRef, plan.requestedProjectName, plan.projectType,
      plan.targetEnvironment, plan.risk, plan.status, JSON.stringify(plan.routeInventory),
      JSON.stringify(plan.manifestDraft), JSON.stringify(plan), plan.createdAt]
  );
}

async function getImportPlan(id: string): Promise<ProjectImportPlan | undefined> {
  if (!pool) return importPlans.get(id);
  const result = await pool.query<{ plan_data: ProjectImportPlan }>(
    "SELECT plan_data FROM import_plans WHERE id=$1", [id]
  );
  return result.rows[0]?.plan_data;
}

async function listImportPlans(): Promise<ProjectImportPlan[]> {
  if (!pool) return [...importPlans.values()].sort((a, b) => b.createdAt.localeCompare(a.createdAt));
  const result = await pool.query<{ plan_data: ProjectImportPlan }>(
    "SELECT plan_data FROM import_plans ORDER BY created_at DESC LIMIT 100"
  );
  return result.rows.map((row) => row.plan_data);
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

async function listJobs(projectId?: string): Promise<OnboardingJob[]> {
  if (!pool) {
    const values = [...jobs.values()];
    return projectId ? values.filter((job) => job.projectId === projectId) : values;
  }
  const result = await pool.query(
    `SELECT id, plan_id, project_id, state, risk_level, requested_by, evidence, created_at
     FROM jobs
     WHERE ($1::uuid IS NULL OR project_id = $1)
     ORDER BY created_at DESC LIMIT 100`,
    [projectId ?? null]
  );
  return result.rows.map((row) => ({
    id: row.id, planId: row.plan_id, projectId: row.project_id,
    state: row.state, risk: row.risk_level, requestedBy: row.requested_by,
    evidence: row.evidence ?? [], createdAt: new Date(row.created_at).toISOString()
  })) as OnboardingJob[];
}

async function audit(projectId: string | null, eventType: string, eventData: object) {
  const event: AuditEvent = {
    id: randomUUID(),
    projectId,
    actor: "owner",
    eventType,
    eventData,
    createdAt: new Date().toISOString()
  };
  if (!pool) {
    memoryAuditEvents.unshift(event);
    return;
  }
  await pool.query(
    `INSERT INTO audit_events (id, project_id, actor, event_type, event_data, created_at)
     VALUES ($1,$2,'owner',$3,$4::jsonb,$5)`,
    [event.id, projectId, eventType, JSON.stringify(eventData), event.createdAt]
  );
}

async function listAuditEvents(projectId: string): Promise<AuditEvent[]> {
  if (!pool) return memoryAuditEvents.filter((event) => event.projectId === projectId).slice(0, 50);
  const result = await pool.query(
    `SELECT id, project_id, actor, event_type, event_data, created_at
     FROM audit_events
     WHERE project_id = $1
     ORDER BY created_at DESC
     LIMIT 50`,
    [projectId]
  );
  return result.rows.map((row) => ({
    id: row.id,
    projectId: row.project_id,
    actor: row.actor,
    eventType: row.event_type,
    eventData: row.event_data ?? {},
    createdAt: new Date(row.created_at).toISOString()
  })) as AuditEvent[];
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

app.get("/api/provider-capabilities", async () => ({
  mode: "dry-run",
  realCloudProvisioningEnabled: false,
  capabilities: providerCapabilities,
  adapters: [
    new DryRunGitHubProvider().name,
    new DryRunCloudflarePagesProvider().name,
    new DryRunCloudRunProvider().name,
    new DryRunDatabaseProvider().name,
    new DryRunOpenAiProvider().name,
    new DryRunSecretProvider().name
  ]
}));
app.get("/api/projects", async () => ({ projects: await registry.list() }));

app.get<{ Params: { id: string } }>("/api/projects/:id", async (request, reply) => {
  const project = await registry.get(request.params.id);
  if (!project) return reply.code(404).send({ error: "project_not_found" });
  return project;
});

app.get<{ Params: { id: string } }>("/api/projects/:id/manifest", async (request, reply) => {
  const project = await registry.get(request.params.id);
  if (!project) return reply.code(404).send({ error: "project_not_found" });
  const environments = await registry.listEnvironments(project.id);
  return buildRuntimeProjectManifest({
    project,
    environments,
    businessRules: project.name.toLowerCase().includes("repair")
      ? ["Repair pricing changes require explicit owner instruction"]
      : []
  });
});
app.get<{ Params: { id: string } }>("/api/projects/:id/command-centre", async (request, reply) => {
  const project = await registry.get(request.params.id);
  if (!project) return reply.code(404).send({ error: "project_not_found" });
  const [environments, projectJobs, auditEvents] = await Promise.all([
    registry.listEnvironments(project.id),
    listJobs(project.id),
    listAuditEvents(project.id)
  ]);
  return {
    project,
    environments,
    jobs: projectJobs.slice(0, 10),
    audit: auditEvents.slice(0, 20),
    protection: {
      productionProtected: project.productionProtected,
      nonDevelopmentConfigLocked: true,
      realCloudProvisioningEnabled: false
    }
  };
});

app.post("/api/projects", async (request, reply) => {
  const parsed = projectCreateSchema.safeParse(request.body);
  if (!parsed.success) return reply.code(400).send({ error: "invalid_project", issues: parsed.error.issues });
  const project = await registry.create(parsed.data);
  await audit(project.id, "project_created_manual", { type: project.type, sourceMode: project.sourceMode });
  return reply.code(201).send(project);
});

app.patch<{
  Params: { id: string; environment: ProjectEnvironmentName };
  Body: ProjectEnvironmentUpdate;
}>("/api/projects/:id/environments/:environment", async (request, reply) => {
  const project = await registry.get(request.params.id);
  if (!project) return reply.code(404).send({ error: "project_not_found" });
  if (request.params.environment !== "development") {
    return reply.code(409).send({ error: "non_development_environment_config_locked" });
  }
  const allowedStatuses = new Set<ProjectEnvironmentStatus>(["unconfigured", "planned", "ready", "degraded"]);
  if (request.body?.status && !allowedStatuses.has(request.body.status)) {
    return reply.code(400).send({ error: "invalid_environment_status" });
  }
  for (const field of ["frontendProvider", "backendProvider", "databaseProvider", "region"] as const) {
    const value = request.body?.[field];
    if (value !== undefined && value !== null && (typeof value !== "string" || value.length > 100)) {
      return reply.code(400).send({ error: "invalid_environment_field", field });
    }
  }
  const updated = await registry.updateEnvironment(project.id, request.params.environment, request.body ?? {});
  if (!updated) return reply.code(404).send({ error: "environment_not_found" });
  await audit(project.id, "development_environment_config_updated", { environment: updated });
  return updated;
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

app.get("/api/pilots/martial-arts-erp", async () => ({
  projectName: "Martial Arts ERP",
  mode: "development-import-pilot",
  sourceType: "chatgpt-sites",
  sourceReferenceRequiredBeforeCapture: true,
  productionProtected: true,
  realCloudProvisioningEnabled: false,
  modules: martialArtsPilotModules,
  acceptance: martialArtsPilotAcceptance
}));

app.post<{ Body: { sourceRef?: string } }>("/api/pilots/martial-arts-erp/import-plan", async (request, reply) => {
  const plan = createMartialArtsErpPilotPlan({ sourceRef: request.body?.sourceRef });
  await saveImportPlan(plan);
  await audit(null, "martial_arts_pilot_import_plan_created", {
    importPlanId: plan.id,
    sourceReferenceStatus: plan.pilot.sourceReferenceStatus,
    routeCount: plan.routeInventory.length,
    moduleCount: plan.pilot.modules.length
  });
  return reply.code(201).send(plan);
});

app.get("/api/import-plans", async () => ({ importPlans: await listImportPlans() }));

app.post<{ Body: CreateImportPlanInput }>("/api/import-plans", async (request, reply) => {
  const error = validateImportSource(request.body);
  if (error) return reply.code(error === "only_development_import_enabled_in_v1" ? 409 : 400).send({ error });
  const plan = createProjectImportPlan({
    sourceType: request.body.sourceType,
    sourceRef: request.body.sourceRef,
    projectName: request.body.projectName,
    projectType: request.body.projectType,
    targetEnvironment: request.body.targetEnvironment ?? "development",
    knownRoutes: request.body.knownRoutes
  });
  await saveImportPlan(plan);
  await audit(null, "import_plan_created", {
    importPlanId: plan.id, sourceType: plan.sourceType, risk: plan.risk, routeCount: plan.routeInventory.length
  });
  return reply.code(201).send(plan);
});

app.get<{ Params: { id: string } }>("/api/import-plans/:id", async (request, reply) => {
  const plan = await getImportPlan(request.params.id);
  if (!plan) return reply.code(404).send({ error: "import_plan_not_found" });
  return plan;
});

app.post<{ Params: { id: string } }>("/api/import-plans/:id/approve", async (request, reply) => {
  const plan = await getImportPlan(request.params.id);
  if (!plan) return reply.code(404).send({ error: "import_plan_not_found" });
  if (plan.targetEnvironment !== "development") {
    return reply.code(409).send({ error: "non_development_import_locked", targetEnvironment: plan.targetEnvironment });
  }
  const project = await registry.create({
    name: plan.manifestDraft.projectName,
    type: plan.manifestDraft.projectType,
    sourceMode: "import",
    lifecycleStatus: "development",
    environments: ["development"],
    productionProtected: true
  });
  const job: OnboardingJob = {
    id: randomUUID(), planId: plan.id, projectId: project.id, state: "succeeded", risk: plan.risk, requestedBy: "owner",
    evidence: ["owner_approval", "import_plan", "development_only_policy", "project_registry_record"],
    createdAt: new Date().toISOString()
  };
  const approvedPlan = { ...plan, status: "approved" as const, updatedAt: new Date().toISOString() };
  await saveImportPlan(approvedPlan);
  await saveJob(job, `Import plan approved for ${plan.requestedProjectName}`);
  await audit(project.id, "import_plan_approved", { importPlanId: plan.id, jobId: job.id, evidence: job.evidence });
  return reply.code(201).send({ approved: true, project, job, importPlan: approvedPlan });
});

app.get("/api/jobs", async () => ({ jobs: await listJobs() }));

app.post<{ Body: { environment: "development" | "staging" | "production"; action: string; destructive?: boolean } }>(
  "/api/policy/evaluate",
  async (request) => ({ risk: classifyRisk(request.body), requiresApproval: requiresApproval(request.body) })
);

const port = Number(process.env.PORT ?? 8080);
await app.listen({ port, host: "0.0.0.0" });
