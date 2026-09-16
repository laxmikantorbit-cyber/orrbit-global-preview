import { useEffect, useState } from 'react'
import './CrmDemo.css'
import './CrmAdvancedHub.css'
import {
  askCrmAi,
  getCrmAiDailyBrief,
  type CrmAiAskResponse,
  type CrmAiDailyBrief,
} from './crmIntelligenceApi'

const money = (value: number) => new Intl.NumberFormat('en-IN', {
  style: 'currency', currency: 'INR', maximumFractionDigits: 0,
}).format(value)

export function CrmIntelligenceHub() {
  const [brief, setBrief] = useState<CrmAiDailyBrief | null>(null)
  const [question, setQuestion] = useState('Which leads need attention today?')
  const [answer, setAnswer] = useState<CrmAiAskResponse | null>(null)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState('Sales intelligence ready')

  async function refresh() {
    setBusy(true)
    try {
      setBrief(await getCrmAiDailyBrief())
      setMessage('Explainable CRM intelligence refreshed')
    } catch (error) {
      setMessage(error instanceof Error ? error.message : String(error))
    } finally { setBusy(false) }
  }

  async function ask() {
    if (!question.trim()) return
    setBusy(true)
    try {
      const result = await askCrmAi(question.trim())
      setAnswer(result)
      setMessage(`Ask CRM intent: ${result.intent}`)
    } catch (error) {
      setMessage(error instanceof Error ? error.message : String(error))
    } finally { setBusy(false) }
  }

  useEffect(() => { void refresh() }, [])

  return <div className="crm2-app crm-advanced-app">
    <aside className="crm2-sidebar">
      <div className="crm2-brand"><div className="crm2-brand-mark">o</div><div><strong>oRRbit</strong><span>BusinessOS</span></div></div>
      <nav className="crm2-nav">
        <a className="crm-advanced-back" href="/crm"><span>←</span>CRM Home</a>
        <a className="active" href="/crm/intelligence"><span>✦</span>AI Sales Command</a>
        <a href="/crm/inbox"><span>☀</span>My CRM Day</a>
        <a href="/crm/analytics"><span>▥</span>Detailed Analytics</a>
        <a href="/crm/leads-query"><span>⌕</span>Advanced Leads</a>
        <a href="/crm/pipeline-board"><span>↔</span>Sales Pipeline</a>
      </nav>
      <div className="crm2-sidebar-foot"><strong>EXPLAINABLE INTELLIGENCE</strong><small>No paid AI API in FreeTesting · every score includes reasons</small></div>
    </aside>

    <main className="crm2-main">
      <header className="crm2-topbar">
        <div><span className="crm2-kicker">BUSINESSOS SALES INTELLIGENCE</span><h1>AI Sales Command Centre</h1></div>
        <div className="crm2-top-actions"><button className="crm2-refresh" disabled={busy} onClick={() => void refresh()}>↻ Refresh</button></div>
      </header>
      <section className="crm2-statusbar"><div><span className={busy ? 'pulse busy' : 'pulse'} />{message}</div><span>{brief?.engine || 'businessos-crm-intelligence-rules-v1'}</span></section>

      <section className="crm-advanced-kpis">
        <article><span>Open Leads</span><strong>{brief?.openLeads ?? '—'}</strong><small>Current sales scope</small></article>
        <article><span>Overdue Follow-ups</span><strong>{brief?.overdueFollowUps ?? '—'}</strong><small>Needs immediate action</small></article>
        <article><span>Overdue Tasks</span><strong>{brief?.overdueTasks ?? '—'}</strong><small>Execution risk</small></article>
        <article><span>Open Deals</span><strong>{brief?.openOpportunities ?? '—'}</strong><small>Weighted {brief ? money(brief.weightedPipelineValue) : '—'}</small></article>
      </section>

      <section className="crm-advanced-grid">
        <article className="crm2-table-card">
          <div className="crm2-section-head"><div><span>PRIORITY ENGINE</span><h2>Top lead priorities</h2></div></div>
          <div className="crm-advanced-list">
            {brief?.topLeadPriorities.map(item => <article key={item.leadId}>
              <div><b>{item.priorityBand} · Score {item.priorityScore}</b><strong>{item.title}</strong><small>{item.nextBestAction}</small><small>{item.reasons.join(' · ')}</small></div>
              <span>{item.isStale ? `${item.staleDays}d stale` : 'Active'}</span>
            </article>)}
            {brief && !brief.topLeadPriorities.length ? <p>No open leads need prioritisation.</p> : null}
          </div>
        </article>

        <article className="crm2-table-card">
          <div className="crm2-section-head"><div><span>RISK ENGINE</span><h2>At-risk opportunities</h2></div></div>
          <div className="crm-advanced-list">
            {brief?.atRiskOpportunities.map(item => <article key={item.opportunityId}>
              <div><b>{item.riskBand} · Risk {item.riskScore}</b><strong>{item.title}</strong><small>{item.productService || 'Product/service not set'} · Weighted {money(item.weightedValue)}</small><small>{item.nextBestAction}</small><small>{item.reasons.join(' · ')}</small></div>
            </article>)}
            {brief && !brief.atRiskOpportunities.length ? <p>No open opportunities currently at risk.</p> : null}
          </div>
        </article>
      </section>

      <section className="crm-advanced-grid">
        <article className="crm2-table-card">
          <div className="crm2-section-head"><div><span>NEXT BEST ACTION</span><h2>What should happen next</h2></div></div>
          <div className="crm-advanced-list">
            {brief?.topActions.map((item, index) => <article key={`${item.type}-${item.id}`}>
              <div><b>#{index + 1} · {item.type} · Score {item.score}</b><strong>{item.title}</strong><small>{item.action}</small></div>
            </article>)}
            {brief && !brief.topActions.length ? <p>No urgent next actions.</p> : null}
          </div>
        </article>

        <article className="crm2-table-card">
          <div className="crm2-section-head"><div><span>ASK YOUR CRM</span><h2>Natural-language CRM search</h2></div></div>
          <div className="crm-advanced-form stacked">
            <input value={question} onChange={e => setQuestion(e.target.value)} onKeyDown={e => { if (e.key === 'Enter') void ask() }} placeholder="Ask: Which qualified leads need action?" />
            <div className="crm2-top-actions"><button className="crm2-primary" disabled={busy || !question.trim()} onClick={() => void ask()}>Ask CRM</button></div>
            <small>Examples: overdue follow-ups, overdue tasks, qualified leads, urgent leads, deals closing soon, customers matching a name.</small>
          </div>
          {answer ? <div className="crm-advanced-list">
            <article><div><b>{answer.intent}</b><strong>{answer.answer}</strong><small>Engine: {answer.engine}</small></div></article>
            {answer.results.map(item => <article key={`${item.type}-${item.id}`}><div><b>{item.type} · {item.status}</b><strong>{item.title}</strong><small>{item.detail || item.id}</small></div></article>)}
          </div> : null}
        </article>
      </section>
    </main>
  </div>
}
