import { useEffect, useState, type FormEvent } from "react";

type Project = {
  id: string;
  name: string;
  type: string;
  lifecycleStatus: string;
  environments: string[];
};

type Plan = {
  id: string;
  planner: string;
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
    setMessage("Plan ready for review");
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
        <button disabled title="Enabled after approval/job engine is implemented">Approve & Create — locked until Step 6</button>
      </div>}
    </section>}

    <section className="sectionHead"><div><span className="eyebrow">Project registry</span><h2>Managed projects</h2></div></section>
    <section className="grid">
      {projects.length === 0 && <div className="empty">No project registered in this development session yet.</div>}
      {projects.map((p) => <article className="card" key={p.id}>
        <div className="cardHead"><h3>{p.name}</h3><span>{p.type}</span></div>
        <dl><div><dt>Lifecycle</dt><dd>{p.lifecycleStatus}</dd></div>
          <div><dt>Environment</dt><dd>{Array.isArray(p.environments) ? p.environments.join(", ") : p.environments}</dd></div></dl>
        <button className="secondary" disabled>Command Centre — next stage</button>
      </article>)}
    </section>
  </main>;
}