import { createHash, randomUUID } from "node:crypto";
import { mkdir, rm, writeFile } from "node:fs/promises";
import { basename, dirname, isAbsolute, relative, resolve } from "node:path";
import multipart from "@fastify/multipart";
import AdmZip from "adm-zip";
import Fastify from "fastify";
import { Pool } from "pg";
import { createLocalProvisioningPlan, type ProvisioningPlan } from "@orrbit/ai-orchestrator";
import { confirmImportWorkspaceSourceReference, createDevelopmentImportExecution, createImportWorkspaceFromPlan, createMartialArtsErpPilotPlan, createProjectImportPlan, createSourceAcquisitionRecord, discardSourceAcquisition, evaluateImportWorkspaceDeployGate, martialArtsPilotAcceptance, martialArtsPilotModules, resetDevelopmentImportExecution, runDevelopmentImportExecution, updateImportWorkspaceCaptureItem, validateImportSource, type CreateImportPlanInput, type ImportExecutionJob, type ProjectImportPlan, type ProjectImportWorkspace, type SourceAcquisitionRecord, type WorkspaceCaptureKind, type WorkspaceCaptureStatus } from "@orrbit/project-importer";
import { classifyRisk, requiresApproval } from "@orrbit/policy-engine";
import { providerCapabilities, DryRunGitHubProvider, DryRunCloudflarePagesProvider, DryRunCloudRunProvider, DryRunDatabaseProvider, DryRunOpenAiProvider, DryRunSecretProvider } from "@orrbit/provider-adapters";
import { buildRuntimeProjectManifest, projectCreateSchema } from "@orrbit/project-manifest";
import { createSourceBuildJob, isDockerSandboxReady, resetSourceBuildJob, runSourceBuild, type SourceBuildJob } from "./source-build-runner.js";
import { getPanelReadiness, isSyntheticFixtureHeader } from "./panel-readiness.js";
import { clearOwnerSessionCookie, createOwnerSessionCookie, OWNER_SESSION_COOKIE, OwnerAuthStore, parseCookie } from "./owner-auth.js";
import { cancelDevelopmentWorkspace, createDevelopmentWorkspace, prepareBranchPlan, prepareReviewPlan, type DevelopmentWorkspace } from "./development-workspace.js";
import { createSecretReference, rejectSecretValueFields, type SecretReferenceRecord } from "./secret-reference.js";
import { approveDnsProposal, cancelDnsProposal, createDnsChangeProposal, type DnsChangeProposal } from "./dns-proposal.js";
import { createReleaseEvidence, verifyReleaseEvidence, type ReleaseEvidenceBundle } from "./release-evidence.js";
import { approveRollbackPlan, cancelRollbackPlan, createRollbackPlan, createVersionLedgerEntry, type RollbackPlan, type VersionLedgerEntry } from "./rollback-history.js";
import { createCostLedgerEntry, createProjectBudget, summarizeCostBudget, type CostLedgerEntry, type ProjectBudget } from "./cost-budget.js";
import {
  MemoryProjectRegistry,
  PostgresProjectRegistry,
  type ProjectEnvironmentName,
  type ProjectEnvironmentStatus,
  type ProjectEnvironmentUpdate
} from "@orrbit/project-registry";

const app = Fastify({ logger: true });
await app.register(multipart, { limits: { files: 1, fileSize: 100 * 1024 * 1024 } });
const importInboxRoot = resolve(process.env.CONTROL_RUNTIME_DIR?.trim() || resolve(process.cwd(), "runtime"), "import-inbox");
const databaseUrl = process.env.CONTROL_DATABASE_URL?.trim();
const pool = databaseUrl ? new Pool({ connectionString: databaseUrl }) : null;
const ownerAuth = new OwnerAuthStore(pool);
const registry = pool ? new PostgresProjectRegistry(pool) : new MemoryProjectRegistry();
const plans = new Map<string, ProvisioningPlan>();
const importPlans = new Map<string, ProjectImportPlan>();
const importWorkspaces = new Map<string, ProjectImportWorkspace>();
const importExecutions = new Map<string, ImportExecutionJob>();
const sourceAcquisitions = new Map<string, SourceAcquisitionRecord>();

const sourceBuildJobs = new Map<string, SourceBuildJob>();
const developmentWorkspaces = new Map<string, DevelopmentWorkspace>();
const secretReferences = new Map<string, SecretReferenceRecord>();
const dnsChangeProposals = new Map<string, DnsChangeProposal>();
const releaseEvidenceBundles = new Map<string, ReleaseEvidenceBundle>();
const versionLedger = new Map<string, VersionLedgerEntry>();
const rollbackPlans = new Map<string, RollbackPlan>();
const projectBudgets = new Map<string, ProjectBudget>();
const costLedger = new Map<string, CostLedgerEntry>();

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
const ownerLoginAttempts = new Map<string, { count: number; windowStartedAt: number }>();
const LOGIN_WINDOW_MS = 15 * 60 * 1000;
const LOGIN_MAX_FAILURES = 5;

function ownerLoginRateLimited(key: string) {
  const current = ownerLoginAttempts.get(key);
  if (!current) return false;
  if (Date.now() - current.windowStartedAt > LOGIN_WINDOW_MS) {
    ownerLoginAttempts.delete(key);
    return false;
  }
  return current.count >= LOGIN_MAX_FAILURES;
}

function recordOwnerLoginFailure(key: string) {
  const current = ownerLoginAttempts.get(key);
  if (!current || Date.now() - current.windowStartedAt > LOGIN_WINDOW_MS) {
    ownerLoginAttempts.set(key, { count: 1, windowStartedAt: Date.now() });
    return;
  }
  current.count += 1;
}

function secureOwnerCookie(request: { protocol: string }) {
  return process.env.NODE_ENV === "production" || request.protocol === "https";
}

function ownerSetupProtection(ip: string) {
  const loopback = ip === "127.0.0.1" || ip === "::1" || ip === "::ffff:127.0.0.1";
  if (loopback) return "local" as const;
  return process.env.CONTROL_OWNER_SETUP_TOKEN?.trim() ? "token_required" as const : "blocked_remote" as const;
}

function ownerSetupTokenMatches(value: string | string[] | undefined) {
  const expected = process.env.CONTROL_OWNER_SETUP_TOKEN?.trim();
  const received = Array.isArray(value) ? value[0] : value;
  if (!expected || !received) return false;
  const expectedHash = createHash("sha256").update(expected).digest("hex");
  const receivedHash = createHash("sha256").update(received).digest("hex");
  return expectedHash === receivedHash;
}

function realImportAccessAllowed(request: { headers: { [key: string]: string | string[] | undefined } }) {
  const readiness = getPanelReadiness();
  return readiness.realImportUnlocked || isSyntheticFixtureHeader(request.headers["x-orrbit-test-fixture"]);
}

function realImportLockedPayload() {
  const readiness = getPanelReadiness();
  return {
    error: "panel_completion_required_before_real_import",
    panelComplete: readiness.panelComplete,
    realImportUnlocked: readiness.realImportUnlocked,
    standingRule: readiness.standingRule,
    completion: readiness.completion
  };
}

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


async function saveImportWorkspace(workspace: ProjectImportWorkspace) {
  if (!pool) {
    importWorkspaces.set(workspace.id, workspace);
    return;
  }
  await pool.query(
    `INSERT INTO import_workspaces
      (id, import_plan_id, project_id, status, source_reference_status, route_capture,
       module_capture, capture_checklist, deploy_gate, workspace_data, created_at, updated_at)
     VALUES ($1,$2,$3,$4,$5,$6::jsonb,$7::jsonb,$8::jsonb,$9::jsonb,$10::jsonb,$11,$11)
     ON CONFLICT (id) DO UPDATE SET status=EXCLUDED.status,
       source_reference_status=EXCLUDED.source_reference_status, route_capture=EXCLUDED.route_capture,
       module_capture=EXCLUDED.module_capture, capture_checklist=EXCLUDED.capture_checklist,
       deploy_gate=EXCLUDED.deploy_gate, workspace_data=EXCLUDED.workspace_data, updated_at=NOW()`,
    [workspace.id, workspace.importPlanId, workspace.projectId ?? null, workspace.status,
      workspace.sourceReferenceStatus, JSON.stringify(workspace.routeCapture),
      JSON.stringify(workspace.moduleCapture), JSON.stringify(workspace.captureChecklist),
      JSON.stringify(workspace.deployGate), JSON.stringify(workspace), workspace.createdAt]
  );
}
async function getImportWorkspace(id: string): Promise<ProjectImportWorkspace | undefined> {
  if (!pool) return importWorkspaces.get(id);
  const result = await pool.query<{ workspace_data: ProjectImportWorkspace }>(
    "SELECT workspace_data FROM import_workspaces WHERE id=$1", [id]
  );
  return result.rows[0]?.workspace_data;
}

async function listImportWorkspaces(): Promise<ProjectImportWorkspace[]> {
  if (!pool) return [...importWorkspaces.values()].sort((a, b) => b.createdAt.localeCompare(a.createdAt));
  const result = await pool.query<{ workspace_data: ProjectImportWorkspace }>(
    "SELECT workspace_data FROM import_workspaces ORDER BY created_at DESC LIMIT 100"
  );
  return result.rows.map((row) => row.workspace_data);
}

async function saveImportExecution(job: ImportExecutionJob) {
  if (!pool) {
    importExecutions.set(job.id, job);
    return;
  }
  await pool.query(
    `INSERT INTO import_execution_jobs
      (id, workspace_id, project_id, status, execution_data, created_at, updated_at)
     VALUES ($1,$2,$3,$4,$5::jsonb,$6,$6)
     ON CONFLICT (id) DO UPDATE SET status=EXCLUDED.status,
       execution_data=EXCLUDED.execution_data, updated_at=NOW()`,
    [job.id, job.workspaceId, job.projectId, job.status, JSON.stringify(job), job.createdAt]
  );
}

async function getImportExecution(id: string): Promise<ImportExecutionJob | undefined> {
  if (!pool) return importExecutions.get(id);
  const result = await pool.query<{ execution_data: ImportExecutionJob }>(
    "SELECT execution_data FROM import_execution_jobs WHERE id=$1", [id]
  );
  return result.rows[0]?.execution_data;
}

async function listImportExecutions(workspaceId?: string): Promise<ImportExecutionJob[]> {
  if (!pool) {
    const values = [...importExecutions.values()];
    return values.filter((job) => !workspaceId || job.workspaceId === workspaceId)
      .sort((a, b) => b.createdAt.localeCompare(a.createdAt));
  }
  const result = await pool.query<{ execution_data: ImportExecutionJob }>(
    `SELECT execution_data FROM import_execution_jobs
     WHERE ($1::uuid IS NULL OR workspace_id=$1)
     ORDER BY created_at DESC LIMIT 100`, [workspaceId ?? null]
  );
  return result.rows.map((row) => row.execution_data);
}

async function saveSourceAcquisition(record: SourceAcquisitionRecord) {
  if (!pool) {
    sourceAcquisitions.set(record.id, record);
    return;
  }
  await pool.query(
    `INSERT INTO source_acquisitions
      (id, workspace_id, project_id, status, acquisition_data, created_at, updated_at)
     VALUES ($1,$2,$3,$4,$5::jsonb,$6,$6)
     ON CONFLICT (id) DO UPDATE SET status=EXCLUDED.status,
       acquisition_data=EXCLUDED.acquisition_data, updated_at=NOW()`,
    [record.id, record.workspaceId, record.projectId, record.status, JSON.stringify(record), record.createdAt]
  );
}

async function getSourceAcquisition(id: string): Promise<SourceAcquisitionRecord | undefined> {
  if (!pool) return sourceAcquisitions.get(id);
  const result = await pool.query<{ acquisition_data: SourceAcquisitionRecord }>(
    "SELECT acquisition_data FROM source_acquisitions WHERE id=$1", [id]
  );
  return result.rows[0]?.acquisition_data;
}

async function listSourceAcquisitions(workspaceId: string): Promise<SourceAcquisitionRecord[]> {
  if (!pool) return [...sourceAcquisitions.values()]
    .filter((record) => record.workspaceId === workspaceId)
    .sort((a, b) => b.createdAt.localeCompare(a.createdAt));
  const result = await pool.query<{ acquisition_data: SourceAcquisitionRecord }>(
    "SELECT acquisition_data FROM source_acquisitions WHERE workspace_id=$1 ORDER BY created_at DESC LIMIT 50", [workspaceId]
  );
  return result.rows.map((row) => row.acquisition_data);
}

async function saveSourceBuildJob(job: SourceBuildJob) {
  if (!pool) {
    sourceBuildJobs.set(job.id, job);
    return;
  }
  await pool.query(
    `INSERT INTO source_build_jobs
      (id, acquisition_id, workspace_id, project_id, status, build_data, created_at, updated_at)
     VALUES ($1,$2,$3,$4,$5,$6::jsonb,$7,$7)
     ON CONFLICT (id) DO UPDATE SET status=EXCLUDED.status,
       build_data=EXCLUDED.build_data, updated_at=NOW()`,
    [job.id, job.acquisitionId, job.workspaceId, job.projectId, job.status, JSON.stringify(job), job.createdAt]
  );
}

async function getSourceBuildJob(id: string): Promise<SourceBuildJob | undefined> {
  if (!pool) return sourceBuildJobs.get(id);
  const result = await pool.query<{ build_data: SourceBuildJob }>(
    "SELECT build_data FROM source_build_jobs WHERE id=$1", [id]
  );
  return result.rows[0]?.build_data;
}

async function listSourceBuildJobs(acquisitionId: string): Promise<SourceBuildJob[]> {
  if (!pool) return [...sourceBuildJobs.values()]
    .filter((job) => job.acquisitionId === acquisitionId)
    .sort((a, b) => b.createdAt.localeCompare(a.createdAt));
  const result = await pool.query<{ build_data: SourceBuildJob }>(
    "SELECT build_data FROM source_build_jobs WHERE acquisition_id=$1 ORDER BY created_at DESC LIMIT 50", [acquisitionId]
  );
  return result.rows.map((row) => row.build_data);
}

async function saveDevelopmentWorkspace(workspace: DevelopmentWorkspace) {
  if (!pool) {
    developmentWorkspaces.set(workspace.id, workspace);
    return;
  }
  await pool.query(
    `INSERT INTO development_workspaces
      (id, project_id, status, workspace_data, created_at, updated_at)
     VALUES ($1,$2,$3,$4::jsonb,$5,$5)
     ON CONFLICT (id) DO UPDATE SET status=EXCLUDED.status,
       workspace_data=EXCLUDED.workspace_data, updated_at=NOW()`,
    [workspace.id, workspace.projectId, workspace.status, JSON.stringify(workspace), workspace.createdAt]
  );
}

async function getDevelopmentWorkspace(id: string): Promise<DevelopmentWorkspace | undefined> {
  if (!pool) return developmentWorkspaces.get(id);
  const result = await pool.query<{ workspace_data: DevelopmentWorkspace }>(
    "SELECT workspace_data FROM development_workspaces WHERE id=$1", [id]
  );
  return result.rows[0]?.workspace_data;
}

async function listDevelopmentWorkspaces(projectId: string): Promise<DevelopmentWorkspace[]> {
  if (!pool) return [...developmentWorkspaces.values()]
    .filter((workspace) => workspace.projectId === projectId)
    .sort((a, b) => b.createdAt.localeCompare(a.createdAt));
  const result = await pool.query<{ workspace_data: DevelopmentWorkspace }>(
    "SELECT workspace_data FROM development_workspaces WHERE project_id=$1 ORDER BY created_at DESC LIMIT 100",
    [projectId]
  );
  return result.rows.map((row) => row.workspace_data);
}

async function saveSecretReference(record: SecretReferenceRecord) {
  if (!pool) {
    const duplicate = [...secretReferences.values()].find((item) =>
      item.projectId === record.projectId &&
      item.environment === record.environment &&
      item.secretName === record.secretName &&
      item.id !== record.id);
    if (duplicate) throw new Error("secret_reference_already_exists");
    secretReferences.set(record.id, record);
    return;
  }
  await pool.query(
    `INSERT INTO secret_references
      (id, project_id, environment_name, secret_name, provider, provider_reference,
       reference_data, created_at, updated_at)
     VALUES ($1,$2,$3,$4,$5,$6,$7::jsonb,$8,$8)`,
    [record.id, record.projectId, record.environment, record.secretName, record.provider,
      record.providerReference, JSON.stringify(record), record.createdAt]
  );
}

async function listSecretReferences(projectId: string): Promise<SecretReferenceRecord[]> {
  if (!pool) return [...secretReferences.values()]
    .filter((record) => record.projectId === projectId)
    .sort((a, b) => b.createdAt.localeCompare(a.createdAt));
  const result = await pool.query<{ reference_data: SecretReferenceRecord }>(
    "SELECT reference_data FROM secret_references WHERE project_id=$1 ORDER BY created_at DESC LIMIT 100",
    [projectId]
  );
  return result.rows.map((row) => row.reference_data);
}

async function saveDnsProposal(proposal: DnsChangeProposal) {
  if (!pool) {
    dnsChangeProposals.set(proposal.id, proposal);
    return;
  }
  await pool.query(
    `INSERT INTO dns_change_proposals (id, project_id, status, proposal_data, created_at, updated_at)
     VALUES ($1,$2,$3,$4::jsonb,$5,$5)
     ON CONFLICT (id) DO UPDATE SET status=EXCLUDED.status,
       proposal_data=EXCLUDED.proposal_data, updated_at=NOW()`,
    [proposal.id, proposal.projectId, proposal.status, JSON.stringify(proposal), proposal.createdAt]
  );
}

async function getDnsProposal(id: string): Promise<DnsChangeProposal | undefined> {
  if (!pool) return dnsChangeProposals.get(id);
  const result = await pool.query<{ proposal_data: DnsChangeProposal }>(
    "SELECT proposal_data FROM dns_change_proposals WHERE id=$1", [id]
  );
  return result.rows[0]?.proposal_data;
}

async function listDnsProposals(projectId: string): Promise<DnsChangeProposal[]> {
  if (!pool) return [...dnsChangeProposals.values()]
    .filter((proposal) => proposal.projectId === projectId)
    .sort((a, b) => b.createdAt.localeCompare(a.createdAt));
  const result = await pool.query<{ proposal_data: DnsChangeProposal }>(
    "SELECT proposal_data FROM dns_change_proposals WHERE project_id=$1 ORDER BY created_at DESC LIMIT 100",
    [projectId]
  );
  return result.rows.map((row) => row.proposal_data);
}

async function saveReleaseEvidence(bundle: ReleaseEvidenceBundle) {
  if (!pool) {
    releaseEvidenceBundles.set(bundle.id, bundle);
    return;
  }
  await pool.query(
    `INSERT INTO release_evidence_bundles
      (id, project_id, environment_name, status, evidence_data, created_at, updated_at)
     VALUES ($1,$2,$3,$4,$5::jsonb,$6,$6)
     ON CONFLICT (id) DO UPDATE SET status=EXCLUDED.status,
       evidence_data=EXCLUDED.evidence_data, updated_at=NOW()`,
    [bundle.id, bundle.projectId, bundle.environment, bundle.status, JSON.stringify(bundle), bundle.createdAt]
  );
}

async function getReleaseEvidence(id: string): Promise<ReleaseEvidenceBundle | undefined> {
  if (!pool) return releaseEvidenceBundles.get(id);
  const result = await pool.query<{ evidence_data: ReleaseEvidenceBundle }>(
    "SELECT evidence_data FROM release_evidence_bundles WHERE id=$1", [id]
  );
  return result.rows[0]?.evidence_data;
}

async function listReleaseEvidence(projectId: string): Promise<ReleaseEvidenceBundle[]> {
  if (!pool) return [...releaseEvidenceBundles.values()]
    .filter((bundle) => bundle.projectId === projectId)
    .sort((a, b) => b.createdAt.localeCompare(a.createdAt));
  const result = await pool.query<{ evidence_data: ReleaseEvidenceBundle }>(
    "SELECT evidence_data FROM release_evidence_bundles WHERE project_id=$1 ORDER BY created_at DESC LIMIT 100",
    [projectId]
  );
  return result.rows.map((row) => row.evidence_data);
}

async function saveVersionLedgerEntry(entry: VersionLedgerEntry) {
  if (!pool) {
    versionLedger.set(entry.id, entry);
    return;
  }
  await pool.query(
    `INSERT INTO version_ledger (id, project_id, environment_name, version_data, created_at)
     VALUES ($1,$2,$3,$4::jsonb,$5)`,
    [entry.id, entry.projectId, entry.environment, JSON.stringify(entry), entry.createdAt]
  );
}

async function listVersionLedger(projectId: string): Promise<VersionLedgerEntry[]> {
  if (!pool) return [...versionLedger.values()]
    .filter((entry) => entry.projectId === projectId)
    .sort((a, b) => b.createdAt.localeCompare(a.createdAt));
  const result = await pool.query<{ version_data: VersionLedgerEntry }>(
    "SELECT version_data FROM version_ledger WHERE project_id=$1 ORDER BY created_at DESC LIMIT 100", [projectId]
  );
  return result.rows.map((row) => row.version_data);
}

async function saveRollbackPlan(plan: RollbackPlan) {
  if (!pool) {
    rollbackPlans.set(plan.id, plan);
    return;
  }
  await pool.query(
    `INSERT INTO rollback_plans (id, project_id, status, rollback_data, created_at, updated_at)
     VALUES ($1,$2,$3,$4::jsonb,$5,$5)
     ON CONFLICT (id) DO UPDATE SET status=EXCLUDED.status,
       rollback_data=EXCLUDED.rollback_data, updated_at=NOW()`,
    [plan.id, plan.projectId, plan.status, JSON.stringify(plan), plan.createdAt]
  );
}

async function getRollbackPlan(id: string): Promise<RollbackPlan | undefined> {
  if (!pool) return rollbackPlans.get(id);
  const result = await pool.query<{ rollback_data: RollbackPlan }>(
    "SELECT rollback_data FROM rollback_plans WHERE id=$1", [id]
  );
  return result.rows[0]?.rollback_data;
}

async function listRollbackPlans(projectId: string): Promise<RollbackPlan[]> {
  if (!pool) return [...rollbackPlans.values()]
    .filter((plan) => plan.projectId === projectId)
    .sort((a, b) => b.createdAt.localeCompare(a.createdAt));
  const result = await pool.query<{ rollback_data: RollbackPlan }>(
    "SELECT rollback_data FROM rollback_plans WHERE project_id=$1 ORDER BY created_at DESC LIMIT 100", [projectId]
  );
  return result.rows.map((row) => row.rollback_data);
}

async function saveProjectBudget(budget: ProjectBudget) {
  if (!pool) {
    projectBudgets.set(budget.projectId, budget);
    return;
  }
  await pool.query(
    `INSERT INTO project_budgets
      (id, project_id, currency, monthly_limit, warning_percent, budget_data, created_at, updated_at)
     VALUES ($1,$2,$3,$4,$5,$6::jsonb,$7,$7)
     ON CONFLICT (project_id) DO UPDATE SET currency=EXCLUDED.currency,
       monthly_limit=EXCLUDED.monthly_limit, warning_percent=EXCLUDED.warning_percent,
       budget_data=EXCLUDED.budget_data, updated_at=NOW()`,
    [budget.id, budget.projectId, budget.currency, budget.monthlyLimit, budget.warningPercent, JSON.stringify(budget), budget.createdAt]
  );
}

async function getProjectBudget(projectId: string): Promise<ProjectBudget | null> {
  if (!pool) return projectBudgets.get(projectId) ?? null;
  const result = await pool.query<{ budget_data: ProjectBudget }>(
    "SELECT budget_data FROM project_budgets WHERE project_id=$1", [projectId]
  );
  return result.rows[0]?.budget_data ?? null;
}

async function saveCostLedgerEntry(entry: CostLedgerEntry) {
  if (!pool) {
    costLedger.set(entry.id, entry);
    return;
  }
  await pool.query(
    `INSERT INTO cost_ledger
      (id, project_id, provider, category, amount, currency, occurred_at, entry_data, created_at)
     VALUES ($1,$2,$3,$4,$5,$6,$7,$8::jsonb,$9)`,
    [entry.id, entry.projectId, entry.provider, entry.category, entry.amount, entry.currency,
      entry.occurredAt, JSON.stringify(entry), entry.createdAt]
  );
}

async function listCostLedger(projectId: string): Promise<CostLedgerEntry[]> {
  if (!pool) return [...costLedger.values()]
    .filter((entry) => entry.projectId === projectId)
    .sort((a, b) => b.occurredAt.localeCompare(a.occurredAt));
  const result = await pool.query<{ entry_data: CostLedgerEntry }>(
    "SELECT entry_data FROM cost_ledger WHERE project_id=$1 ORDER BY occurred_at DESC LIMIT 500", [projectId]
  );
  return result.rows.map((row) => row.entry_data);
}

function acquisitionFolder(workspaceId: string, acquisitionId: string) {
  return resolve(importInboxRoot, workspaceId, acquisitionId);
}

async function extractAcquiredSource(record: SourceAcquisitionRecord, archiveBuffer: Buffer, zip: AdmZip) {
  const root = acquisitionFolder(record.workspaceId, record.id);
  const sourceRoot = resolve(root, "source");
  await mkdir(sourceRoot, { recursive: true });
  await writeFile(resolve(root, "source.zip"), archiveBuffer);
  for (const entry of zip.getEntries()) {
    if (entry.isDirectory) continue;
    const normalized = entry.entryName.replaceAll("\\", "/");
    const destination = resolve(sourceRoot, normalized);
    const rel = relative(sourceRoot, destination);
    if (!rel || rel.startsWith("..") || isAbsolute(rel)) throw new Error("archive_path_escape_blocked");
    await mkdir(dirname(destination), { recursive: true });
    await writeFile(destination, entry.getData());
  }
  return `control-plane://source-snapshot/${record.id}`;
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

const ownerAuthExemptPaths = new Set([
  "/api/health",
  "/api/auth/status",
  "/api/auth/setup",
  "/api/auth/login"
]);

app.addHook("preHandler", async (request, reply) => {
  const path = request.url.split("?")[0];
  if (!path.startsWith("/api/") || ownerAuthExemptPaths.has(path)) return;
  const token = parseCookie(request.headers.cookie, OWNER_SESSION_COOKIE);
  const owner = await ownerAuth.getSessionOwner(token);
  if (!owner) {
    return reply.code(401).send({ error: "owner_authentication_required" });
  }
});

app.get("/api/auth/status", async (request) => {
  const configured = await ownerAuth.isConfigured();
  const token = parseCookie(request.headers.cookie, OWNER_SESSION_COOKIE);
  const owner = configured ? await ownerAuth.getSessionOwner(token) : undefined;
  return {
    configured,
    authenticated: Boolean(owner),
    setupProtection: configured ? "disabled" : ownerSetupProtection(request.ip),
    owner: owner ? { id: owner.id, email: owner.email } : null
  };
});

app.post<{ Body: { email?: string; password?: string } }>("/api/auth/setup", async (request, reply) => {
  if (await ownerAuth.isConfigured()) return reply.code(409).send({ error: "owner_already_configured" });
  const setupProtection = ownerSetupProtection(request.ip);
  if (setupProtection === "blocked_remote") return reply.code(403).send({ error: "remote_owner_setup_disabled" });
  if (setupProtection === "token_required" && !ownerSetupTokenMatches(request.headers["x-orrbit-setup-token"])) {
    return reply.code(403).send({ error: "owner_setup_token_required" });
  }
  try {
    const owner = await ownerAuth.setup(request.body?.email ?? "", request.body?.password ?? "");
    const session = await ownerAuth.createSession(owner);
    reply.header("Set-Cookie", createOwnerSessionCookie(session.token, secureOwnerCookie(request)));
    await audit(null, "owner_account_configured", { ownerId: owner.id });
    return reply.code(201).send({ configured: true, authenticated: true, owner: { id: owner.id, email: owner.email } });
  } catch (error) {
    return reply.code(400).send({ error: error instanceof Error ? error.message : "owner_setup_failed" });
  }
});

app.post<{ Body: { email?: string; password?: string } }>("/api/auth/login", async (request, reply) => {
  const rateKey = request.ip || "unknown";
  if (ownerLoginRateLimited(rateKey)) {
    return reply.code(429).send({ error: "too_many_login_attempts", retryAfterSeconds: 900 });
  }
  if (!await ownerAuth.isConfigured()) return reply.code(409).send({ error: "owner_setup_required" });
  const owner = await ownerAuth.authenticate(request.body?.email ?? "", request.body?.password ?? "");
  if (!owner) {
    recordOwnerLoginFailure(rateKey);
    return reply.code(401).send({ error: "invalid_owner_credentials" });
  }
  ownerLoginAttempts.delete(rateKey);
  const session = await ownerAuth.createSession(owner);
  reply.header("Set-Cookie", createOwnerSessionCookie(session.token, secureOwnerCookie(request)));
  await audit(null, "owner_login_succeeded", { ownerId: owner.id });
  return { configured: true, authenticated: true, owner: { id: owner.id, email: owner.email } };
});

app.post("/api/auth/logout", async (request, reply) => {
  const token = parseCookie(request.headers.cookie, OWNER_SESSION_COOKIE);
  await ownerAuth.revokeSession(token);
  reply.header("Set-Cookie", clearOwnerSessionCookie(secureOwnerCookie(request)));
  return { authenticated: false };
});

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

app.get("/api/panel-readiness", async () => getPanelReadiness());

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
  const [environments, projectJobs, auditEvents, projectWorkspaces, projectSecretReferences, projectDnsProposals, projectReleaseEvidence, projectVersions, projectRollbackPlans, projectBudget, projectCosts] = await Promise.all([
    registry.listEnvironments(project.id),
    listJobs(project.id),
    listAuditEvents(project.id),
    listDevelopmentWorkspaces(project.id),
    listSecretReferences(project.id),
    listDnsProposals(project.id),
    listReleaseEvidence(project.id),
    listVersionLedger(project.id),
    listRollbackPlans(project.id),
    getProjectBudget(project.id),
    listCostLedger(project.id)
  ]);
  return {
    project,
    environments,
    workspaces: projectWorkspaces,
    secretReferences: projectSecretReferences,
    dnsProposals: projectDnsProposals,
    releaseEvidence: projectReleaseEvidence,
    versions: projectVersions,
    rollbackPlans: projectRollbackPlans,
    budget: projectBudget,
    costLedger: projectCosts,
    costSummary: summarizeCostBudget(projectBudget, projectCosts),
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
  if (parsed.data.sourceMode === "import" && !realImportAccessAllowed(request)) {
    return reply.code(409).send(realImportLockedPayload());
  }
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

app.get<{ Params: { id: string } }>("/api/projects/:id/development-workspaces", async (request, reply) => {
  const project = await registry.get(request.params.id);
  if (!project) return reply.code(404).send({ error: "project_not_found" });
  return { workspaces: await listDevelopmentWorkspaces(project.id) };
});

app.post<{
  Params: { id: string };
  Body: { requestSummary?: string; baseBranch?: string };
}>("/api/projects/:id/development-workspaces", async (request, reply) => {
  const project = await registry.get(request.params.id);
  if (!project) return reply.code(404).send({ error: "project_not_found" });
  try {
    const workspace = createDevelopmentWorkspace({
      projectId: project.id,
      repositoryFullName: project.repository?.fullName,
      defaultBranch: project.repository?.defaultBranch,
      requestSummary: request.body?.requestSummary,
      baseBranch: request.body?.baseBranch
    });
    await saveDevelopmentWorkspace(workspace);
    await audit(project.id, "development_workspace_created", {
      workspaceId: workspace.id,
      repository: workspace.repositoryFullName,
      baseBranch: workspace.baseBranch,
      branchName: workspace.branchName
    });
    return reply.code(201).send(workspace);
  } catch (error) {
    return reply.code(400).send({ error: error instanceof Error ? error.message : "workspace_create_failed" });
  }
});

app.get<{ Params: { id: string } }>("/api/development-workspaces/:id", async (request, reply) => {
  const workspace = await getDevelopmentWorkspace(request.params.id);
  if (!workspace) return reply.code(404).send({ error: "development_workspace_not_found" });
  return workspace;
});

app.post<{ Params: { id: string } }>("/api/development-workspaces/:id/prepare-branch", async (request, reply) => {
  const workspace = await getDevelopmentWorkspace(request.params.id);
  if (!workspace) return reply.code(404).send({ error: "development_workspace_not_found" });
  try {
    const updated = await prepareBranchPlan(workspace, new DryRunGitHubProvider());
    await saveDevelopmentWorkspace(updated);
    await audit(updated.projectId, "development_workspace_branch_plan_ready", {
      workspaceId: updated.id,
      branchName: updated.branchName,
      providerMode: updated.branchPlan?.mode,
      executionAllowed: updated.branchPlan?.executionAllowed
    });
    return updated;
  } catch (error) {
    return reply.code(409).send({ error: error instanceof Error ? error.message : "branch_plan_failed" });
  }
});

app.post<{ Params: { id: string } }>("/api/development-workspaces/:id/prepare-review", async (request, reply) => {
  const workspace = await getDevelopmentWorkspace(request.params.id);
  if (!workspace) return reply.code(404).send({ error: "development_workspace_not_found" });
  try {
    const updated = await prepareReviewPlan(workspace, new DryRunGitHubProvider());
    await saveDevelopmentWorkspace(updated);
    await audit(updated.projectId, "development_workspace_review_plan_ready", {
      workspaceId: updated.id,
      branchName: updated.branchName,
      providerMode: updated.reviewPlan?.mode,
      executionAllowed: updated.reviewPlan?.executionAllowed
    });
    return updated;
  } catch (error) {
    return reply.code(409).send({ error: error instanceof Error ? error.message : "review_plan_failed" });
  }
});

app.post<{ Params: { id: string } }>("/api/development-workspaces/:id/cancel", async (request, reply) => {
  const workspace = await getDevelopmentWorkspace(request.params.id);
  if (!workspace) return reply.code(404).send({ error: "development_workspace_not_found" });
  const updated = cancelDevelopmentWorkspace(workspace);
  await saveDevelopmentWorkspace(updated);
  await audit(updated.projectId, "development_workspace_cancelled", {
    workspaceId: updated.id,
    branchName: updated.branchName,
    actualBranchCreated: updated.actualBranchCreated
  });
  return updated;
});

app.get<{ Params: { id: string } }>("/api/projects/:id/secret-references", async (request, reply) => {
  const project = await registry.get(request.params.id);
  if (!project) return reply.code(404).send({ error: "project_not_found" });
  return { secretReferences: await listSecretReferences(project.id) };
});

app.post<{
  Params: { id: string };
  Body: Record<string, unknown> & {
    environment?: string;
    secretName?: string;
    providerReference?: string;
  };
}>("/api/projects/:id/secret-references", async (request, reply) => {
  const project = await registry.get(request.params.id);
  if (!project) return reply.code(404).send({ error: "project_not_found" });
  try {
    rejectSecretValueFields(request.body ?? {});
    const record = await createSecretReference({
      projectId: project.id,
      environment: request.body?.environment,
      secretName: request.body?.secretName,
      providerReference: request.body?.providerReference
    }, new DryRunSecretProvider());
    await saveSecretReference(record);
    await audit(project.id, "secret_reference_created", {
      secretReferenceId: record.id,
      environment: record.environment,
      secretName: record.secretName,
      provider: record.provider,
      providerReference: record.providerReference,
      secretValueStored: false
    });
    return reply.code(201).send(record);
  } catch (error) {
    const message = error instanceof Error ? error.message : "secret_reference_failed";
    const duplicate = message.includes("duplicate key") || message.includes("already_exists");
    return reply.code(duplicate ? 409 : 400).send({ error: duplicate ? "secret_reference_already_exists" : message });
  }
});

app.get<{ Params: { id: string } }>("/api/projects/:id/dns-proposals", async (request, reply) => {
  const project = await registry.get(request.params.id);
  if (!project) return reply.code(404).send({ error: "project_not_found" });
  return { dnsProposals: await listDnsProposals(project.id) };
});

app.post<{
  Params: { id: string };
  Body: {
    domain?: string;
    action?: string;
    recordType?: string;
    recordName?: string;
    proposedValue?: string | null;
    ttl?: number;
  };
}>("/api/projects/:id/dns-proposals", async (request, reply) => {
  const project = await registry.get(request.params.id);
  if (!project) return reply.code(404).send({ error: "project_not_found" });
  try {
    const proposal = createDnsChangeProposal({ projectId: project.id, ...request.body });
    await saveDnsProposal(proposal);
    await audit(project.id, "dns_change_proposal_created", {
      proposalId: proposal.id,
      domain: proposal.domain,
      action: proposal.action,
      recordType: proposal.recordType,
      recordName: proposal.recordName,
      restorePointRequired: proposal.restorePointRequired,
      executionAllowed: proposal.executionAllowed
    });
    return reply.code(201).send(proposal);
  } catch (error) {
    return reply.code(400).send({ error: error instanceof Error ? error.message : "dns_proposal_failed" });
  }
});

app.get<{ Params: { id: string } }>("/api/dns-proposals/:id", async (request, reply) => {
  const proposal = await getDnsProposal(request.params.id);
  if (!proposal) return reply.code(404).send({ error: "dns_proposal_not_found" });
  return proposal;
});

app.post<{ Params: { id: string } }>("/api/dns-proposals/:id/approve", async (request, reply) => {
  const proposal = await getDnsProposal(request.params.id);
  if (!proposal) return reply.code(404).send({ error: "dns_proposal_not_found" });
  try {
    const updated = approveDnsProposal(proposal);
    await saveDnsProposal(updated);
    await audit(updated.projectId, "dns_change_proposal_approved", {
      proposalId: updated.id,
      domain: updated.domain,
      executionAllowed: updated.executionAllowed,
      restorePointRequired: updated.restorePointRequired
    });
    return updated;
  } catch (error) {
    return reply.code(409).send({ error: error instanceof Error ? error.message : "dns_proposal_approve_failed" });
  }
});

app.post<{ Params: { id: string } }>("/api/dns-proposals/:id/cancel", async (request, reply) => {
  const proposal = await getDnsProposal(request.params.id);
  if (!proposal) return reply.code(404).send({ error: "dns_proposal_not_found" });
  const updated = cancelDnsProposal(proposal);
  await saveDnsProposal(updated);
  await audit(updated.projectId, "dns_change_proposal_cancelled", {
    proposalId: updated.id,
    domain: updated.domain,
    executionAllowed: updated.executionAllowed
  });
  return updated;
});

app.post<{ Params: { id: string } }>("/api/dns-proposals/:id/execute", async (request, reply) => {
  const proposal = await getDnsProposal(request.params.id);
  if (!proposal) return reply.code(404).send({ error: "dns_proposal_not_found" });
  return reply.code(409).send({
    error: "dns_execution_locked",
    proposalId: proposal.id,
    status: proposal.status,
    executionAllowed: false,
    protections: proposal.protections
  });
});

app.get<{ Params: { id: string } }>("/api/projects/:id/release-evidence", async (request, reply) => {
  const project = await registry.get(request.params.id);
  if (!project) return reply.code(404).send({ error: "project_not_found" });
  return { releaseEvidence: await listReleaseEvidence(project.id) };
});

app.post<{
  Params: { id: string };
  Body: {
    environment?: string;
    sourceRevision?: string;
    buildResult?: string;
    testResult?: string;
    healthResult?: string;
    deploymentIdentifier?: string | null;
    healthReference?: string;
  };
}>("/api/projects/:id/release-evidence", async (request, reply) => {
  const project = await registry.get(request.params.id);
  if (!project) return reply.code(404).send({ error: "project_not_found" });
  try {
    const bundle = createReleaseEvidence({ projectId: project.id, ...request.body });
    await saveReleaseEvidence(bundle);
    await audit(project.id, "release_evidence_created", {
      releaseEvidenceId: bundle.id,
      environment: bundle.environment,
      complete: bundle.complete,
      blockers: bundle.blockers,
      sourceRevision: bundle.sourceRevision
    });
    return reply.code(201).send(bundle);
  } catch (error) {
    return reply.code(400).send({ error: error instanceof Error ? error.message : "release_evidence_failed" });
  }
});

app.get<{ Params: { id: string } }>("/api/release-evidence/:id", async (request, reply) => {
  const bundle = await getReleaseEvidence(request.params.id);
  if (!bundle) return reply.code(404).send({ error: "release_evidence_not_found" });
  return bundle;
});

app.post<{ Params: { id: string } }>("/api/release-evidence/:id/verify", async (request, reply) => {
  const bundle = await getReleaseEvidence(request.params.id);
  if (!bundle) return reply.code(404).send({ error: "release_evidence_not_found" });
  try {
    const updated = verifyReleaseEvidence(bundle);
    await saveReleaseEvidence(updated);
    await audit(updated.projectId, "release_evidence_verification_completed", {
      releaseEvidenceId: updated.id,
      environment: updated.environment,
      status: updated.status,
      blockers: updated.blockers
    });
    return updated;
  } catch (error) {
    return reply.code(409).send({ error: error instanceof Error ? error.message : "release_evidence_verify_failed" });
  }
});

app.post<{ Params: { id: string } }>("/api/release-evidence/:id/deploy", async (request, reply) => {
  const bundle = await getReleaseEvidence(request.params.id);
  if (!bundle) return reply.code(404).send({ error: "release_evidence_not_found" });
  return reply.code(409).send({
    error: "release_execution_locked",
    releaseEvidenceId: bundle.id,
    status: bundle.status,
    protections: bundle.protections
  });
});

app.get<{ Params: { id: string } }>("/api/projects/:id/version-history", async (request, reply) => {
  const project = await registry.get(request.params.id);
  if (!project) return reply.code(404).send({ error: "project_not_found" });
  return { versions: await listVersionLedger(project.id), rollbackPlans: await listRollbackPlans(project.id) };
});

app.post<{
  Params: { id: string };
  Body: { releaseEvidenceId?: string };
}>("/api/projects/:id/version-history", async (request, reply) => {
  const project = await registry.get(request.params.id);
  if (!project) return reply.code(404).send({ error: "project_not_found" });
  const bundle = request.body?.releaseEvidenceId ? await getReleaseEvidence(request.body.releaseEvidenceId) : undefined;
  if (!bundle || bundle.projectId !== project.id) return reply.code(404).send({ error: "release_evidence_not_found" });
  if (bundle.status !== "verified") return reply.code(409).send({ error: "verified_release_evidence_required" });
  const entry = createVersionLedgerEntry({
    projectId: project.id,
    environment: bundle.environment,
    sourceRevision: bundle.sourceRevision,
    releaseEvidenceId: bundle.id,
    deploymentIdentifier: bundle.deploymentIdentifier,
    healthVerified: bundle.healthResult === "passed"
  });
  await saveVersionLedgerEntry(entry);
  await audit(project.id, "version_ledger_entry_created", {
    versionId: entry.id,
    environment: entry.environment,
    sourceRevision: entry.sourceRevision,
    releaseEvidenceId: entry.releaseEvidenceId
  });
  return reply.code(201).send(entry);
});

app.post<{
  Params: { id: string };
  Body: { environment?: string; fromVersionId?: string; toVersionId?: string };
}>("/api/projects/:id/rollback-plans", async (request, reply) => {
  const project = await registry.get(request.params.id);
  if (!project) return reply.code(404).send({ error: "project_not_found" });
  const versions = await listVersionLedger(project.id);
  const from = versions.find((item) => item.id === request.body?.fromVersionId);
  const to = versions.find((item) => item.id === request.body?.toVersionId);
  if (!from || !to) return reply.code(404).send({ error: "rollback_version_not_found" });
  if (from.environment !== request.body?.environment || to.environment !== request.body?.environment) {
    return reply.code(400).send({ error: "rollback_versions_environment_mismatch" });
  }
  try {
    const plan = createRollbackPlan({
      projectId: project.id,
      environment: request.body?.environment ?? "",
      fromVersionId: from.id,
      toVersionId: to.id
    });
    await saveRollbackPlan(plan);
    await audit(project.id, "rollback_plan_created", {
      rollbackPlanId: plan.id,
      environment: plan.environment,
      fromVersionId: plan.fromVersionId,
      toVersionId: plan.toVersionId,
      executionAllowed: plan.executionAllowed
    });
    return reply.code(201).send(plan);
  } catch (error) {
    return reply.code(400).send({ error: error instanceof Error ? error.message : "rollback_plan_failed" });
  }
});

app.post<{ Params: { id: string } }>("/api/rollback-plans/:id/approve", async (request, reply) => {
  const plan = await getRollbackPlan(request.params.id);
  if (!plan) return reply.code(404).send({ error: "rollback_plan_not_found" });
  try {
    const updated = approveRollbackPlan(plan);
    await saveRollbackPlan(updated);
    await audit(updated.projectId, "rollback_plan_approved", {
      rollbackPlanId: updated.id,
      executionAllowed: updated.executionAllowed,
      restorePointRequired: updated.restorePointRequired
    });
    return updated;
  } catch (error) {
    return reply.code(409).send({ error: error instanceof Error ? error.message : "rollback_approve_failed" });
  }
});

app.post<{ Params: { id: string } }>("/api/rollback-plans/:id/cancel", async (request, reply) => {
  const plan = await getRollbackPlan(request.params.id);
  if (!plan) return reply.code(404).send({ error: "rollback_plan_not_found" });
  const updated = cancelRollbackPlan(plan);
  await saveRollbackPlan(updated);
  return updated;
});

app.post<{ Params: { id: string } }>("/api/rollback-plans/:id/execute", async (request, reply) => {
  const plan = await getRollbackPlan(request.params.id);
  if (!plan) return reply.code(404).send({ error: "rollback_plan_not_found" });
  return reply.code(409).send({ error: "rollback_execution_locked", rollbackPlanId: plan.id, executionAllowed: false, protections: plan.protections });
});

app.get<{ Params: { id: string } }>("/api/projects/:id/costs", async (request, reply) => {
  const project = await registry.get(request.params.id);
  if (!project) return reply.code(404).send({ error: "project_not_found" });
  const [budget, entries] = await Promise.all([getProjectBudget(project.id), listCostLedger(project.id)]);
  return { budget, entries, summary: summarizeCostBudget(budget, entries) };
});

app.put<{
  Params: { id: string };
  Body: { currency?: string; monthlyLimit?: number; warningPercent?: number };
}>("/api/projects/:id/budget", async (request, reply) => {
  const project = await registry.get(request.params.id);
  if (!project) return reply.code(404).send({ error: "project_not_found" });
  try {
    const budget = createProjectBudget({
      projectId: project.id,
      currency: request.body?.currency,
      monthlyLimit: request.body?.monthlyLimit,
      warningPercent: request.body?.warningPercent
    });
    await saveProjectBudget(budget);
    await audit(project.id, "project_budget_updated", {
      currency: budget.currency,
      monthlyLimit: budget.monthlyLimit,
      warningPercent: budget.warningPercent
    });
    const entries = await listCostLedger(project.id);
    return { budget, summary: summarizeCostBudget(budget, entries) };
  } catch (error) {
    return reply.code(400).send({ error: error instanceof Error ? error.message : "budget_update_failed" });
  }
});

app.post<{
  Params: { id: string };
  Body: { provider?: string; category?: string; amount?: number; currency?: string; note?: string; occurredAt?: string };
}>("/api/projects/:id/costs", async (request, reply) => {
  const project = await registry.get(request.params.id);
  if (!project) return reply.code(404).send({ error: "project_not_found" });
  try {
    const entry = createCostLedgerEntry({ projectId: project.id, ...request.body });
    await saveCostLedgerEntry(entry);
    const [budget, entries] = await Promise.all([getProjectBudget(project.id), listCostLedger(project.id)]);
    const summary = summarizeCostBudget(budget, entries);
    await audit(project.id, "cost_ledger_entry_created", {
      costEntryId: entry.id,
      provider: entry.provider,
      category: entry.category,
      amount: entry.amount,
      currency: entry.currency,
      budgetStatus: summary.status,
      automationBlocked: summary.automationBlocked
    });
    return reply.code(201).send({ entry, summary });
  } catch (error) {
    return reply.code(400).send({ error: error instanceof Error ? error.message : "cost_entry_failed" });
  }
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
  if (plan.sourceMode === "import" && !realImportAccessAllowed(request)) {
    return reply.code(409).send(realImportLockedPayload());
  }
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
  if (!realImportAccessAllowed(request)) return reply.code(409).send(realImportLockedPayload());
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
  if (!realImportAccessAllowed(request)) return reply.code(409).send(realImportLockedPayload());
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
  if (!realImportAccessAllowed(request)) return reply.code(409).send(realImportLockedPayload());
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
  const workspace = createImportWorkspaceFromPlan(approvedPlan, { projectId: project.id });
  await saveImportPlan(approvedPlan);
  await saveImportWorkspace(workspace);
  await saveJob(job, `Import plan approved for ${plan.requestedProjectName}`);
  await audit(project.id, "import_plan_approved", { importPlanId: plan.id, jobId: job.id, workspaceId: workspace.id, evidence: job.evidence });
  return reply.code(201).send({ approved: true, project, job, importPlan: approvedPlan, workspace });
});

app.get("/api/import-workspaces", async () => ({ workspaces: await listImportWorkspaces() }));

app.get<{ Params: { id: string } }>("/api/import-workspaces/:id", async (request, reply) => {
  const workspace = await getImportWorkspace(request.params.id);
  if (!workspace) return reply.code(404).send({ error: "import_workspace_not_found" });
  return workspace;
});

app.get<{ Params: { id: string } }>("/api/import-workspaces/:id/deploy-gate", async (request, reply) => {
  const workspace = await getImportWorkspace(request.params.id);
  if (!workspace) return reply.code(404).send({ error: "import_workspace_not_found" });
  return evaluateImportWorkspaceDeployGate(workspace);
});


app.patch<{ Params: { id: string }; Body: { sourceRef?: string } }>("/api/import-workspaces/:id/source-reference", async (request, reply) => {
  if (!realImportAccessAllowed(request)) return reply.code(409).send(realImportLockedPayload());
  const workspace = await getImportWorkspace(request.params.id);
  if (!workspace) return reply.code(404).send({ error: "import_workspace_not_found" });
  try {
    const updated = confirmImportWorkspaceSourceReference(workspace, request.body?.sourceRef ?? "");
    await saveImportWorkspace(updated);
    await audit(updated.projectId ?? null, "import_workspace_source_confirmed", { workspaceId: updated.id, sourceRef: updated.sourceRef });
    return updated;
  } catch (error) {
    return reply.code(400).send({ error: error instanceof Error ? error.message : "source_reference_update_failed" });
  }
});

app.patch<{
  Params: { id: string };
  Body: { kind?: WorkspaceCaptureKind; key?: string; status?: WorkspaceCaptureStatus };
}>("/api/import-workspaces/:id/capture", async (request, reply) => {
  if (!realImportAccessAllowed(request)) return reply.code(409).send(realImportLockedPayload());
  const workspace = await getImportWorkspace(request.params.id);
  if (!workspace) return reply.code(404).send({ error: "import_workspace_not_found" });
  if (!request.body?.kind || !request.body?.key || !request.body?.status) {
    return reply.code(400).send({ error: "capture_kind_key_status_required" });
  }
  try {
    const updated = updateImportWorkspaceCaptureItem(workspace, {
      kind: request.body.kind,
      key: request.body.key,
      status: request.body.status
    });
    await saveImportWorkspace(updated);
    await audit(updated.projectId ?? null, "import_workspace_capture_updated", {
      workspaceId: updated.id, kind: request.body.kind, key: request.body.key, status: request.body.status,
      canDeploy: updated.deployGate.canDeploy
    });
    return updated;
  } catch (error) {
    return reply.code(409).send({ error: error instanceof Error ? error.message : "capture_update_failed" });
  }
});

app.post<{ Params: { id: string } }>("/api/import-workspaces/:id/source-acquisitions", async (request, reply) => {
  if (!realImportAccessAllowed(request)) return reply.code(409).send(realImportLockedPayload());
  const workspace = await getImportWorkspace(request.params.id);
  if (!workspace) return reply.code(404).send({ error: "import_workspace_not_found" });
  try {
    const part = await request.file();
    if (!part) return reply.code(400).send({ error: "source_zip_required" });
    const archiveName = basename(part.filename || "source.zip");
    if (!archiveName.toLowerCase().endsWith(".zip")) return reply.code(400).send({ error: "zip_archive_required" });
    const archiveBuffer = await part.toBuffer();
    const sha256 = createHash("sha256").update(archiveBuffer).digest("hex");
    const zip = new AdmZip(archiveBuffer);
    const entries = zip.getEntries().map((entry) => ({
      name: entry.entryName,
      size: Number(entry.header.size ?? 0),
      directory: entry.isDirectory
    }));
    const record = createSourceAcquisitionRecord({
      workspace, archiveName, archiveSizeBytes: archiveBuffer.length, sha256, entries
    });
    if (record.status !== "acquired") {
      await saveSourceAcquisition(record);
      await audit(record.projectId, "source_acquisition_rejected", {
        workspaceId: workspace.id, acquisitionId: record.id, issues: record.issues
      });
      return reply.code(422).send({ acquisition: record, error: "source_package_rejected" });
    }
    let snapshotRef: string;
    try {
      snapshotRef = await extractAcquiredSource(record, archiveBuffer, zip);
    } catch (error) {
      await rm(acquisitionFolder(record.workspaceId, record.id), { recursive: true, force: true });
      throw error;
    }
    await saveSourceAcquisition(record);
    await audit(record.projectId, "source_acquisition_completed", {
      workspaceId: workspace.id, acquisitionId: record.id, sha256: record.sha256,
      fileCount: record.inventory.fileCount, codeFiles: record.inventory.codeFiles
    });
    return reply.code(201).send({ acquisition: record, snapshotRef });
  } catch (error) {
    return reply.code(400).send({ error: error instanceof Error ? error.message : "source_acquisition_failed" });
  }
});

app.get<{ Params: { id: string } }>("/api/import-workspaces/:id/source-acquisitions", async (request, reply) => {
  const workspace = await getImportWorkspace(request.params.id);
  if (!workspace) return reply.code(404).send({ error: "import_workspace_not_found" });
  return { acquisitions: await listSourceAcquisitions(workspace.id) };
});

app.get<{ Params: { id: string } }>("/api/source-acquisitions/:id", async (request, reply) => {
  const record = await getSourceAcquisition(request.params.id);
  if (!record) return reply.code(404).send({ error: "source_acquisition_not_found" });
  return record;
});

app.post<{ Params: { id: string } }>("/api/source-acquisitions/:id/discard", async (request, reply) => {
  const record = await getSourceAcquisition(request.params.id);
  if (!record) return reply.code(404).send({ error: "source_acquisition_not_found" });
  const builds = await listSourceBuildJobs(record.id);
  for (const build of builds) {
    if (build.status === "reset") continue;
    const resetBuild = await resetSourceBuildJob(build, importInboxRoot);
    await saveSourceBuildJob(resetBuild);
  }
  const discarded = discardSourceAcquisition(record);
  await rm(acquisitionFolder(record.workspaceId, record.id), { recursive: true, force: true });
  await saveSourceAcquisition(discarded);
  await audit(record.projectId, "source_acquisition_discarded", {
    workspaceId: record.workspaceId, acquisitionId: record.id
  });
  return discarded;
});

app.get("/api/source-builds/sandbox-status", async () => ({
  dockerSandboxReady: await isDockerSandboxReady(),
  hostExecutionDisabled: true,
  buildNetworkDisabled: true
}));

app.post<{ Params: { id: string } }>("/api/source-acquisitions/:id/builds", async (request, reply) => {
  if (!realImportAccessAllowed(request)) return reply.code(409).send(realImportLockedPayload());
  const record = await getSourceAcquisition(request.params.id);
  if (!record) return reply.code(404).send({ error: "source_acquisition_not_found" });
  const previous = await listSourceBuildJobs(record.id);
  if (previous.some((job) => job.preview?.containerName && job.status === "preview_ready")) {
    return reply.code(409).send({ error: "active_preview_exists_reset_first" });
  }
  const queued = createSourceBuildJob(record);
  await saveSourceBuildJob(queued);
  try {
    const completed = await runSourceBuild(queued, record, importInboxRoot);
    await saveSourceBuildJob(completed);
    await audit(record.projectId, "source_build_completed", {
      acquisitionId: record.id,
      buildId: completed.id,
      status: completed.status,
      framework: completed.framework,
      packageManager: completed.packageManager,
      artifactDirectory: completed.artifactDirectory,
      blockers: completed.blockers,
      previewUrl: completed.preview?.url ?? null
    });
    return reply.code(201).send(completed);
  } catch (error) {
    queued.status = "failed";
    queued.blockers = ["source_build_runner_error"];
    queued.logs.push(error instanceof Error ? error.message : "source_build_runner_error");
    queued.updatedAt = new Date().toISOString();
    await saveSourceBuildJob(queued);
    return reply.code(500).send(queued);
  }
});

app.get<{ Params: { id: string } }>("/api/source-acquisitions/:id/builds", async (request, reply) => {
  const record = await getSourceAcquisition(request.params.id);
  if (!record) return reply.code(404).send({ error: "source_acquisition_not_found" });
  return { builds: await listSourceBuildJobs(record.id) };
});

app.get<{ Params: { id: string } }>("/api/source-builds/:id", async (request, reply) => {
  const job = await getSourceBuildJob(request.params.id);
  if (!job) return reply.code(404).send({ error: "source_build_not_found" });
  return job;
});

app.post<{ Params: { id: string } }>("/api/source-builds/:id/reset", async (request, reply) => {
  const job = await getSourceBuildJob(request.params.id);
  if (!job) return reply.code(404).send({ error: "source_build_not_found" });
  const reset = await resetSourceBuildJob(job, importInboxRoot);
  await saveSourceBuildJob(reset);
  await audit(reset.projectId, "source_build_reset", { buildId: reset.id, acquisitionId: reset.acquisitionId });
  return reset;
});

app.post<{ Params: { id: string } }>("/api/source-builds/:id/deploy", async (request, reply) => {
  const job = await getSourceBuildJob(request.params.id);
  if (!job) return reply.code(404).send({ error: "source_build_not_found" });
  return reply.code(409).send({
    error: "real_deployment_locked",
    targetEnvironment: "development",
    protections: job.protections
  });
});

app.post<{ Params: { id: string } }>("/api/import-workspaces/:id/executions", async (request, reply) => {
  if (!realImportAccessAllowed(request)) return reply.code(409).send(realImportLockedPayload());
  const workspace = await getImportWorkspace(request.params.id);
  if (!workspace) return reply.code(404).send({ error: "import_workspace_not_found" });
  try {
    const queued = createDevelopmentImportExecution(workspace);
    await saveImportExecution(queued);
    const completed = runDevelopmentImportExecution(queued, workspace);
    await saveImportExecution(completed);
    await audit(workspace.projectId ?? null, "development_import_preview_ready", {
      workspaceId: workspace.id, executionId: completed.id, status: completed.status,
      routeParity: completed.parity.routes, moduleParity: completed.parity.modules
    });
    return reply.code(201).send(completed);
  } catch (error) {
    return reply.code(409).send({ error: error instanceof Error ? error.message : "import_execution_blocked" });
  }
});

app.get<{ Params: { id: string } }>("/api/import-workspaces/:id/executions", async (request, reply) => {
  const workspace = await getImportWorkspace(request.params.id);
  if (!workspace) return reply.code(404).send({ error: "import_workspace_not_found" });
  return { executions: await listImportExecutions(workspace.id) };
});

app.get<{ Params: { id: string } }>("/api/import-executions/:id", async (request, reply) => {
  const execution = await getImportExecution(request.params.id);
  if (!execution) return reply.code(404).send({ error: "import_execution_not_found" });
  return execution;
});

app.post<{ Params: { id: string } }>("/api/import-executions/:id/reset", async (request, reply) => {
  const execution = await getImportExecution(request.params.id);
  if (!execution) return reply.code(404).send({ error: "import_execution_not_found" });
  const reset = resetDevelopmentImportExecution(execution);
  await saveImportExecution(reset);
  await audit(reset.projectId, "development_import_preview_reset", { executionId: reset.id, workspaceId: reset.workspaceId });
  return reset;
});

app.post<{ Params: { id: string } }>("/api/import-executions/:id/deploy", async (request, reply) => {
  const execution = await getImportExecution(request.params.id);
  if (!execution) return reply.code(404).send({ error: "import_execution_not_found" });
  return reply.code(409).send({
    error: "real_deployment_locked",
    targetEnvironment: "development",
    protections: execution.protections
  });
});

app.post<{ Params: { id: string } }>("/api/import-plans/:id/workspace", async (request, reply) => {
  if (!realImportAccessAllowed(request)) return reply.code(409).send(realImportLockedPayload());
  const plan = await getImportPlan(request.params.id);
  if (!plan) return reply.code(404).send({ error: "import_plan_not_found" });
  if (plan.targetEnvironment !== "development") return reply.code(409).send({ error: "non_development_workspace_locked" });
  const workspace = createImportWorkspaceFromPlan(plan);
  await saveImportWorkspace(workspace);
  await audit(null, "import_workspace_created", { importPlanId: plan.id, workspaceId: workspace.id, status: workspace.status });
  return reply.code(201).send(workspace);
});
app.get("/api/jobs", async () => ({ jobs: await listJobs() }));

app.post<{ Body: { environment: "development" | "staging" | "production"; action: string; destructive?: boolean } }>(
  "/api/policy/evaluate",
  async (request) => ({ risk: classifyRisk(request.body), requiresApproval: requiresApproval(request.body) })
);

const port = Number(process.env.PORT ?? 8080);
await app.listen({ port, host: "0.0.0.0" });
