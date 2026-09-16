import { useEffect, useState } from 'react'
import './CrmDemo.css'
import './CrmAdvancedHub.css'
import { getCrmOpportunityAging, type CrmOpportunityAging } from './crmAnalyticsApi'

const money = (value: number) => new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR', maximumFractionDigits: 0 }).format(value || 0)

export function CrmDealAgingHub() {
  const [data, setData] = useState<CrmOpportunityAging | null>(null)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState('Deal aging ready')

  async function refresh() {
    setBusy(true)
    try { setData(await getCrmOpportunityAging()); setMessage('Deal aging refreshed') }
    catch (error) { setMessage(error instanceof Error ? error.message : String(error)) }
    finally { setBusy(false) }
  }
  useEffect(() => { void refresh() }, [])

  return <div className="crm2-app crm-advanced-app">
    <aside className="crm2-sidebar">
      <div className="crm2-brand"><div className="crm2-brand-mark">o</div><div><strong>oRRbit</strong><span>BusinessOS</span></div></div>
      <nav className="crm2-nav"><a className="crm-advanced-back" href="/crm/analytics"><span>←</span>Analytics</a><a className="active" href="/crm/deal-aging"><span>⌛</span>Deal Aging</a><a href="/crm/pipeline-board"><span>↔</span>Sales Pipeline</a></nav>
      <div className="crm2-sidebar-foot"><strong>DEAL AGING</strong><small>Opportunity age · Stage age · Closing risk</small></div>
    </aside>
    <main className="crm2-main">
      <header className="crm2-topbar"><div><span className="crm2-kicker">CRM OPPORTUNITY ANALYTICS</span><h1>Deal aging</h1></div><div className="crm2-top-actions"><button className="crm2-refresh" disabled={busy} onClick={() => void refresh()}>↻ Refresh</button></div></header>
      <section className="crm2-statusbar"><div><span className={busy ? 'pulse busy' : 'pulse'} />{message}</div><span>{data?.opportunityCount ?? 0} opportunity(s)</span></section>
      <section className="crm-advanced-grid">{data?.buckets.map(x => <article className="crm2-table-card" key={x.label}><div className="crm2-section-head"><div><span>AGE BUCKET</span><h2>{x.label}</h2></div></div><div className="crm2-metrics crm-advanced-metrics"><article><span>Deals</span><strong>{x.count}</strong><small>Visible scope</small></article></div></article>)}</section>
      <section className="crm2-table-card">
        <div className="crm2-section-head"><div><span>OPPORTUNITIES</span><h2>Oldest deals first</h2></div></div>
        <div className="crm-advanced-list">{data?.opportunities.map(x => <article key={x.opportunityId}><div><b>{x.stage} · {x.ownerName}</b><strong>{x.title}</strong><small>{x.productService || 'Product/service not set'} · {money(x.estimatedValue)} · {x.probabilityPercent}% probability · Close {x.expectedCloseDate || 'not set'}</small></div><span>{x.ageDays == null ? 'Age unknown' : `${x.ageDays}d old`} · {x.stageAgeDays == null ? 'Stage age unknown' : `${x.stageAgeDays}d in stage`}</span></article>)}{data && !data.opportunities.length ? <p>No opportunities.</p> : null}</div>
      </section>
    </main>
  </div>
}
