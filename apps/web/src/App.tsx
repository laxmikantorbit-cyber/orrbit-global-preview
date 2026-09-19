const projects = [
  { name: "Martial Arts ERP", type: "SaaS", state: "Development", health: "Not onboarded" },
  { name: "orrbit.in", type: "Website", state: "Migration", health: "Existing staging" },
  { name: "orrbitrepair.com", type: "Website + API", state: "Migration", health: "Existing staging" }
];

export default function App() {
  return <main className="shell">
    <header className="topbar">
      <div><span className="eyebrow">oRRbit</span><h1>AI Control Plane</h1></div>
      <button>+ Add Project</button>
    </header>
    <section className="hero">
      <h2>Websites and SaaS, one controlled workflow.</h2>
      <p>Plan with AI, build in isolation, verify, approve, deploy and roll back.</p>
    </section>
    <section className="grid">
      {projects.map((p) => <article className="card" key={p.name}>
        <div className="cardHead"><h3>{p.name}</h3><span>{p.type}</span></div>
        <dl><div><dt>Lifecycle</dt><dd>{p.state}</dd></div><div><dt>Status</dt><dd>{p.health}</dd></div></dl>
        <button className="secondary">Open Command Centre</button>
      </article>)}
    </section>
  </main>;
}