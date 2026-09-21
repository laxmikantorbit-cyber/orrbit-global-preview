import { useEffect, useState, type FormEvent } from "react";

type Project = {
  id: string;
  name: string;
  type: string;
  sourceMode?: string;
  lifecycleStatus: string;
  environments: string[];
  productionProtected?: boolean;
  repository?: { fullName: string; defaultBranch: string };
};

type ProjectEnvironment = {
  id: string;
  projectId: string;
  environmentName: string;
  status: string;
  frontendProvider?: string;
  backendProvider?: string;
  databaseProvider?: string;
  region?: string;
};

type DevelopmentWorkspace = {
  id: string;
  projectId: string;
  repositoryFullName: string;
  baseBranch: string;
  branchName: string;
  requestSummary: string;
  status: string;
  branchPlan: null | { provider: string; action: string; mode: string; risk: string; requiresApproval: boolean; executionAllowed: boolean; notes: string[] };
  reviewPlan: null | { provider: string; action: string; mode: string; risk: string; requiresApproval: boolean; executionAllowed: boolean; notes: string[] };
  actualBranchCreated: boolean;
  protections: Record<string, boolean>;
  createdAt: string;
  updatedAt: string;
};

type SecretReference = {
  id: string;
  projectId: string;
  environment: string;
  secretName: string;
  provider: string;
  providerReference: string;
  providerPlan: { provider: string; action: string; mode: string; risk: string; requiresApproval: boolean; executionAllowed: boolean; notes: string[] };
  status: string;
  secretValueStored: false;
  createdAt: string;
  updatedAt: string;
};

type DnsProposal = {
  id: string;
  projectId: string;
  domain: string;
  action: string;
  recordType: string;
  recordName: string;
  proposedValue: string | null;
  ttl: number;
  status: string;
  risk: "high";
  requiresApproval: true;
  restorePointRequired: true;
  executionAllowed: false;
  protections: Record<string, boolean>;
  createdAt: string;
  updatedAt: string;
};

type ReleaseEvidence = {
  id: string; projectId: string; environment: string; sourceRevision: string;
  buildResult: string; testResult: string; healthResult: string;
  deploymentIdentifier: string | null; healthReference: string;
  status: string; complete: boolean; blockers: string[]; protections: Record<string, boolean>;
  createdAt: string; updatedAt: string;
};

type VersionLedgerEntry = {
  id: string; projectId: string; environment: string; sourceRevision: string;
  releaseEvidenceId: string; deploymentIdentifier: string | null; healthVerified: boolean; createdAt: string;
};

type RollbackPlan = {
  id: string; projectId: string; environment: string; fromVersionId: string; toVersionId: string;
  status: string; risk: "high"; requiresApproval: true; executionAllowed: false;
  restorePointRequired: true; protections: Record<string, boolean>; createdAt: string; updatedAt: string;
};

type CommandCentre = {
  project: Project;
  environments: ProjectEnvironment[];
  workspaces: DevelopmentWorkspace[];
  secretReferences: SecretReference[];
  dnsProposals: DnsProposal[];
  releaseEvidence: ReleaseEvidence[];
  versions: VersionLedgerEntry[];
  rollbackPlans: RollbackPlan[];
  jobs: Array<{ id: string; state: string; risk: string; createdAt: string; evidence: string[] }>;
  audit: Array<{ id: string; eventType: string; createdAt: string }>;
  protection: { productionProtected: boolean; nonDevelopmentConfigLocked: boolean; realCloudProvisioningEnabled: boolean };
};

type Plan = {
  id: string;
  planner: string;
  suggestedName: string;
  inferredProjectType: string;
  sourceMode: string;
  targetEnvironment: string;
  risk: string;
  actions: string[];
  requiresApproval: boolean;
  executionAllowed: boolean;
};

type ImportPlan = {
  id: string;
  sourceType: string;
  sourceRef: string;
  requestedProjectName: string;
  projectType: string;
  targetEnvironment: string;
  risk: string;
  status: string;
  routeInventory: Array<{ path: string; kind: string; status: string; notes: string }>;
  manifestDraft: { projectName: string; projectType: string; targetEnvironment: string; productionProtected: boolean };
  actions: string[];
  blockedActions: string[];
  createdAt: string;
};

type CaptureRecord = { key: string; label: string; status: string; blocker?: string };
type RouteCaptureRecord = { path: string; kind: string; status: string; notes?: string };
type ModuleCaptureRecord = { name: string; status: string; notes?: string };

type ImportWorkspace = {
  id: string;
  importPlanId: string;
  projectId?: string;
  projectName: string;
  sourceType: string;
  sourceRef: string;
  status: string;
  sourceReferenceStatus: string;
  routeCapture: RouteCaptureRecord[];
  moduleCapture: ModuleCaptureRecord[];
  captureChecklist: CaptureRecord[];
  deployGate: { canDeploy: boolean; blockers: string[] };
};

type DeployGate = { canDeploy: boolean; blockers: string[] };
type SourceAcquisition = {
  id: string;
  workspaceId: string;
  projectId: string;
  sourceType: "chatgpt-sites-export";
  archiveName: string;
  archiveSizeBytes: number;
  sha256: string;
  status: string;
  inventory: {
    fileCount: number;
    totalBytes: number;
    codeFiles: number;
    assetFiles: number;
    hasPackageJson: boolean;
    packageJsonPath?: string;
    topLevelEntries: string[];
    routeHints: string[];
  };
  issues: string[];
  evidence: string[];
  createdAt: string;
  updatedAt: string;
};
type SourceBuild = {
  id: string;
  acquisitionId: string;
  workspaceId: string;
  projectId: string;
  status: string;
  framework: string;
  packageManager: string;
  projectSubdir: string;
  installCommand: string;
  buildCommand: string;
  stages: Array<{ name: string; status: string; detail?: string }>;
  artifactDirectory: string | null;
  preview: null | { containerName: string; networkName: string; url: string; hostPort: number; kind: string };
  logs: string[];
  blockers: string[];
  protections: Record<string, boolean>;
  createdAt: string;
  updatedAt: string;
};
type AuthStatus = {
  configured: boolean;
  authenticated: boolean;
  setupProtection?: "local" | "token_required" | "blocked_remote" | "disabled";
  owner: null | { id: string; email: string };
};

type PanelReadiness = {
  panelComplete: boolean;
  realImportUnlocked: boolean;
  standingRule: string;
  completion: { ready: number; partial: number; pending: number; total: number; buildProgressPercent: number };
  modules: Array<{ key: string; name: string; status: "ready" | "partial" | "pending"; summary: string }>;
};

type ImportExecution = {
  id: string;
  workspaceId: string;
  projectId: string;
  projectName: string;
  mode: "captured-inventory-preview";
  targetEnvironment: "development";
  status: string;
  stages: Array<{ name: string; status: string; evidence: string[] }>;
  parity: {
    routes: { expected: number; imported: number; missing: string[] };
    modules: { expected: number; imported: number; missing: string[] };
  };
  preview: null | { kind: string; reference: string; realDeploymentEnabled: false };
  protections: Record<string, boolean>;
  evidence: string[];
  createdAt: string;
  updatedAt: string;
};

const apiBase = import.meta.env.VITE_API_BASE_URL ?? "";

export default function App() {
  const [authStatus, setAuthStatus] = useState<AuthStatus | null>(null);
  const [authError, setAuthError] = useState("");
  const [projects, setProjects] = useState<Project[]>([]);
  const [panelReadiness, setPanelReadiness] = useState<PanelReadiness | null>(null);
  const [addOpen, setAddOpen] = useState(false);
  const [mode, setMode] = useState<"manual" | "ai">("manual");
  const [message, setMessage] = useState("Ready");
  const [plan, setPlan] = useState<Plan | null>(null);
  const [planName, setPlanName] = useState("");
  const [planRepository, setPlanRepository] = useState("");
  const [commandCentre, setCommandCentre] = useState<CommandCentre | null>(null);
  const [importPlans, setImportPlans] = useState<ImportPlan[]>([]);
  const [activeImportPlan, setActiveImportPlan] = useState<ImportPlan | null>(null);
  const [importWorkspaces, setImportWorkspaces] = useState<ImportWorkspace[]>([]);
  const [activeWorkspace, setActiveWorkspace] = useState<ImportWorkspace | null>(null);
  const [deployGate, setDeployGate] = useState<DeployGate | null>(null);
  const [activeExecution, setActiveExecution] = useState<ImportExecution | null>(null);
  const [activeAcquisition, setActiveAcquisition] = useState<SourceAcquisition | null>(null);
  const [activeBuild, setActiveBuild] = useState<SourceBuild | null>(null);
  const [sandboxReady, setSandboxReady] = useState<boolean | null>(null);
  const [sourcePackage, setSourcePackage] = useState<File | null>(null);
  const [sourceRefDraft, setSourceRefDraft] = useState("");
  const [envDraft, setEnvDraft] = useState({
    status: "unconfigured",
    frontendProvider: "",
    backendProvider: "",
    databaseProvider: "",
    region: ""
  });
  useEffect(() => {
    loadAuthStatus();
  }, []);

  useEffect(() => {
    if (!authStatus?.authenticated) return;
    fetch(`${apiBase}/api/projects`)
      .then((r) => r.json())
      .then((d) => setProjects(d.projects ?? []))
      .catch(() => setMessage("API offline"));
    loadPanelReadiness();
    loadImportPlans();
    loadImportWorkspaces();
  }, [authStatus?.authenticated]);

  async function loadAuthStatus() {
    try {
      const response = await fetch(`${apiBase}/api/auth/status`);
      const result = await response.json();
      if (response.ok) setAuthStatus(result);
      else setAuthError(result.error ?? "Unable to check owner access");
    } catch {
      setAuthError("Control API is offline");
    }
  }

  async function submitOwnerAccess(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const data = new FormData(event.currentTarget);
    const configured = Boolean(authStatus?.configured);
    setAuthError("");
    const setupToken = String(data.get("setupToken") ?? "").trim();
    const headers: Record<string, string> = { "Content-Type": "application/json" };
    if (!configured && setupToken) headers["x-orrbit-setup-token"] = setupToken;
    const response = await fetch(`${apiBase}/api/auth/${configured ? "login" : "setup"}`, {
      method: "POST",
      headers,
      body: JSON.stringify({ email: String(data.get("email") ?? ""), password: String(data.get("password") ?? "") })
    });
    const result = await response.json();
    if (!response.ok) {
      setAuthError(result.error ?? (configured ? "Sign in failed" : "Owner setup failed"));
      return;
    }
    setAuthStatus(result);
    event.currentTarget.reset();
  }

  async function logoutOwner() {
    const response = await fetch(`${apiBase}/api/auth/logout`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: "{}"
    });
    if (!response.ok) return setMessage("Sign out failed");
    setAuthStatus({ configured: true, authenticated: false, owner: null });
    setProjects([]);
    setPanelReadiness(null);
    setCommandCentre(null);
    setImportPlans([]);
    setImportWorkspaces([]);
    setMessage("Signed out");
  }

  async function loadPanelReadiness() {
    const response = await fetch(`${apiBase}/api/panel-readiness`);
    const result = await response.json();
    if (response.ok) setPanelReadiness(result);
  }

  async function loadImportPlans() {
    const response = await fetch(`${apiBase}/api/import-plans`);
    const result = await response.json();
    if (response.ok) setImportPlans(result.importPlans ?? []);
  }

  async function loadImportWorkspaces() {
    const response = await fetch(`${apiBase}/api/import-workspaces`);
    const result = await response.json();
    if (response.ok) setImportWorkspaces(result.workspaces ?? []);
  }

  async function createManual(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setMessage("Creating project...");
    const data = new FormData(event.currentTarget);
    const sourceMode = String(data.get("sourceMode"));
    const repo = String(data.get("repository") ?? "").trim();
    const payload: Record<string, unknown> = {
      name: String(data.get("name")),
      type: String(data.get("type")),
      sourceMode,
      environments: ["development"],
      productionProtected: true
    };
    if (repo) payload.repository = { fullName: repo, defaultBranch: "main" };
    const response = await fetch(`${apiBase}/api/projects`, {
      method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload)
    });
    const result = await response.json();
    if (!response.ok) return setMessage(result.error ?? "Project creation failed");
    setProjects((current) => [...current, result]);
    setMessage("Project added in Development");
    event.currentTarget.reset();
  }
  async function createPlan(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    setMessage("Analysing prompt...");
    const data = new FormData(event.currentTarget);
    const response = await fetch(`${apiBase}/api/project-plans`, {
      method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ prompt: String(data.get("prompt")) })
    });
    const result = await response.json();
    if (!response.ok) return setMessage(result.error ?? "Plan failed");
    setPlan(result);
    setPlanName(result.suggestedName ?? "");
    setPlanRepository("");
    setMessage("Plan ready for review");
  }

  async function approvePlan() {
    if (!plan) return;
    setMessage("Approving development plan...");
    const body: Record<string, unknown> = { name: planName.trim() || plan.suggestedName };
    if (planRepository.trim()) body.repository = { fullName: planRepository.trim(), defaultBranch: "main" };
    const response = await fetch(`${apiBase}/api/project-plans/${plan.id}/approve`, {
      method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body)
    });
    const result = await response.json();
    if (!response.ok) return setMessage(result.error ?? "Approval failed");
    setProjects((current) => [...current, result.project]);
    setMessage("Development project created safely");
    setPlan(null);
  }

  async function createMartialArtsImportPlan() {
    setMessage("Creating Martial Arts ERP pilot import plan...");
    const response = await fetch(`${apiBase}/api/pilots/martial-arts-erp/import-plan`, {
      method: "POST", headers: { "Content-Type": "application/json" }, body: "{}"
    });
    const result = await response.json();
    if (!response.ok) return setMessage(result.error ?? "Import plan failed");
    setActiveImportPlan(result);
    setImportPlans((current) => [result, ...current]);
    setActiveWorkspace(null);
    setDeployGate(null);
    setActiveExecution(null);
    setActiveAcquisition(null);
    setActiveBuild(null);
    setSourcePackage(null);
    setSourceRefDraft("");
    setMessage("Martial Arts ERP pilot plan ready; source reference pending");
  }

  async function approveImportPlan(planId: string) {
    setMessage("Approving import plan for Development...");
    const response = await fetch(`${apiBase}/api/import-plans/${planId}/approve`, { method: "POST", headers: { "Content-Type": "application/json" }, body: "{}" });
    const result = await response.json();
    if (!response.ok) return setMessage(result.error ?? "Import approval failed");
    setProjects((current) => [...current, result.project]);
    setActiveImportPlan(result.importPlan);
    if (result.workspace) {
      setActiveWorkspace(result.workspace);
      setActiveExecution(null);
      setActiveAcquisition(null);
      setActiveBuild(null);
      setSourcePackage(null);
      setSourceRefDraft(result.workspace.sourceReferenceStatus === "pending" ? "" : result.workspace.sourceRef);
      setImportWorkspaces((current) => [result.workspace, ...current]);
      await loadDeployGate(result.workspace.id);
    }
    await loadImportPlans();
    setMessage("Import approved; workspace created and deploy gate locked");
  }

  async function openWorkspace(workspaceId: string) {
    setMessage("Loading import workspace...");
    setDeployGate(null);
    setActiveExecution(null);
    setActiveAcquisition(null);
    setActiveBuild(null);
    setSourcePackage(null);
    const response = await fetch(`${apiBase}/api/import-workspaces/${workspaceId}`);
    const result = await response.json();
    if (!response.ok) return setMessage(result.error ?? "Workspace failed");
    setActiveWorkspace(result);
    setSourceRefDraft(result.sourceReferenceStatus === "pending" ? "" : result.sourceRef);
    await loadDeployGate(result.id);
    await loadLatestExecution(result.id);
    await loadLatestAcquisition(result.id);
    setMessage("Import workspace ready");
  }

  async function confirmSourceReference() {
    if (!activeWorkspace) return;
    setMessage("Confirming source reference...");
    const response = await fetch(`${apiBase}/api/import-workspaces/${activeWorkspace.id}/source-reference`, {
      method: "PATCH", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ sourceRef: sourceRefDraft.trim() })
    });
    const result = await response.json();
    if (!response.ok) return setMessage(result.error ?? "Source reference update failed");
    setActiveWorkspace(result);
    setImportWorkspaces((current) => current.map((item) => item.id === result.id ? result : item));
    await loadDeployGate(result.id);
    setMessage("Source reference confirmed; capture unlocked");
  }

  async function updateCapture(kind: "route" | "module" | "checklist", key: string, status = "captured") {
    if (!activeWorkspace) return;
    setMessage("Updating capture status...");
    const response = await fetch(`${apiBase}/api/import-workspaces/${activeWorkspace.id}/capture`, {
      method: "PATCH", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ kind, key, status })
    });
    const result = await response.json();
    if (!response.ok) return setMessage(result.error ?? "Capture update failed");
    setActiveWorkspace(result);
    setImportWorkspaces((current) => current.map((item) => item.id === result.id ? result : item));
    await loadDeployGate(result.id);
    setMessage("Capture status updated");
  }

  async function loadDeployGate(workspaceId: string) {
    const response = await fetch(`${apiBase}/api/import-workspaces/${workspaceId}/deploy-gate`);
    const result = await response.json();
    if (response.ok) setDeployGate(result);
  }

  async function loadLatestExecution(workspaceId: string) {
    const response = await fetch(`${apiBase}/api/import-workspaces/${workspaceId}/executions`);
    const result = await response.json();
    if (response.ok) setActiveExecution(result.executions?.[0] ?? null);
  }

  async function loadLatestAcquisition(workspaceId: string) {
    const response = await fetch(`${apiBase}/api/import-workspaces/${workspaceId}/source-acquisitions`);
    const result = await response.json();
    if (response.ok) {
      const latest = (result.acquisitions ?? []).find((item: SourceAcquisition) => item.status !== "discarded") ?? null;
      setActiveAcquisition(latest);
      if (latest) await loadLatestBuild(latest.id);
      else setActiveBuild(null);
    }
  }

  async function loadLatestBuild(acquisitionId: string) {
    const response = await fetch(`${apiBase}/api/source-acquisitions/${acquisitionId}/builds`);
    const result = await response.json();
    if (response.ok) setActiveBuild(result.builds?.[0] ?? null);
  }

  async function loadSandboxStatus() {
    const response = await fetch(`${apiBase}/api/source-builds/sandbox-status`);
    const result = await response.json();
    if (response.ok) setSandboxReady(Boolean(result.dockerSandboxReady));
  }

  async function startSourceBuild() {
    if (!activeAcquisition) return;
    setMessage("Running isolated actual-source build...");
    await loadSandboxStatus();
    const response = await fetch(`${apiBase}/api/source-acquisitions/${activeAcquisition.id}/builds`, {
      method: "POST", headers: { "Content-Type": "application/json" }, body: "{}"
    });
    const result = await response.json();
    setActiveBuild(result);
    if (!response.ok) return setMessage(result.error ?? "Source build failed");
    if (result.status === "preview_ready") setMessage("Actual source build complete; isolated local preview ready");
    else if (result.blockers?.includes("sandbox_unavailable")) setMessage("Build safely blocked: Docker sandbox is not running");
    else setMessage(`Source build finished with status: ${result.status}`);
  }

  async function resetSourceBuild() {
    if (!activeBuild) return;
    setMessage("Resetting source build and stopping preview...");
    const response = await fetch(`${apiBase}/api/source-builds/${activeBuild.id}/reset`, {
      method: "POST", headers: { "Content-Type": "application/json" }, body: "{}"
    });
    const result = await response.json();
    if (!response.ok) return setMessage(result.error ?? "Source build reset failed");
    setActiveBuild(result);
    setMessage("Source build reset; isolated build files and preview stopped");
  }

  async function uploadSourcePackage() {
    if (!activeWorkspace || !sourcePackage) return;
    setMessage("Validating and acquiring source ZIP...");
    const form = new FormData();
    form.append("sourceZip", sourcePackage);
    const response = await fetch(`${apiBase}/api/import-workspaces/${activeWorkspace.id}/source-acquisitions`, {
      method: "POST",
      body: form
    });
    const result = await response.json();
    const acquisition = result.acquisition ?? null;
    if (acquisition) { setActiveAcquisition(acquisition); setActiveBuild(null); }
    if (!response.ok) {
      const issues = acquisition?.issues?.join(", ");
      return setMessage(issues ? `Source package rejected: ${issues}` : (result.error ?? "Source acquisition failed"));
    }
    setSourcePackage(null);
    setMessage("Actual source package acquired into isolated control-plane inbox");
  }

  async function discardSourcePackage() {
    if (!activeAcquisition) return;
    setMessage("Discarding isolated source package...");
    const response = await fetch(`${apiBase}/api/source-acquisitions/${activeAcquisition.id}/discard`, {
      method: "POST", headers: { "Content-Type": "application/json" }, body: "{}"
    });
    const result = await response.json();
    if (!response.ok) return setMessage(result.error ?? "Source package discard failed");
    setActiveAcquisition(null);
    setActiveBuild(null);
    setSourcePackage(null);
    setMessage("Source package discarded; production remains untouched");
  }

  async function startDevelopmentPreview() {
    if (!activeWorkspace) return;
    setMessage("Running isolated Development import preview...");
    const response = await fetch(`${apiBase}/api/import-workspaces/${activeWorkspace.id}/executions`, {
      method: "POST", headers: { "Content-Type": "application/json" }, body: "{}"
    });
    const result = await response.json();
    if (!response.ok) return setMessage(result.error ?? "Development preview execution blocked");
    setActiveExecution(result);
    setMessage("Development preview ready; real deployment remains locked");
  }

  async function resetDevelopmentPreview() {
    if (!activeExecution) return;
    setMessage("Resetting Development preview...");
    const response = await fetch(`${apiBase}/api/import-executions/${activeExecution.id}/reset`, {
      method: "POST", headers: { "Content-Type": "application/json" }, body: "{}"
    });
    const result = await response.json();
    if (!response.ok) return setMessage(result.error ?? "Preview reset failed");
    setActiveExecution(result);
    setMessage("Development preview reset safely");
  }

  async function openCommandCentre(projectId: string) {
    setMessage("Loading Command Centre...");
    const response = await fetch(`${apiBase}/api/projects/${projectId}/command-centre`);
    const result = await response.json();
    if (!response.ok) return setMessage(result.error ?? "Command Centre failed");
    setCommandCentre(result);
    const development = result.environments?.find((environment: ProjectEnvironment) => environment.environmentName === "development");
    setEnvDraft({
      status: development?.status ?? "unconfigured",
      frontendProvider: development?.frontendProvider ?? "",
      backendProvider: development?.backendProvider ?? "",
      databaseProvider: development?.databaseProvider ?? "",
      region: development?.region ?? ""
    });
    setMessage("Command Centre ready");
  }

  async function saveDevelopmentEnvironment(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!commandCentre) return;
    setMessage("Saving Development configuration...");
    const payload = {
      status: envDraft.status,
      frontendProvider: envDraft.frontendProvider.trim() || null,
      backendProvider: envDraft.backendProvider.trim() || null,
      databaseProvider: envDraft.databaseProvider.trim() || null,
      region: envDraft.region.trim() || null
    };
    const response = await fetch(`${apiBase}/api/projects/${commandCentre.project.id}/environments/development`, {
      method: "PATCH", headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload)
    });
    const result = await response.json();
    if (!response.ok) return setMessage(result.error ?? "Environment update failed");
    setCommandCentre((current) => current ? {
      ...current,
      environments: current.environments.map((environment) =>
        environment.environmentName === "development" ? result : environment)
    } : current);
    setMessage("Development configuration saved");
  }

  function replaceWorkspace(updated: DevelopmentWorkspace) {
    setCommandCentre((current) => current ? {
      ...current,
      workspaces: current.workspaces.map((workspace) => workspace.id === updated.id ? updated : workspace)
    } : current);
  }

  async function createGitWorkspace(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!commandCentre) return;
    const data = new FormData(event.currentTarget);
    setMessage("Creating protected feature workspace...");
    const response = await fetch(`${apiBase}/api/projects/${commandCentre.project.id}/development-workspaces`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        requestSummary: String(data.get("requestSummary") ?? ""),
        baseBranch: String(data.get("baseBranch") ?? "").trim() || undefined
      })
    });
    const result = await response.json();
    if (!response.ok) return setMessage(result.error ?? "Workspace creation failed");
    setCommandCentre((current) => current ? { ...current, workspaces: [result, ...current.workspaces] } : current);
    setMessage("Protected feature workspace planned");
    event.currentTarget.reset();
  }

  async function prepareGitBranch(workspaceId: string) {
    setMessage("Preparing safe branch plan...");
    const response = await fetch(`${apiBase}/api/development-workspaces/${workspaceId}/prepare-branch`, {
      method: "POST", headers: { "Content-Type": "application/json" }, body: "{}"
    });
    const result = await response.json();
    if (!response.ok) return setMessage(result.error ?? "Branch plan failed");
    replaceWorkspace(result);
    setMessage("Branch plan ready — no real branch created");
  }

  async function prepareGitReview(workspaceId: string) {
    setMessage("Preparing review plan...");
    const response = await fetch(`${apiBase}/api/development-workspaces/${workspaceId}/prepare-review`, {
      method: "POST", headers: { "Content-Type": "application/json" }, body: "{}"
    });
    const result = await response.json();
    if (!response.ok) return setMessage(result.error ?? "Review plan failed");
    replaceWorkspace(result);
    setMessage("Review plan ready — provider execution remains gated");
  }

  async function cancelGitWorkspace(workspaceId: string) {
    const response = await fetch(`${apiBase}/api/development-workspaces/${workspaceId}/cancel`, {
      method: "POST", headers: { "Content-Type": "application/json" }, body: "{}"
    });
    const result = await response.json();
    if (!response.ok) return setMessage(result.error ?? "Workspace cancel failed");
    replaceWorkspace(result);
    setMessage("Development workspace cancelled");
  }

  async function createSecretReferenceRecord(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!commandCentre) return;
    const data = new FormData(event.currentTarget);
    setMessage("Registering secret reference...");
    const response = await fetch(`${apiBase}/api/projects/${commandCentre.project.id}/secret-references`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        environment: String(data.get("environment") ?? "development"),
        secretName: String(data.get("secretName") ?? ""),
        providerReference: String(data.get("providerReference") ?? "").trim() || undefined
      })
    });
    const result = await response.json();
    if (!response.ok) return setMessage(result.error ?? "Secret reference failed");
    setCommandCentre((current) => current ? { ...current, secretReferences: [result, ...current.secretReferences] } : current);
    setMessage("Secret reference registered — no secret value stored");
    event.currentTarget.reset();
  }

  function replaceDnsProposal(updated: DnsProposal) {
    setCommandCentre((current) => current ? {
      ...current,
      dnsProposals: current.dnsProposals.map((item) => item.id === updated.id ? updated : item)
    } : current);
  }

  async function createDnsProposalRecord(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!commandCentre) return;
    const data = new FormData(event.currentTarget);
    setMessage("Creating DNS change proposal...");
    const action = String(data.get("action") ?? "create");
    const response = await fetch(`${apiBase}/api/projects/${commandCentre.project.id}/dns-proposals`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        domain: String(data.get("domain") ?? ""),
        action,
        recordType: String(data.get("recordType") ?? "A"),
        recordName: String(data.get("recordName") ?? ""),
        proposedValue: action === "delete" ? null : String(data.get("proposedValue") ?? ""),
        ttl: Number(data.get("ttl") ?? 300)
      })
    });
    const result = await response.json();
    if (!response.ok) return setMessage(result.error ?? "DNS proposal failed");
    setCommandCentre((current) => current ? { ...current, dnsProposals: [result, ...current.dnsProposals] } : current);
    setMessage("DNS proposal created — execution remains locked");
    event.currentTarget.reset();
  }

  async function approveDnsChange(proposalId: string) {
    const response = await fetch(`${apiBase}/api/dns-proposals/${proposalId}/approve`, {
      method: "POST", headers: { "Content-Type": "application/json" }, body: "{}"
    });
    const result = await response.json();
    if (!response.ok) return setMessage(result.error ?? "DNS approval failed");
    replaceDnsProposal(result);
    setMessage("DNS proposal approved — execution still locked");
  }

  async function cancelDnsChange(proposalId: string) {
    const response = await fetch(`${apiBase}/api/dns-proposals/${proposalId}/cancel`, {
      method: "POST", headers: { "Content-Type": "application/json" }, body: "{}"
    });
    const result = await response.json();
    if (!response.ok) return setMessage(result.error ?? "DNS cancel failed");
    replaceDnsProposal(result);
    setMessage("DNS proposal cancelled");
  }

  async function confirmDnsExecutionLocked(proposalId: string) {
    const response = await fetch(`${apiBase}/api/dns-proposals/${proposalId}/execute`, {
      method: "POST", headers: { "Content-Type": "application/json" }, body: "{}"
    });
    const result = await response.json();
    setMessage(response.status === 409 ? "DNS execution is safely locked" : (result.error ?? "Unexpected DNS response"));
  }

  function replaceReleaseEvidence(updated: ReleaseEvidence) {
    setCommandCentre((current) => current ? {
      ...current,
      releaseEvidence: current.releaseEvidence.map((item) => item.id === updated.id ? updated : item)
    } : current);
  }

  async function createReleaseEvidenceRecord(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!commandCentre) return;
    const data = new FormData(event.currentTarget);
    const response = await fetch(`${apiBase}/api/projects/${commandCentre.project.id}/release-evidence`, {
      method: "POST", headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        environment: String(data.get("environment") ?? "development"),
        sourceRevision: String(data.get("sourceRevision") ?? ""),
        buildResult: String(data.get("buildResult") ?? "not_run"),
        testResult: String(data.get("testResult") ?? "not_run"),
        healthResult: String(data.get("healthResult") ?? "not_run"),
        deploymentIdentifier: String(data.get("deploymentIdentifier") ?? "").trim() || undefined,
        healthReference: String(data.get("healthReference") ?? "")
      })
    });
    const result = await response.json();
    if (!response.ok) return setMessage(result.error ?? "Release evidence failed");
    setCommandCentre((current) => current ? { ...current, releaseEvidence: [result, ...current.releaseEvidence] } : current);
    setMessage(result.complete ? "Release evidence complete - ready for verification" : "Release evidence saved with blockers");
    event.currentTarget.reset();
  }

  async function verifyReleaseEvidenceRecord(id: string) {
    const response = await fetch(`${apiBase}/api/release-evidence/${id}/verify`, { method: "POST", headers: { "Content-Type": "application/json" }, body: "{}" });
    const result = await response.json();
    if (!response.ok) return setMessage(result.error ?? "Release evidence verification failed");
    replaceReleaseEvidence(result);
    setMessage(`Release evidence ${result.status}`);
  }

  async function confirmReleaseDeployLocked(id: string) {
    const response = await fetch(`${apiBase}/api/release-evidence/${id}/deploy`, { method: "POST", headers: { "Content-Type": "application/json" }, body: "{}" });
    const result = await response.json();
    setMessage(response.status === 409 ? "Release execution is safely locked" : (result.error ?? "Unexpected release response"));
  }

  function replaceRollbackPlan(updated: RollbackPlan) {
    setCommandCentre((current) => current ? { ...current, rollbackPlans: current.rollbackPlans.map((item) => item.id === updated.id ? updated : item) } : current);
  }

  async function addVersionFromEvidence(releaseEvidenceId: string) {
    if (!commandCentre) return;
    const response = await fetch(`${apiBase}/api/projects/${commandCentre.project.id}/version-history`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ releaseEvidenceId }) });
    const result = await response.json();
    if (!response.ok) return setMessage(result.error ?? "Version ledger update failed");
    setCommandCentre((current) => current ? { ...current, versions: [result, ...current.versions] } : current);
    setMessage("Version history entry created");
  }

  async function createRollbackPlanRecord(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!commandCentre) return;
    const data = new FormData(event.currentTarget);
    const response = await fetch(`${apiBase}/api/projects/${commandCentre.project.id}/rollback-plans`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ environment: String(data.get("environment") ?? "development"), fromVersionId: String(data.get("fromVersionId") ?? ""), toVersionId: String(data.get("toVersionId") ?? "") }) });
    const result = await response.json();
    if (!response.ok) return setMessage(result.error ?? "Rollback plan failed");
    setCommandCentre((current) => current ? { ...current, rollbackPlans: [result, ...current.rollbackPlans] } : current);
    setMessage("Rollback plan created - execution locked");
  }

  async function approveRollbackPlanRecord(id: string) {
    const response = await fetch(`${apiBase}/api/rollback-plans/${id}/approve`, { method: "POST", headers: { "Content-Type": "application/json" }, body: "{}" });
    const result = await response.json(); if (!response.ok) return setMessage(result.error ?? "Rollback approval failed"); replaceRollbackPlan(result); setMessage("Rollback plan approved - execution still locked");
  }

  async function confirmRollbackExecuteLocked(id: string) {
    const response = await fetch(`${apiBase}/api/rollback-plans/${id}/execute`, { method: "POST", headers: { "Content-Type": "application/json" }, body: "{}" });
    const result = await response.json(); setMessage(response.status === 409 ? "Rollback execution is safely locked" : (result.error ?? "Unexpected rollback response"));
  }

  if (!authStatus) {
    return <main className="authShell"><section className="authCard">
      <span className="eyebrow">oRRbit AI Control Plane</span>
      <h1>Owner Access</h1>
      <p>{authError || "Checking secure owner access..."}</p>
      {authError && <button onClick={loadAuthStatus}>Retry</button>}
    </section></main>;
  }

  if (!authStatus.authenticated) {
    const firstSetup = !authStatus.configured;
    return <main className="authShell"><section className="authCard">
      <span className="eyebrow">oRRbit AI Control Plane</span>
      <h1>{firstSetup ? "Create Owner Access" : "Owner Sign In"}</h1>
      <p>{firstSetup
        ? "One-time setup. Create the owner login that protects this Control Plane."
        : "Sign in with the owner account to access projects, controls and audit history."}</p>
      <form className="authForm" onSubmit={submitOwnerAccess}>
        <label>Email<input name="email" type="email" autoComplete="username" required placeholder="owner@company.com" /></label>
        <label>Password<input name="password" type="password" autoComplete={firstSetup ? "new-password" : "current-password"}
          minLength={12} required placeholder="Minimum 12 characters" /></label>
        {firstSetup && authStatus.setupProtection === "token_required" && <label>One-time setup token
          <input name="setupToken" type="password" autoComplete="off" required placeholder="Token configured on the server" /></label>}
        {firstSetup && authStatus.setupProtection === "blocked_remote" &&
          <div className="warningBox">Remote first-time setup is disabled. Configure CONTROL_OWNER_SETUP_TOKEN on the server or complete first setup locally.</div>}
        <button type="submit" disabled={firstSetup && authStatus.setupProtection === "blocked_remote"}>
          {firstSetup ? "Create Secure Owner Access" : "Sign In"}</button>
      </form>
      {authError && <div className="warningBox authError">{authError}</div>}
      <div className="protectionStrip">HttpOnly session · SameSite Strict · Server-side session hash · Login rate limit</div>
    </section></main>;
  }

  const realImportLocked = !panelReadiness?.realImportUnlocked;

  return <main className="shell">
    <header className="topbar">
      <div><span className="eyebrow">oRRbit</span><h1>AI Control Plane</h1></div>
      <div className="headerActions"><span className="status">{message}</span>
        <span className="ownerIdentity">{authStatus.owner?.email}</span>
        <button onClick={() => setAddOpen((v) => !v)}>+ Add Project</button>
        <button className="ghost" onClick={logoutOwner}>Sign out</button></div>
    </header>

    <section className="hero">
      <div><h2>Websites and SaaS, one controlled workflow.</h2>
        <p>Plan with AI, build in isolation, verify, approve, deploy and roll back.</p></div>
      <div className="metric"><strong>{projects.length}</strong><span>Registered projects</span></div>
    </section>

    {panelReadiness && <section className="readinessPanel">
      <div className="readinessHead">
        <div><span className="eyebrow">Panel Completion Gate</span>
          <h2>{panelReadiness.panelComplete ? "Panel complete" : "Panel completion in progress"}</h2>
          <p>{panelReadiness.standingRule}</p></div>
        <div className="readinessScore"><strong>{panelReadiness.completion.buildProgressPercent}%</strong><span>build progress</span></div>
      </div>
      <div className="readinessStats">
        <div><strong>{panelReadiness.completion.ready}</strong><span>Ready</span></div>
        <div><strong>{panelReadiness.completion.partial}</strong><span>Partial</span></div>
        <div><strong>{panelReadiness.completion.pending}</strong><span>Pending</span></div>
        <div><strong>{panelReadiness.completion.total}</strong><span>Total modules</span></div>
      </div>
      <div className={panelReadiness.realImportUnlocked ? "readyBox" : "warningBox"}>
        <strong>{panelReadiness.realImportUnlocked ? "Real import unlocked" : "Real import/transfer locked"}</strong>
        {!panelReadiness.realImportUnlocked && " — only explicitly marked synthetic QA fixtures are permitted until every required panel module is ready."}
      </div>
      <div className="readinessGrid">
        {panelReadiness.modules.map((item) => <article className={"readinessItem readiness-" + item.status} key={item.key}>
          <div><strong>{item.name}</strong><span>{item.status}</span></div><p>{item.summary}</p>
        </article>)}
      </div>
    </section>}

    {addOpen && <section className="builder">
      <div className="builderHead"><div><span className="eyebrow">Project onboarding</span>
        <h2>Add manually or describe it to AI</h2></div>
        <button className="ghost" onClick={() => setAddOpen(false)}>Close</button></div>
      <div className="tabs">
        <button className={mode === "manual" ? "active" : ""} onClick={() => setMode("manual")}>Manual</button>
        <button className={mode === "ai" ? "active" : ""} onClick={() => setMode("ai")}>AI Prompt</button>
      </div>
      {mode === "manual" ? <form className="form" onSubmit={createManual}>
        <label>Project name<input name="name" required minLength={2} placeholder="e.g. Martial Arts ERP" /></label>
        <label>Project type<select name="type" defaultValue="saas">
          <option value="static-website">Static Website</option><option value="dynamic-website">Dynamic Website</option>
          <option value="saas">SaaS</option><option value="erp-crm">ERP / CRM</option>
          <option value="api">API</option><option value="pwa">PWA</option>
        </select></label>
        <label>Source<select name="sourceMode" defaultValue="new-project">
          <option value="new-project">New project</option><option value="existing-repository">Existing GitHub repository</option>
          <option value="import" disabled={realImportLocked}>Import existing project {realImportLocked ? "(locked until panel complete)" : ""}</option>
        </select></label>
        <label>Repository (optional)<input name="repository" placeholder="owner/repository" /></label>
        <button type="submit">Add to Development</button>
      </form> : <form className="form aiForm" onSubmit={createPlan}>
        <label>Describe the project<textarea name="prompt" required minLength={10}
          placeholder="Create a protected development project and prepare a safe implementation plan. Keep production disabled." /></label>
        <button type="submit">Analyse & Create Plan</button>
      </form>}
      {plan && <div className="plan">
        <div className="planTop"><div><span className="eyebrow">Plan preview</span>
          <h3>{plan.inferredProjectType} · {plan.targetEnvironment}</h3></div>
          <span className={`risk risk-${plan.risk}`}>{plan.risk} risk</span></div>
        <dl><div><dt>Source</dt><dd>{plan.sourceMode}</dd></div>
          <div><dt>Planner</dt><dd>{plan.planner}</dd></div>
          <div><dt>Approval</dt><dd>{plan.requiresApproval ? "Required" : "Not required"}</dd></div></dl>
        <ol>{plan.actions.map((action) => <li key={action}>{action}</li>)}</ol>
        <div className="planControls">
          <label>Project name<input value={planName} onChange={(e) => setPlanName(e.target.value)} /></label>
          {plan.sourceMode === "existing-repository" && <label>Repository<input value={planRepository}
            onChange={(e) => setPlanRepository(e.target.value)} placeholder="owner/repository" /></label>}
        </div>
        <button onClick={approvePlan} disabled={plan.targetEnvironment !== "development"}
          title={plan.targetEnvironment !== "development" ? "Only Development apply is enabled in V1" : "Create protected Development project"}>
          {plan.targetEnvironment === "development" ? "Approve & Create Development Project" : "Production/Staging Apply Locked"}
        </button>
      </div>}
    </section>}

    <section className={realImportLocked ? "importPanel importLocked" : "importPanel"}>
      <div className="builderHead">
        <div><span className="eyebrow">Existing Project Import Engine</span>
          <h2>{realImportLocked ? "Locked until panel completion" : "Import & Transfer"}</h2>
          <p>{realImportLocked ? "This engine is being developed and QA-tested only. No real SaaS or website import/transfer is permitted until the full panel reaches 100%." : "Controlled import workflow through the panel only."}</p></div>
        <button onClick={createMartialArtsImportPlan} disabled={realImportLocked}>{realImportLocked ? "Real Import Locked" : "Create Import Plan"}</button>
      </div>
      <div className="importLayout">
        <div className="importList">
          <span className="eyebrow">Import plans</span>
          {importPlans.length === 0 ? <p className="muted">No import plan yet.</p> : importPlans.map((item) => <button
            className={activeImportPlan?.id === item.id ? "importItem active" : "importItem"}
            key={item.id} onClick={() => setActiveImportPlan(item)}>
            <strong>{item.requestedProjectName}</strong>
            <span>{item.sourceType} · {item.status} · {item.routeInventory.length} routes</span>
          </button>)}
          <div className="workspaceMiniList"><span className="eyebrow">Workspaces</span>
            {importWorkspaces.length === 0 ? <p className="muted">No workspace yet.</p> : importWorkspaces.map((workspace) => <button
              className={activeWorkspace?.id === workspace.id ? "importItem active" : "importItem"}
              key={workspace.id} onClick={() => openWorkspace(workspace.id)}>
              <strong>{workspace.status}</strong>
              <span>{workspace.sourceReferenceStatus} · routes {workspace.routeCapture.length} · modules {workspace.moduleCapture.length}</span>
            </button>)}
          </div>
        </div>
        {activeImportPlan ? <div className="importDetail">
          <div className="planTop"><div><span className="eyebrow">Selected import plan</span>
            <h3>{activeImportPlan.requestedProjectName}</h3></div>
            <span className={"risk risk-" + activeImportPlan.risk}>{activeImportPlan.risk} risk</span></div>
          <dl>
            <div><dt>Source</dt><dd>{activeImportPlan.sourceRef}</dd></div>
            <div><dt>Target</dt><dd>{activeImportPlan.targetEnvironment}</dd></div>
            <div><dt>Manifest</dt><dd>{activeImportPlan.manifestDraft.projectType} · protected={String(activeImportPlan.manifestDraft.productionProtected)}</dd></div>
          </dl>
          <div className="routeBox"><span className="eyebrow">Route inventory</span>
            {activeImportPlan.routeInventory.map((route) => <p key={route.path}>{route.path} <span>{route.kind} · {route.status}</span></p>)}</div>
          <div className="blockedBox"><span className="eyebrow">Blocked in V1</span>
            {activeImportPlan.blockedActions.map((action) => <p key={action}>{action}</p>)}</div>
          <button onClick={() => approveImportPlan(activeImportPlan.id)} disabled={activeImportPlan.status === "approved"}>
            {activeImportPlan.status === "approved" ? "Approved as Development Project" : "Approve Import to Development"}
          </button>
          {activeWorkspace && <div className="workspacePanel">
            <div className="planTop"><div><span className="eyebrow">Import Workspace</span>
              <h3>{activeWorkspace.status}</h3></div>
              <span className={activeWorkspace.sourceReferenceStatus === "pending" ? "lockBadge" : "okBadge"}>
                Source {activeWorkspace.sourceReferenceStatus}</span></div>
            {activeWorkspace.sourceReferenceStatus === "pending" && <div className="warningBox">
              Source reference pending. Deploy, DNS, live payment and production actions are disabled until capture is verified.
              <div className="sourceConfirmRow">
                <input value={sourceRefDraft} onChange={(e) => setSourceRefDraft(e.target.value)}
                  placeholder="Paste ChatGPT Sites source reference" />
                <button onClick={confirmSourceReference} disabled={!sourceRefDraft.trim()}>Confirm Source</button>
              </div>
            </div>}
            {activeWorkspace.sourceReferenceStatus === "provided" && <div className="sourceConfirmedBox">
              <span className="eyebrow">Confirmed source</span>
              <strong>{activeWorkspace.sourceRef}</strong>
            </div>}
            <div className="sourceAcquisitionPanel">
              <div className="planTop"><div><span className="eyebrow">Actual Source Acquisition</span>
                <h3>ChatGPT Sites export ZIP</h3></div>
                <span className={activeAcquisition?.status === "acquired" ? "okBadge" : "lockBadge"}>
                  {activeAcquisition?.status ?? "awaiting package"}</span></div>
              {!activeAcquisition && <div className="sourceUploadRow">
                <input type="file" accept=".zip,application/zip" onChange={(e) => setSourcePackage(e.target.files?.[0] ?? null)} />
                <button onClick={uploadSourcePackage} disabled={activeWorkspace.sourceReferenceStatus !== "provided" || !sourcePackage}>Upload & Validate Source ZIP</button>
              </div>}
              {sourcePackage && !activeAcquisition && <p className="muted">Selected: {sourcePackage.name} · {(sourcePackage.size / 1024 / 1024).toFixed(2)} MB</p>}
              {activeAcquisition && <div className="sourceInventory">
                <div><span>Archive</span><strong>{activeAcquisition.archiveName}</strong></div>
                <div><span>Files</span><strong>{activeAcquisition.inventory.fileCount}</strong></div>
                <div><span>Code files</span><strong>{activeAcquisition.inventory.codeFiles}</strong></div>
                <div><span>Assets</span><strong>{activeAcquisition.inventory.assetFiles}</strong></div>
                <div><span>package.json</span><strong>{activeAcquisition.inventory.hasPackageJson ? (activeAcquisition.inventory.packageJsonPath ?? "Yes") : "No"}</strong></div>
              </div>}
              {activeAcquisition && <div className="hashBox"><span className="eyebrow">SHA-256 evidence</span><strong>{activeAcquisition.sha256}</strong></div>}
              {activeAcquisition?.issues.length ? <div className="warningBox">{activeAcquisition.issues.join(" · ")}</div> : null}
              {activeAcquisition?.status === "acquired" && <div className="readyBox sourceReady">Actual source acquired and extracted only into the isolated control-plane inbox. No source code has been executed.</div>}
              {activeAcquisition && <button className="secondary sourceDiscard" onClick={discardSourcePackage}>Discard Isolated Source Package</button>}
            </div>
            {activeAcquisition?.status === "acquired" && <div className="sourceBuildPanel">
              <div className="planTop"><div><span className="eyebrow">Isolated Actual Source Build</span>
                <h3>{activeBuild?.status ?? "Ready to build"}</h3></div>
                <span className={sandboxReady ? "okBadge" : "lockBadge"}>{sandboxReady === null ? "Sandbox unchecked" : sandboxReady ? "Docker sandbox ready" : "Docker sandbox unavailable"}</span></div>
              <div className="buildActions">
                <button className="secondary" onClick={loadSandboxStatus}>Check Sandbox</button>
                <button onClick={startSourceBuild} disabled={activeBuild?.status === "preview_ready"}>Build Actual Source</button>
                <button disabled title="Production deployment remains locked">Deploy Locked</button>
              </div>
              <div className="protectionStrip">Docker sandbox required · Host execution disabled · Build network disabled · Production/DNS/payment/live DB/customer data locked</div>
              {activeBuild && <>
                <div className="buildSummary">
                  <div><span>Framework</span><strong>{activeBuild.framework}</strong></div>
                  <div><span>Package manager</span><strong>{activeBuild.packageManager}</strong></div>
                  <div><span>Project root</span><strong>{activeBuild.projectSubdir}</strong></div>
                  <div><span>Artifact</span><strong>{activeBuild.artifactDirectory ?? "Not ready"}</strong></div>
                </div>
                <div className="executionStages">
                  {activeBuild.stages.map((stage) => <div key={stage.name}><strong>{stage.name}</strong><span>{stage.status}</span>{stage.detail && <small>{stage.detail}</small>}</div>)}
                </div>
                {activeBuild.blockers.length > 0 && <div className="warningBox"><strong>Build blockers:</strong> {activeBuild.blockers.join(" · ")}</div>}
                <div className="buildCommands"><span>Install</span><code>{activeBuild.installCommand || "pending"}</code><span>Build</span><code>{activeBuild.buildCommand || "pending"}</code></div>
                {activeBuild.preview && <div className="actualPreviewBox"><span className="eyebrow">Actual local preview</span><a href={activeBuild.preview.url} target="_blank" rel="noreferrer">Open {activeBuild.preview.url}</a></div>}
                {activeBuild.logs.length > 0 && <details className="buildLogs"><summary>Build logs</summary><pre>{activeBuild.logs.join("\n\n")}</pre></details>}
                <div className="buildActions"><button className="secondary" onClick={resetSourceBuild} disabled={activeBuild.status === "reset"}>Reset Build / Stop Preview</button><button disabled>External Deploy Locked</button></div>
              </>}
            </div>}
            <div className="captureGrid">
              <div><span className="eyebrow">Route/Page capture</span>
                <strong>{activeWorkspace.routeCapture.filter((x) => x.status === "captured").length}/{activeWorkspace.routeCapture.length}</strong></div>
              <div><span className="eyebrow">Module capture</span>
                <strong>{activeWorkspace.moduleCapture.filter((x) => x.status === "captured").length}/{activeWorkspace.moduleCapture.length}</strong></div>
              <div><span className="eyebrow">Deploy gate</span>
                <strong>{(deployGate ?? activeWorkspace.deployGate).canDeploy ? "Ready" : "Blocked"}</strong></div>
            </div>
            <div className="captureControlBox"><span className="eyebrow">Route capture controls</span>
              {activeWorkspace.routeCapture.map((item) => <p key={item.path}>{item.path}<span>{item.status}</span>
                <button onClick={() => updateCapture("route", item.path)} disabled={activeWorkspace.sourceReferenceStatus === "pending" || item.status === "captured"}>Mark captured</button></p>)}</div>
            <div className="captureControlBox"><span className="eyebrow">Module capture controls</span>
              {activeWorkspace.moduleCapture.map((item) => <p key={item.name}>{item.name}<span>{item.status}</span>
                <button onClick={() => updateCapture("module", item.name)} disabled={activeWorkspace.sourceReferenceStatus === "pending" || item.status === "captured"}>Mark captured</button></p>)}</div>
            <div className="checklistBox"><span className="eyebrow">Capture checklist</span>
              {activeWorkspace.captureChecklist.map((item) => <p key={item.key}>{item.label}<span>{item.status}</span>
                {item.key === "manifest-review" && <button onClick={() => updateCapture("checklist", item.key)} disabled={item.status === "captured"}>Mark reviewed</button>}</p>)}</div>
            <div className="blockedBox"><span className="eyebrow">Deploy blockers</span>
              {(deployGate ?? activeWorkspace.deployGate).blockers.length > 0
                ? (deployGate ?? activeWorkspace.deployGate).blockers.map((blocker) => <p key={blocker}>{blocker}</p>)
                : <p className="readyBox">Capture verified. Development deploy gate is ready; real deployment execution remains locked in this phase.</p>}</div>
            <div className="gateActions">
              <button className="secondary" onClick={() => loadDeployGate(activeWorkspace.id)}>Recompute Deploy Gate</button>
              <button onClick={startDevelopmentPreview}
                disabled={!(deployGate ?? activeWorkspace.deployGate).canDeploy || (!!activeExecution && activeExecution.status !== "reset")}>
                {(deployGate ?? activeWorkspace.deployGate).canDeploy ? "Start Development Preview" : "Preview Blocked — Complete Capture"}
              </button>
              <button disabled title="Real deployment execution is intentionally locked">Real Deploy Locked</button>
            </div>
            {activeExecution && <div className="executionPanel">
              <div className="planTop"><div><span className="eyebrow">Development Import Execution</span>
                <h3>{activeExecution.status}</h3></div>
                <span className={activeExecution.status === "preview_ready" ? "okBadge" : "lockBadge"}>{activeExecution.mode}</span></div>
              <div className="executionStages">
                {activeExecution.stages.map((stage) => <div key={stage.name}>
                  <strong>{stage.name.replaceAll("_", " ")}</strong><span>{stage.status}</span>
                </div>)}
              </div>
              <div className="parityGrid">
                <div><span>Route parity</span><strong>{activeExecution.parity.routes.imported}/{activeExecution.parity.routes.expected}</strong></div>
                <div><span>Module parity</span><strong>{activeExecution.parity.modules.imported}/{activeExecution.parity.modules.expected}</strong></div>
                <div><span>Preview</span><strong>{activeExecution.preview ? "Ready" : "Not ready"}</strong></div>
              </div>
              {activeExecution.preview && <div className="previewRef">
                <span className="eyebrow">Safe preview reference</span><strong>{activeExecution.preview.reference}</strong>
              </div>}
              <div className="protectionStrip">
                Production locked · DNS locked · Live payment locked · Live DB locked · Customer data locked
              </div>
              <div className="gateActions">
                <button className="secondary" onClick={resetDevelopmentPreview} disabled={activeExecution.status === "reset"}>Reset Development Preview</button>
                <button disabled title="External deployment is intentionally unavailable">External Deploy Locked</button>
              </div>
            </div>}
          </div>}
        </div> : <div className="importDetail empty">Create or select an import plan to review inventory and blocked actions.</div>}
      </div>
    </section>

    {commandCentre && <section className="commandCentre">
      <div className="builderHead">
        <div><span className="eyebrow">Project Command Centre</span>
          <h2>{commandCentre.project.name}</h2>
          <p>{commandCentre.project.type} · {commandCentre.project.lifecycleStatus}</p></div>
        <button className="ghost" onClick={() => setCommandCentre(null)}>Close</button>
      </div>
      <div className="commandSummary">
        <div><span>Source</span><strong>{commandCentre.project.repository?.fullName ?? commandCentre.project.sourceMode ?? "Not linked"}</strong></div>
        <div><span>Production</span><strong>{commandCentre.protection.productionProtected ? "Protected" : "Unprotected"}</strong></div>
        <div><span>Cloud execution</span><strong>{commandCentre.protection.realCloudProvisioningEnabled ? "Enabled" : "Locked in V1"}</strong></div>
      </div>
      <div className="environmentGrid">
        {commandCentre.environments.map((environment) => <article className="environmentCard" key={environment.id}>
          <div className="cardHead"><h3>{environment.environmentName}</h3><span>{environment.status}</span></div>
          <dl>
            <div><dt>Frontend</dt><dd>{environment.frontendProvider ?? "Not configured"}</dd></div>
            <div><dt>Backend</dt><dd>{environment.backendProvider ?? "Not configured"}</dd></div>
            <div><dt>Database</dt><dd>{environment.databaseProvider ?? "Not configured"}</dd></div>
            <div><dt>Region</dt><dd>{environment.region ?? "Not configured"}</dd></div>
          </dl>
        </article>)}
      </div>
      <form className="form environmentForm" onSubmit={saveDevelopmentEnvironment}>
        <label>Status<select value={envDraft.status} onChange={(e) => setEnvDraft({ ...envDraft, status: e.target.value })}>
          <option value="unconfigured">Unconfigured</option><option value="planned">Planned</option>
          <option value="ready">Ready</option><option value="degraded">Degraded</option>
        </select></label>
        <label>Frontend provider<input value={envDraft.frontendProvider}
          onChange={(e) => setEnvDraft({ ...envDraft, frontendProvider: e.target.value })} placeholder="e.g. cloudflare-pages" /></label>
        <label>Backend provider<input value={envDraft.backendProvider}
          onChange={(e) => setEnvDraft({ ...envDraft, backendProvider: e.target.value })} placeholder="e.g. google-cloud-run" /></label>
        <label>Database provider<input value={envDraft.databaseProvider}
          onChange={(e) => setEnvDraft({ ...envDraft, databaseProvider: e.target.value })} placeholder="e.g. postgresql" /></label>
        <label>Region<input value={envDraft.region}
          onChange={(e) => setEnvDraft({ ...envDraft, region: e.target.value })} placeholder="e.g. asia-south1" /></label>
        <button type="submit">Save Development Config</button>
      </form>
      <div className="gitWorkspacePanel">
        <div className="builderHead"><div><span className="eyebrow">Git Branch & Workspace Management</span>
          <h3>Protected feature workspaces</h3>
          <p>Plan branch and review workflows without direct writes to main or production branches.</p></div>
          <span className="lockBadge">Provider execution gated</span></div>
        {!commandCentre.project.repository ? <div className="warningBox">
          Link a repository to this project before creating a Git development workspace.
        </div> : <>
          <form className="gitWorkspaceForm" onSubmit={createGitWorkspace}>
            <label>Change request<input name="requestSummary" required minLength={5} maxLength={240}
              placeholder="e.g. Add customer export report" /></label>
            <label>Base branch<input name="baseBranch" placeholder={commandCentre.project.repository.defaultBranch || "main"} /></label>
            <button type="submit">Plan Feature Workspace</button>
          </form>
          <div className="gitWorkspaceList">
            {commandCentre.workspaces.length === 0 ? <p className="muted">No feature workspace planned yet.</p> :
              commandCentre.workspaces.map((workspace) => <article className="gitWorkspaceCard" key={workspace.id}>
                <div className="cardHead"><h3>{workspace.requestSummary}</h3><span>{workspace.status.replaceAll("_", " ")}</span></div>
                <dl>
                  <div><dt>Repository</dt><dd>{workspace.repositoryFullName}</dd></div>
                  <div><dt>Base</dt><dd>{workspace.baseBranch}</dd></div>
                  <div><dt>Feature branch</dt><dd><code>{workspace.branchName}</code></dd></div>
                  <div><dt>Real branch created</dt><dd>{workspace.actualBranchCreated ? "Yes" : "No"}</dd></div>
                </dl>
                <div className="protectionStrip">Development only · direct main write disabled · provider execution gated</div>
                {workspace.branchPlan && <div className="gitPlanEvidence"><strong>Branch plan</strong>
                  <span>{workspace.branchPlan.provider} · {workspace.branchPlan.mode} · execution allowed={String(workspace.branchPlan.executionAllowed)}</span></div>}
                {workspace.reviewPlan && <div className="gitPlanEvidence"><strong>Review plan</strong>
                  <span>{workspace.reviewPlan.provider} · {workspace.reviewPlan.mode} · execution allowed={String(workspace.reviewPlan.executionAllowed)}</span></div>}
                <div className="gateActions">
                  <button onClick={() => prepareGitBranch(workspace.id)}
                    disabled={workspace.status !== "planned"}>Prepare Branch Plan</button>
                  <button onClick={() => prepareGitReview(workspace.id)}
                    disabled={workspace.status !== "branch_plan_ready"}>Prepare Review Plan</button>
                  <button className="secondary" onClick={() => cancelGitWorkspace(workspace.id)}
                    disabled={workspace.status === "cancelled"}>Cancel</button>
                </div>
              </article>)}
          </div>
        </>}
      </div>
      <div className="secretReferencePanel">
        <div className="builderHead"><div><span className="eyebrow">Secret References</span>
          <h3>Reference secrets without storing values</h3>
          <p>Only secret names and provider references are stored. Secret values never enter the Control Plane.</p></div>
          <span className="okBadge">Values blocked</span></div>
        <form className="secretReferenceForm" onSubmit={createSecretReferenceRecord}>
          <label>Environment<select name="environment" defaultValue="development">
            <option value="development">Development</option>
            <option value="staging">Staging</option>
            <option value="production">Production</option>
          </select></label>
          <label>Secret name<input name="secretName" required minLength={2} maxLength={128}
            placeholder="e.g. DATABASE_URL" /></label>
          <label>Provider reference (optional)<input name="providerReference"
            placeholder="Leave blank to create a safe reference ID" /></label>
          <button type="submit">Add Secret Reference</button>
        </form>
        <div className="secretReferenceList">
          {commandCentre.secretReferences.length === 0 ? <p className="muted">No secret references configured.</p> :
            commandCentre.secretReferences.map((item) => <article className="secretReferenceCard" key={item.id}>
              <div className="cardHead"><h3>{item.secretName}</h3><span>{item.environment}</span></div>
              <dl>
                <div><dt>Provider</dt><dd>{item.provider}</dd></div>
                <div><dt>Reference</dt><dd><code>{item.providerReference}</code></dd></div>
                <div><dt>Provider mode</dt><dd>{item.providerPlan.mode}</dd></div>
                <div><dt>Secret value stored</dt><dd>{item.secretValueStored ? "Unexpected" : "No"}</dd></div>
              </dl>
              <div className="protectionStrip">Reference metadata only · no plaintext value · no AI secret exposure</div>
            </article>)}
        </div>
      </div>
      <div className="dnsProposalPanel">
        <div className="builderHead"><div><span className="eyebrow">Domains & DNS Change Proposals</span>
          <h3>Plan high-risk DNS changes safely</h3>
          <p>Create, approve or cancel typed DNS proposals. Real DNS execution remains hard-locked in this phase.</p></div>
          <span className="lockBadge">Execution locked</span></div>
        <form className="dnsProposalForm" onSubmit={createDnsProposalRecord}>
          <label>Domain<input name="domain" required placeholder="example.com" /></label>
          <label>Action<select name="action" defaultValue="create">
            <option value="create">Create</option><option value="update">Update</option><option value="delete">Delete</option>
          </select></label>
          <label>Type<select name="recordType" defaultValue="A">
            <option>A</option><option>AAAA</option><option>CNAME</option><option>TXT</option><option>MX</option><option>CAA</option>
          </select></label>
          <label>Record name<input name="recordName" required placeholder="@ or www" /></label>
          <label>Value<input name="proposedValue" placeholder="Required unless deleting" /></label>
          <label>TTL<input name="ttl" type="number" min={60} max={86400} defaultValue={300} required /></label>
          <button type="submit">Create DNS Proposal</button>
        </form>
        <div className="dnsProposalList">
          {commandCentre.dnsProposals.length === 0 ? <p className="muted">No DNS proposals yet.</p> :
            commandCentre.dnsProposals.map((item) => <article className="dnsProposalCard" key={item.id}>
              <div className="cardHead"><h3>{item.recordType} {item.recordName}</h3><span>{item.status}</span></div>
              <dl>
                <div><dt>Domain</dt><dd>{item.domain}</dd></div>
                <div><dt>Action</dt><dd>{item.action}</dd></div>
                <div><dt>Value</dt><dd>{item.proposedValue ?? "Delete record"}</dd></div>
                <div><dt>TTL</dt><dd>{item.ttl}s</dd></div>
                <div><dt>Risk</dt><dd>{item.risk}</dd></div>
                <div><dt>Restore point</dt><dd>{item.restorePointRequired ? "Required" : "Not required"}</dd></div>
              </dl>
              <div className="protectionStrip">Approval required · restore point required · DNS execution locked · live traffic protected</div>
              <div className="gateActions">
                <button onClick={() => approveDnsChange(item.id)} disabled={item.status !== "proposed"}>Approve Proposal</button>
                <button className="secondary" onClick={() => cancelDnsChange(item.id)} disabled={item.status === "cancelled"}>Cancel</button>
                <button onClick={() => confirmDnsExecutionLocked(item.id)}>Verify Execute Lock</button>
              </div>
            </article>)}
        </div>
      </div>
      <div className="releaseEvidencePanel">
        <div className="builderHead"><div><span className="eyebrow">Health Verification & Release Evidence</span>
          <h3>Prove readiness before release</h3>
          <p>Source revision, build, tests and health evidence must all pass before verification.</p></div>
          <span className="lockBadge">Deploy locked</span></div>
        <form className="releaseEvidenceForm" onSubmit={createReleaseEvidenceRecord}>
          <label>Environment<select name="environment" defaultValue="development">
            <option value="development">Development</option><option value="staging">Staging</option><option value="production">Production</option>
          </select></label>
          <label>Source revision<input name="sourceRevision" required placeholder="commit/revision" /></label>
          <label>Build<select name="buildResult" defaultValue="passed"><option value="passed">Passed</option><option value="failed">Failed</option><option value="not_run">Not run</option></select></label>
          <label>Tests<select name="testResult" defaultValue="passed"><option value="passed">Passed</option><option value="failed">Failed</option><option value="not_run">Not run</option></select></label>
          <label>Health<select name="healthResult" defaultValue="passed"><option value="passed">Passed</option><option value="failed">Failed</option><option value="not_run">Not run</option></select></label>
          <label>Health reference<input name="healthReference" required placeholder="health check/evidence reference" /></label>
          <label>Deployment ID (optional)<input name="deploymentIdentifier" placeholder="preview/deployment reference" /></label>
          <button type="submit">Create Evidence Bundle</button>
        </form>
        <div className="releaseEvidenceList">
          {commandCentre.releaseEvidence.length === 0 ? <p className="muted">No release evidence bundles yet.</p> :
            commandCentre.releaseEvidence.map((item) => <article className="releaseEvidenceCard" key={item.id}>
              <div className="cardHead"><h3>{item.sourceRevision}</h3><span>{item.status}</span></div>
              <dl>
                <div><dt>Environment</dt><dd>{item.environment}</dd></div>
                <div><dt>Build</dt><dd>{item.buildResult}</dd></div>
                <div><dt>Tests</dt><dd>{item.testResult}</dd></div>
                <div><dt>Health</dt><dd>{item.healthResult}</dd></div>
                <div><dt>Complete</dt><dd>{item.complete ? "Yes" : "No"}</dd></div>
                <div><dt>Blockers</dt><dd>{item.blockers.length ? item.blockers.join(", ") : "None"}</dd></div>
              </dl>
              <div className="protectionStrip">Evidence only · no deployment execution · production release locked</div>
              <div className="gateActions">
                <button onClick={() => verifyReleaseEvidenceRecord(item.id)} disabled={item.status !== "draft"}>Verify Evidence</button>
                <button onClick={() => addVersionFromEvidence(item.id)} disabled={item.status !== "verified"}>Add to Version History</button>
                <button onClick={() => confirmReleaseDeployLocked(item.id)}>Verify Deploy Lock</button>
              </div>
            </article>)}
        </div>
      </div>
      <div className="rollbackPanel">
        <div className="builderHead"><div><span className="eyebrow">Rollback & Version History</span>
          <h3>Verified versions and rollback plans</h3>
          <p>Only verified release evidence can enter version history. Rollback execution stays locked.</p></div>
          <span className="lockBadge">Rollback execution locked</span></div>
        <div className="versionLedgerList">
          {commandCentre.versions.length === 0 ? <p className="muted">No verified versions recorded yet.</p> :
            commandCentre.versions.map((item) => <article className="versionLedgerCard" key={item.id}>
              <div className="cardHead"><h3>{item.sourceRevision}</h3><span>{item.environment}</span></div>
              <p>Health verified: {item.healthVerified ? "Yes" : "No"} · Evidence: {item.releaseEvidenceId}</p>
            </article>)}
        </div>
        <form className="rollbackForm" onSubmit={createRollbackPlanRecord}>
          <label>Environment<select name="environment" defaultValue="development"><option value="development">Development</option><option value="staging">Staging</option><option value="production">Production</option></select></label>
          <label>From version<select name="fromVersionId" required defaultValue=""><option value="" disabled>Select version</option>{commandCentre.versions.map((item) => <option key={item.id} value={item.id}>{item.sourceRevision}</option>)}</select></label>
          <label>To version<select name="toVersionId" required defaultValue=""><option value="" disabled>Select version</option>{commandCentre.versions.map((item) => <option key={item.id} value={item.id}>{item.sourceRevision}</option>)}</select></label>
          <button type="submit" disabled={commandCentre.versions.length < 2}>Create Rollback Plan</button>
        </form>
        <div className="rollbackList">
          {commandCentre.rollbackPlans.map((item) => <article className="rollbackCard" key={item.id}>
            <div className="cardHead"><h3>{item.environment} rollback</h3><span>{item.status}</span></div>
            <p>{item.fromVersionId} → {item.toVersionId}</p>
            <div className="protectionStrip">High risk · approval required · restore point required · execution locked</div>
            <div className="gateActions">
              <button onClick={() => approveRollbackPlanRecord(item.id)} disabled={item.status !== "planned"}>Approve Plan</button>
              <button onClick={() => confirmRollbackExecuteLocked(item.id)}>Verify Execute Lock</button>
            </div>
          </article>)}
        </div>
      </div>
      <div className="historyGrid">
        <div><span className="eyebrow">Recent jobs</span>
          {commandCentre.jobs.length === 0 ? <p className="muted">No jobs yet.</p> :
            commandCentre.jobs.map((job) => <p className="historyItem" key={job.id}>{job.state} · {job.risk} · {new Date(job.createdAt).toLocaleString()}</p>)}
        </div>
        <div><span className="eyebrow">Audit trail</span>
          {commandCentre.audit.length === 0 ? <p className="muted">No project audit events yet.</p> :
            commandCentre.audit.map((item) => <p className="historyItem" key={item.id}>{item.eventType} · {new Date(item.createdAt).toLocaleString()}</p>)}
        </div>
      </div>
    </section>}

    <section className="sectionHead"><div><span className="eyebrow">Project registry</span><h2>Managed projects</h2></div></section>
    <section className="grid">
      {projects.length === 0 && <div className="empty">No project registered in this development session yet.</div>}
      {projects.map((p) => <article className="card" key={p.id}>
        <div className="cardHead"><h3>{p.name}</h3><span>{p.type}</span></div>
        <dl><div><dt>Lifecycle</dt><dd>{p.lifecycleStatus}</dd></div>
          <div><dt>Environment</dt><dd>{Array.isArray(p.environments) ? p.environments.join(", ") : p.environments}</dd></div></dl>
        <button className="secondary" onClick={() => openCommandCentre(p.id)}>Open Command Centre</button>
      </article>)}
    </section>
  </main>;
}