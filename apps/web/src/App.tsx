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

type CommandCentre = {
  project: Project;
  environments: ProjectEnvironment[];
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
    fetch(`${apiBase}/api/projects`)
      .then((r) => r.json())
      .then((d) => setProjects(d.projects ?? []))
      .catch(() => setMessage("API offline"));
    loadPanelReadiness();
    loadImportPlans();
    loadImportWorkspaces();
  }, []);

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

  const realImportLocked = !panelReadiness?.realImportUnlocked;

  return <main className="shell">
    <header className="topbar">
      <div><span className="eyebrow">oRRbit</span><h1>AI Control Plane</h1></div>
      <div className="headerActions"><span className="status">{message}</span>
        <button onClick={() => setAddOpen((v) => !v)}>+ Add Project</button></div>
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