import { useEffect, useMemo, useState } from 'react'
import './CrmDemo.css'
import './CrmAdvancedHub.css'
import {
  getCrmDetailedAnalytics,
  getCrmLeadAging,
  type CrmDetailedAnalytics,
  type CrmLeadAging,
} from './crmAnalyticsApi'

type View = 'executives' | 'productivity' | 'forecast' | 'accounts' | 'trends' | 'aging'

function money(value: number) {
  return new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR', maximumFractionDigits: 0 }).format(value || 0)
}

function percent(value: number) { return `${Math.round((value || 0) * 100) / 100}%` }
function when(value?: string | null) { return value ? new Intl.DateTimeFormat('en-IN', { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(value)) : 'Not scheduled' }

export function CrmAnalyticsHub() {
  const [view, setView] = useState<View>('executives')
  const [data, setData] = useState<CrmDetailedAnalytics | null>(null)
  const [aging, setAging] = useState<CrmLeadAging | null>(null)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState('Detailed analytics ready')

  async function refresh() {
    setBusy(true)
    try {
      const [analyticsResult, agingResult] = await Promise.all([getCrmDetailedAnalytics(), getCrmLeadAging()])
      setData(analyticsResult)
      setAging(agingResult)
      setMessage('Detailed CRM analytics refreshed')
    } catch (error) {
      setMessage(error instanceof Error ? error.message : String(error))
    } finally { setBusy(false) }
  }

  useEffect(() => { void refresh() }, [])

  const totals = useMemo(() => {
    const executives = data?.executivePerformance ?? []
    return {
      pipeline: executives.reduce((sum, x) => sum + x.openPipelineValue, 0),
      weighted: executives.reduce((sum, x) => sum + x.weightedPipelineValue, 0),
      won: executives.reduce((sum, x) => sum + x.wonValue, 0),
      converted: executives.reduce((sum, x) => sum + x.convertedLeads, 0),
    }
  }, [data])

  const title = view === 'executives' ? 'Executive performance'
    : view === 'productivity' ? 'Follow-up & task productivity'
      : view === 'forecast' ? 'Sales forecast'
        : view === 'accounts' ? 'Account-wise business'
          : view === 'trends' ? 'Monthly sales trends'
            : 'Lead aging & inactivity'

  return <div className="crm2-app crm-advanced-app">
    <aside className="crm2-sidebar">
      <div className="crm2-brand"><div className="crm2-brand-mark">o</div><div><strong>oRRbit</strong><span>BusinessOS</span></div></div>
      <nav className="crm2-nav">
        <a className="crm-advanced-back" href="/crm"><span>←</span>CRM Home</a>
        <button className={view === 'executives' ? 'active' : ''} onClick={() => setView('executives')}><span>U</span>Executive Performance</button>
        <button className={view === 'productivity' ? 'active' : ''} onClick={() => setView('productivity')}><span>✓</span>Productivity</button>
        <button className={view === 'forecast' ? 'active' : ''} onClick={() => setView('forecast')}><span>↗</span>Forecast</button>
        <button className={view === 'accounts' ? 'active' : ''} onClick={() => setView('accounts')}><span>A</span>Account Business</button>
        <button className={view === 'trends' ? 'active' : ''} onClick={() => setView('trends')}><span>≋</span>Monthly Trends</button>
        <button className={view === 'aging' ? 'active' : ''} onClick={() => setView('aging')}><span>⌛</span>Lead Aging</button>
      </nav>
      <div className="crm2-sidebar-foot"><strong>SALES ANALYTICS</strong><small>Team · Productivity · Forecast · Customers · Aging</small></div>
    </aside>

    <main className="crm2-main">
      <header className="crm2-topbar"><div><span className="crm2-kicker">CRM ANALYTICS</span><h1>{title}</h1></div><div className="crm2-top-actions"><button className="crm2-refresh" disabled={busy} onClick={() => void refresh()}>↻ Refresh</button></div></header>
      <section className="crm2-statusbar"><div><span className={busy ? 'pulse busy' : 'pulse'} />{message}</div><span>Persistent CRM data · live calculations</span></section>

      <section className="crm2-metrics crm-advanced-metrics">
        <article><span>Open pipeline</span><strong>{money(totals.pipeline)}</strong><small>All visible owners</small></article>
        <article><span>Weighted forecast</span><strong>{money(totals.weighted)}</strong><small>Probability adjusted</small></article>
        <article className="accent"><span>Won value</span><strong>{money(totals.won)}</strong><small>Closed won opportunities</small></article>
        <article><span>{view === 'aging' ? 'Open leads' : 'Converted leads'}</span><strong>{view === 'aging' ? aging?.openLeadCount ?? '—' : totals.converted}</strong><small>Current visible scope</small></article>
      </section>

      {view === 'executives' ? <section className="crm2-table-card">
        <div className="crm2-section-head"><div><span>TEAM PERFORMANCE</span><h2>Executive scorecard</h2></div></div>
        <div className="crm2-table-head" style={{ gridTemplateColumns: '1.4fr repeat(7, 1fr)' }}><span>Executive</span><span>Leads</span><span>Converted</span><span>Conv.%</span><span>Follow-ups</span><span>Tasks</span><span>Weighted</span><span>Won</span></div>
        <div className="crm2-table-body">{data?.executivePerformance.map(x => <article key={x.userId} className="crm2-row" style={{ gridTemplateColumns: '1.4fr repeat(7, 1fr)' }}><strong>{x.userName}</strong><span>{x.leads}</span><span>{x.convertedLeads}</span><span>{percent(x.conversionPercent)}</span><span>{x.completedFollowUps}</span><span>{x.completedTasks}</span><span>{money(x.weightedPipelineValue)}</span><span>{money(x.wonValue)}</span></article>)}</div>
      </section> : null}

      {view === 'productivity' ? <section className="crm-advanced-grid">
        <article className="crm2-table-card"><div className="crm2-section-head"><div><span>FOLLOW-UPS</span><h2>Follow-up productivity</h2></div></div><div className="crm-advanced-list">{data?.followUpProductivity.map(x => <article key={x.userId || x.userName}><div><b>{x.userName}</b><strong>{x.completed} completed / {x.total} total</strong><small>{x.open} open · {x.cancelled} cancelled</small></div><span className={x.overdue ? 'danger' : ''}>{x.overdue} overdue</span></article>)}</div></article>
        <article className="crm2-table-card"><div className="crm2-section-head"><div><span>TASKS</span><h2>Task performance</h2></div></div><div className="crm-advanced-list">{data?.taskPerformance.map(x => <article key={x.userId || x.userName}><div><b>{x.userName}</b><strong>{x.completed} completed / {x.total} total</strong><small>{x.open} open · {x.cancelled} cancelled</small></div><span className={x.overdue ? 'danger' : ''}>{x.overdue} overdue</span></article>)}</div></article>
      </section> : null}

      {view === 'forecast' ? <section className="crm-advanced-grid">
        <article className="crm2-table-card"><div className="crm2-section-head"><div><span>OWNER FORECAST</span><h2>Pipeline by owner</h2></div></div><div className="crm-advanced-list">{data?.ownerForecast.map(x => <article key={x.label}><div><b>{x.label}</b><strong>{money(x.pipelineValue)}</strong><small>{x.opportunityCount} opportunity(s)</small></div><span>{money(x.weightedValue)} weighted</span></article>)}</div></article>
        <article className="crm2-table-card"><div className="crm2-section-head"><div><span>MONTH FORECAST</span><h2>Expected close month</h2></div></div><div className="crm-advanced-list">{data?.monthForecast.map(x => <article key={x.label}><div><b>{x.label}</b><strong>{money(x.pipelineValue)}</strong><small>{x.opportunityCount} opportunity(s)</small></div><span>{money(x.weightedValue)} weighted</span></article>)}</div></article>
      </section> : null}

      {view === 'accounts' ? <section className="crm2-table-card">
        <div className="crm2-section-head"><div><span>CUSTOMER VALUE</span><h2>Account-wise business</h2></div></div>
        <div className="crm-advanced-list">{data?.accountBusiness.map(x => <article key={x.accountId}><div><b>{x.accountName}</b><strong>{x.opportunityCount} opportunity(s)</strong><small>Open {money(x.openPipelineValue)} · Weighted {money(x.weightedPipelineValue)}</small></div><span>{money(x.wonValue)} won</span></article>)}{data && !data.accountBusiness.length ? <p>No account business data yet.</p> : null}</div>
      </section> : null}

      {view === 'trends' ? <section className="crm2-table-card">
        <div className="crm2-section-head"><div><span>TRENDS</span><h2>Month-wise CRM movement</h2></div></div>
        <div className="crm2-table-head" style={{ gridTemplateColumns: '1.4fr repeat(3, 1fr)' }}><span>Month</span><span>Leads created</span><span>Converted</span><span>Expected closures</span></div>
        <div className="crm2-table-body">{data?.monthlyTrends.map(x => <article key={x.month} className="crm2-row" style={{ gridTemplateColumns: '1.4fr repeat(3, 1fr)' }}><strong>{x.month}</strong><span>{x.leadsCreated}</span><span>{x.leadsConverted}</span><span>{x.expectedClosures}</span></article>)}</div>
      </section> : null}

      {view === 'aging' ? <>
        <section className="crm-advanced-grid">
          {aging?.buckets.map(x => <article className="crm2-table-card" key={x.label}><div className="crm2-section-head"><div><span>AGING BUCKET</span><h2>{x.label}</h2></div></div><div className="crm2-metrics crm-advanced-metrics"><article><span>Lead age</span><strong>{x.leadAgeCount}</strong><small>Created in bucket</small></article><article><span>Inactive</span><strong>{x.inactivityCount}</strong><small>No update in bucket</small></article></div></article>)}
        </section>
        <section className="crm2-table-card">
          <div className="crm2-section-head"><div><span>OPEN LEADS</span><h2>Oldest / stalest leads first</h2></div></div>
          <div className="crm-advanced-list">{aging?.leads.map(x => <article key={x.leadId}><div><b>{x.priority} · {x.status}</b><strong>{x.title}</strong><small>{x.ownerName} · {x.productInterest || 'Product not set'} · Next: {when(x.nextFollowUpAtUtc)}</small></div><span className={x.inactiveDays >= 7 ? 'danger' : ''}>{x.ageDays}d old · {x.inactiveDays}d inactive</span></article>)}{aging && !aging.leads.length ? <p>No open leads.</p> : null}</div>
        </section>
      </> : null}
    </main>
  </div>
}
