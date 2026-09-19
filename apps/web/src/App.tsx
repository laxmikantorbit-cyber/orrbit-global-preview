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

const apiBase = import.meta.env.VITE_API_BASE_URL ?? "";

export default function App() {
  const [projects, setProjects] = useState<Project[]>([]);
  const [addOpen, setAddOpen] = useState(false);
  const [mode, setMode] = useState<"manual" | "ai">("manual");
  const [message, setMessage] = useState("Ready");
  const [plan, setPlan] = useState<Plan | null>(null);
  const [planName, setPlanName] = useState("");
  const [planRepository, setPlanRepository] = useState("");
  const [commandCentre, setCommandCentre] = useState<CommandCentre | null>(null);
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
  }, []);

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
          <option value="import">Import existing project</option>
        </select></label>
        <label>Repository (optional)<input name="repository" placeholder="owner/repository" /></label>
        <button type="submit">Add to Development</button>
      </form> : <form className="form aiForm" onSubmit={createPlan}>
        <label>Describe the project<textarea name="prompt" required minLength={10}
          placeholder="Import my Martial Arts ERP from ChatGPT Sites as a development-mode SaaS project. Keep production disabled." /></label>
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