import { useEffect, useMemo, useState } from 'react'
import './CrmDemo.css'
import './CrmAdvancedHub.css'
import { listCrmAccounts, listCrmLeads, listCrmTeam, type CrmAccount, type CrmLead, type CrmTeamMember } from './crmApi'
import {
  bulkUpdateCrmLeads,
  checkCrmDuplicates,
  exitCrmEmployee,
  getCrmCustomer360,
  getCrmNotifications,
  getCrmReportSummary,
  globalCrmSearch,
  type CrmCustomer360,
  type CrmDuplicateCheck,
  type CrmGlobalSearchHit,
  type CrmNotification,
  type CrmReportSummary,
} from './crmAdvancedApi'

type AdvancedView = 'reports' | 'search' | 'notifications' | 'customer360' | 'data-quality' | 'bulk' | 'team-exit'

function money(value: number) {
  return new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR', maximumFractionDigits: 0 }).format(value || 0)
}

function when(value?: string | null) {
  if (!value) return '—'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : new Intl.DateTimeFormat('en-IN', { dateStyle: 'medium', timeStyle: 'short' }).format(date)
}

export function CrmAdvancedHub() {
  const [view, setView] = useState<AdvancedView>('reports')
  const [report, setReport] = useState<CrmReportSummary | null>(null)
  const [notifications, setNotifications] = useState<CrmNotification[]>([])
  const [accounts, setAccounts] = useState<CrmAccount[]>([])
  const [leads, setLeads] = useState<CrmLead[]>([])
  const [team, setTeam] = useState<CrmTeamMember[]>([])
  const [query, setQuery] = useState('')
  const [searchHits, setSearchHits] = useState<CrmGlobalSearchHit[]>([])
  const [selectedAccountId, setSelectedAccountId] = useState('')
  const [customer360, setCustomer360] = useState<CrmCustomer360 | null>(null)
  const [duplicate, setDuplicate] = useState<CrmDuplicateCheck | null>(null)
  const [dupBusiness, setDupBusiness] = useState('')
  const [dupMobile, setDupMobile] = useState('')
  const [dupEmail, setDupEmail] = useState('')
  const [selectedLeadIds, setSelectedLeadIds] = useState<string[]>([])
  const [bulkStatus, setBulkStatus] = useState('Qualified')
  const [exitUserId, setExitUserId] = useState('')
  const [reassignUserId, setReassignUserId] = useState('')
  const [message, setMessage] = useState('Advanced CRM controls ready')
  const [busy, setBusy] = useState(false)

  const activeTeam = useMemo(() => team.filter(x => x.active), [team])

  async function refresh() {
    setBusy(true)
    try {
      const [reportResult, notificationResult, accountResult, leadResult, teamResult] = await Promise.all([
        getCrmReportSummary(), getCrmNotifications(), listCrmAccounts(), listCrmLeads(), listCrmTeam(),
      ])
      setReport(reportResult)
      setNotifications(notificationResult.items)
      setAccounts(accountResult.accounts)
      setLeads(leadResult.leads)
      setTeam(teamResult.members)
      if (!selectedAccountId && accountResult.accounts[0]) setSelectedAccountId(accountResult.accounts[0].id)
      setMessage('Advanced CRM data refreshed')
    } catch (error) {
      setMessage(error instanceof Error ? error.message : String(error))
    } finally { setBusy(false) }
  }

  useEffect(() => { void refresh() }, [])

  async function runSearch() {
    if (!query.trim()) { setSearchHits([]); return }
    setBusy(true)
    try { setSearchHits((await globalCrmSearch(query)).results); setMessage('Global search completed') }
    catch (error) { setMessage(error instanceof Error ? error.message : String(error)) }
    finally { setBusy(false) }
  }

  async function loadCustomer360() {
    if (!selectedAccountId) return
    setBusy(true)
    try { setCustomer360(await getCrmCustomer360(selectedAccountId)); setMessage('Customer 360 loaded') }
    catch (error) { setMessage(error instanceof Error ? error.message : String(error)) }
    finally { setBusy(false) }
  }

  async function runDuplicateCheck() {
    setBusy(true)
    try { setDuplicate(await checkCrmDuplicates({ business: dupBusiness, mobile: dupMobile, email: dupEmail })); setMessage('Duplicate/data-quality check completed') }
    catch (error) { setMessage(error instanceof Error ? error.message : String(error)) }
    finally { setBusy(false) }
  }

  async function runBulkUpdate() {
    if (selectedLeadIds.length === 0) { setMessage('Select at least one lead'); return }
    setBusy(true)
    try {
      const result = await bulkUpdateCrmLeads({ leadIds: selectedLeadIds, status: bulkStatus, changeOwner: false, note: 'Updated from Advanced CRM bulk workspace' })
      setMessage(`${result.updatedLeadIds.length} leads updated · ${result.failed.length} failed`)
      setSelectedLeadIds([])
      await refresh()
    } catch (error) { setMessage(error instanceof Error ? error.message : String(error)); setBusy(false) }
  }

  async function runEmployeeExit() {
    if (!exitUserId || !reassignUserId || exitUserId === reassignUserId) { setMessage('Select exiting user and a different reassignment user'); return }
    setBusy(true)
    try {
      const result = await exitCrmEmployee(exitUserId, reassignUserId)
      setMessage(`Exit completed · ${result.leadsReassigned} leads, ${result.followUpsReassigned} follow-ups, ${result.tasksReassigned} tasks, ${result.opportunitiesReassigned} opportunities reassigned`)
      setExitUserId(''); setReassignUserId('')
      await refresh()
    } catch (error) { setMessage(error instanceof Error ? error.message : String(error)); setBusy(false) }
  }

  return <div className="crm2-app crm-advanced-app">
    <aside className="crm2-sidebar">
      <div className="crm2-brand"><div className="crm2-brand-mark">o</div><div><strong>oRRbit</strong><span>BusinessOS</span></div></div>
      <nav className="crm2-nav">
        <a className="crm-advanced-back" href="/crm"><span>←</span>CRM Home</a>
        {([
          ['reports', '↗', 'Reports & Forecast'], ['search', '⌕', 'Global Search'], ['notifications', '!', 'Notifications'],
          ['customer360', '◎', 'Customer 360'], ['data-quality', '✓', 'Data Quality'], ['bulk', '⇄', 'Bulk Actions'], ['team-exit', 'U', 'Exit & Reassign'],
        ] as Array<[AdvancedView,string,string]>).map(([key, icon, label]) =>
          <button key={key} className={view === key ? 'active' : ''} onClick={() => setView(key)}><span>{icon}</span>{label}</button>)}
      </nav>
      <div className="crm2-sidebar-foot"><strong>UNIFIED BUSINESSOS</strong><small>CRM → Customer → Commerce → Licensing</small></div>
    </aside>
    <main className="crm2-main">
      <header className="crm2-topbar"><div><span className="crm2-kicker">ADVANCED CRM COMMAND CENTRE</span><h1>{view === 'customer360' ? 'Customer 360' : view === 'data-quality' ? 'Duplicate & data quality' : view === 'bulk' ? 'Bulk lead operations' : view === 'team-exit' ? 'Employee exit & reassignment' : view === 'notifications' ? 'Notifications & reminders' : view === 'search' ? 'Global CRM search' : 'Reports & sales forecast'}</h1></div><div className="crm2-top-actions"><button className="crm2-refresh" disabled={busy} onClick={() => void refresh()}>↻ Refresh</button></div></header>
      <section className="crm2-statusbar"><div><span className={busy ? 'pulse busy' : 'pulse'} />{message}</div><span>oRRbit.BusinessOS unified CRM</span></section>

      {view === 'reports' && report ? <>
        <section className="crm2-metrics crm-advanced-metrics">
          <article><span>Conversion</span><strong>{report.conversionPercent}%</strong><small>{report.convertedLeads} / {report.totalLeads} leads</small></article>
          <article><span>Open pipeline</span><strong>{money(report.openPipelineValue)}</strong><small>Current open deals</small></article>
          <article><span>Weighted forecast</span><strong>{money(report.weightedPipelineValue)}</strong><small>Probability adjusted</small></article>
          <article className="accent"><span>Won value</span><strong>{money(report.wonValue)}</strong><small>{report.lostDeals} lost deals</small></article>
        </section>
        <section className="crm-advanced-grid">
          <article className="crm2-table-card"><div className="crm2-section-head"><div><span>LEAD SOURCE</span><h2>Acquisition mix</h2></div></div>{Object.entries(report.leadSources).map(([name,count]) => <div className="crm-advanced-row" key={name}><span>{name}</span><strong>{count}</strong></div>)}</article>
          <article className="crm2-table-card"><div className="crm2-section-head"><div><span>PIPELINE</span><h2>Opportunity stages</h2></div></div>{Object.entries(report.opportunityStages).map(([name,count]) => <div className="crm-advanced-row" key={name}><span>{name}</span><strong>{count}</strong></div>)}</article>
          <article className="crm2-table-card"><div className="crm2-section-head"><div><span>WORKLOAD</span><h2>Action health</h2></div></div><div className="crm-advanced-row"><span>Open follow-ups</span><strong>{report.openFollowUps}</strong></div><div className="crm-advanced-row danger"><span>Overdue follow-ups</span><strong>{report.overdueFollowUps}</strong></div><div className="crm-advanced-row"><span>Open tasks</span><strong>{report.openTasks}</strong></div><div className="crm-advanced-row danger"><span>Overdue tasks</span><strong>{report.overdueTasks}</strong></div></article>
        </section>
      </> : null}

      {view === 'search' ? <section className="crm2-table-card"><div className="crm-advanced-form"><input value={query} onChange={e => setQuery(e.target.value)} onKeyDown={e => { if (e.key === 'Enter') void runSearch() }} placeholder="Search lead, business, contact, mobile, email, opportunity…"/><button className="crm2-primary" onClick={() => void runSearch()}>Search</button></div><div className="crm-advanced-list">{searchHits.map(hit => <article key={`${hit.type}-${hit.id}`}><div><b>{hit.type}</b><strong>{hit.title}</strong><small>{hit.subtitle || hit.secondary || hit.id}</small></div><span>{hit.status}</span></article>)}{query && searchHits.length === 0 ? <p>No matching CRM records.</p> : null}</div></section> : null}

      {view === 'notifications' ? <section className="crm2-table-card"><div className="crm2-section-head"><div><span>ACTION CENTRE</span><h2>{notifications.length} reminders</h2></div></div><div className="crm-advanced-list">{notifications.map(item => <article key={`${item.type}-${item.recordId}`} className={item.severity === 'High' ? 'danger-card' : ''}><div><b>{item.type}</b><strong>{item.title}</strong><small>{item.detail}</small></div><span>{when(item.dueAtUtc)}</span></article>)}{notifications.length === 0 ? <p>No overdue or upcoming actions.</p> : null}</div></section> : null}

      {view === 'customer360' ? <section className="crm2-table-card"><div className="crm-advanced-form"><select value={selectedAccountId} onChange={e => setSelectedAccountId(e.target.value)}><option value="">Select customer account</option>{accounts.map(a => <option value={a.id} key={a.id}>{a.name}</option>)}</select><button className="crm2-primary" onClick={() => void loadCustomer360()}>Load 360°</button></div>{customer360 ? <div className="crm-customer360"><div className="crm2-section-head"><div><span>CUSTOMER</span><h2>{customer360.account.name}</h2><small>{customer360.account.primaryContactName || 'No primary contact'} · {customer360.account.primaryContactPhone || customer360.account.primaryContactEmail || 'No contact detail'}</small></div><strong>{customer360.account.status}</strong></div><section className="crm2-metrics"><article><span>Contacts</span><strong>{customer360.account.contactCount}</strong></article><article><span>Opportunities</span><strong>{customer360.opportunities.length}</strong></article><article><span>Subscriptions</span><strong>{customer360.activations.length}</strong></article><article className="accent"><span>Paid order value</span><strong>{money(customer360.paidOrderValue)}</strong></article></section><h3>BusinessOS lifecycle</h3><div className="crm-advanced-list">{customer360.opportunities.map(x => <article key={x.id}><div><b>Opportunity</b><strong>{x.title}</strong><small>{money(x.estimatedValue)} · {x.probabilityPercent}% probability</small></div><span>{x.stage}</span></article>)}{customer360.orders.map(x => <article key={x.commerceOrderId}><div><b>Order</b><strong>{x.productCode || 'BusinessOS product'}</strong><small>{money(x.amount)} · {x.currencyCode}</small></div><span>{x.orderStatus}</span></article>)}{customer360.activations.map(x => <article key={x.subscriptionId}><div><b>Subscription / License</b><strong>{x.productCode}</strong><small>License {x.licenseId}</small></div><span>Valid to {x.validUntil}</span></article>)}</div></div> : null}</section> : null}

      {view === 'data-quality' ? <section className="crm2-table-card"><div className="crm-advanced-form stacked"><input value={dupBusiness} onChange={e => setDupBusiness(e.target.value)} placeholder="Business name"/><input value={dupMobile} onChange={e => setDupMobile(e.target.value)} placeholder="Mobile"/><input value={dupEmail} onChange={e => setDupEmail(e.target.value)} placeholder="Email"/><button className="crm2-primary" onClick={() => void runDuplicateCheck()}>Check duplicates</button></div>{duplicate ? <div className="crm-advanced-list"><h3>{duplicate.hasDuplicates ? `${duplicate.matches.length} possible duplicate(s)` : 'No duplicates found'}</h3>{duplicate.matches.map(x => <article key={`${x.type}-${x.id}`}><div><b>{x.type}</b><strong>{x.name}</strong><small>{x.mobile || x.email || x.id}</small></div><span>Review</span></article>)}</div> : null}</section> : null}

      {view === 'bulk' ? <section className="crm2-table-card"><div className="crm-advanced-form"><select value={bulkStatus} onChange={e => setBulkStatus(e.target.value)}><option>New</option><option>Contacted</option><option>Qualified</option><option>Unqualified</option></select><button className="crm2-primary" disabled={!selectedLeadIds.length} onClick={() => void runBulkUpdate()}>Update {selectedLeadIds.length} lead(s)</button></div><div className="crm-advanced-checklist">{leads.filter(x => x.status !== 'Converted').map(lead => <label key={lead.id}><input type="checkbox" checked={selectedLeadIds.includes(lead.id)} onChange={e => setSelectedLeadIds(items => e.target.checked ? [...items, lead.id] : items.filter(id => id !== lead.id))}/><span><strong>{lead.title}</strong><small>{lead.status} · {lead.contactName || lead.mobileNumber || 'No contact'}</small></span></label>)}</div></section> : null}

      {view === 'team-exit' ? <section className="crm2-table-card"><div className="crm2-section-head"><div><span>SAFE OFFBOARDING</span><h2>Deactivate & reassign open work</h2></div></div><div className="crm-advanced-form stacked"><select value={exitUserId} onChange={e => setExitUserId(e.target.value)}><option value="">Exiting user</option>{activeTeam.filter(x => x.role !== 'Owner').map(x => <option key={x.id} value={x.id}>{x.displayName} · {x.role}</option>)}</select><select value={reassignUserId} onChange={e => setReassignUserId(e.target.value)}><option value="">Reassign work to</option>{activeTeam.filter(x => x.id !== exitUserId).map(x => <option key={x.id} value={x.id}>{x.displayName} · {x.role}</option>)}</select><button className="crm2-primary" onClick={() => void runEmployeeExit()}>Reassign & deactivate</button></div><p className="crm-advanced-note">Open leads, follow-ups, tasks and opportunities move to the selected active user. Historical records remain unchanged.</p></section> : null}
    </main>
  </div>
}
