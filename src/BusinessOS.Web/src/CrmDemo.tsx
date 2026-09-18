import { useEffect, useMemo, useState } from 'react'
import './CrmDemo.css'
import { readiness } from './businessosApi'
import {
  changeCrmLeadStatus,
  completeCrmFollowUp,
  completeCrmTask,
  createCrmLead,
  crmDashboard,
  crmWorkSummary,
  getCrmSession,
  setCrmDemoUserId,
  listCrmAccounts,
  listCrmFollowUps,
  listCrmLeads,
  listCrmOpportunities,
  listCrmRoles,
  listCrmTasks,
  listCrmTeam,
  type CrmAccount,
  type CrmDashboard,
  type CrmFollowUp,
  type CrmLead,
  type CrmOpportunity,
  type CrmRole,
  type CrmSession,
  type CrmTask,
  type CrmTeamMember,
  type CrmWorkSummary,
} from './crmApi'
import { CrmLeadDrawer } from './CrmLeadDrawer'
import { CrmWorkView } from './CrmWorkView'
import { CrmSalesView } from './CrmSalesView'
import { CrmTeamView } from './CrmTeamView'

type CrmView = 'overview' | 'leads' | 'pipeline' | 'accounts' | 'opportunities' | 'followups' | 'tasks' | 'reports' | 'team'
const statuses = ['New', 'Contacted', 'Qualified', 'Converted', 'Unqualified']
const statusLabels: Record<string, string> = { New: 'New enquiry', Contacted: 'Talked once', Qualified: 'Interested customer', Converted: 'Became customer', Unqualified: 'Not interested now' }
function initialDashboard(): CrmDashboard {
  return { totalLeads: 0, new: 0, contacted: 0, qualified: 0, converted: 0, unqualified: 0, statusCounts: {} }
}

function initialWorkSummary(): CrmWorkSummary {
  return { openFollowUps: 0, overdueFollowUps: 0, dueTodayFollowUps: 0, openTasks: 0, overdueTasks: 0 }
}

function formatCreated(value?: string | null) {
  if (!value) return '—'
  const parsed = new Date(value)
  if (Number.isNaN(parsed.getTime())) return value
  return new Intl.DateTimeFormat('en-IN', { day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit' }).format(parsed)
}

export function CrmDemo() {
  const [view, setView] = useState<CrmView>('overview')
  const [leads, setLeads] = useState<CrmLead[]>([])
  const [followUps, setFollowUps] = useState<CrmFollowUp[]>([])
  const [tasks, setTasks] = useState<CrmTask[]>([])
  const [accounts, setAccounts] = useState<CrmAccount[]>([])
  const [opportunities, setOpportunities] = useState<CrmOpportunity[]>([])
  const [teamMembers, setTeamMembers] = useState<CrmTeamMember[]>([])
  const [roles, setRoles] = useState<CrmRole[]>([])
  const [session, setSession] = useState<CrmSession | null>(null)
  const [dashboard, setDashboard] = useState<CrmDashboard>(initialDashboard())
  const [workSummary, setWorkSummary] = useState<CrmWorkSummary>(initialWorkSummary())
  const [message, setMessage] = useState('CRM workspace ready')
  const [storageLabel, setStorageLabel] = useState('Staging data')
  const [loading, setLoading] = useState(false)
  const [query, setQuery] = useState('')
  const [statusFilter, setStatusFilter] = useState('All')
  const [showAddLead, setShowAddLead] = useState(false)
  const [selectedLeadId, setSelectedLeadId] = useState<string | null>(null)
  const [title, setTitle] = useState('')
  const [source, setSource] = useState('WhatsApp')
  const [contactName, setContactName] = useState('')
  const [mobile, setMobile] = useState('')
  const [email, setEmail] = useState('')
  const [product, setProduct] = useState('AI Repair Business Management Software')
  const [priority, setPriority] = useState('Normal')
  const [notes, setNotes] = useState('')

  const pipeline = useMemo(() => statuses.map((status) => ({
    status,
    count: leads.filter((lead) => lead.status === status).length,
  })), [leads])

  const filteredLeads = useMemo(() => {
    const search = query.trim().toLowerCase()
    return leads.filter((lead) => {
      const matchesStatus = statusFilter === 'All' || lead.status === statusFilter
      const haystack = [lead.title, lead.leadSource, lead.contactName, lead.mobileNumber, lead.email, lead.productInterest].filter(Boolean).join(' ').toLowerCase()
      return matchesStatus && (!search || haystack.includes(search))
    })
  }, [leads, query, statusFilter])

  const conversionRate = dashboard.totalLeads > 0 ? Math.round((dashboard.converted / dashboard.totalLeads) * 100) : 0
  const can = (permission: string) => session?.member.permissions.includes(permission) ?? false

  async function refresh() {
    setLoading(true)
    try {
      const sessionResult = await getCrmSession()
      setSession(sessionResult)
      const allowed = (permission: string) => sessionResult.member.permissions.includes(permission)
      const [leadResult, dashResult, followResult, taskResult, workResult, accountResult, opportunityResult, teamResult, roleResult, readyResult] = await Promise.all([
        allowed('ViewLeads') ? listCrmLeads() : Promise.resolve({ leads: [] as CrmLead[] }),
        allowed('ViewDashboard') ? crmDashboard() : Promise.resolve(initialDashboard()),
        allowed('ManageFollowUps') ? listCrmFollowUps() : Promise.resolve({ followUps: [] as CrmFollowUp[] }),
        allowed('ManageTasks') ? listCrmTasks() : Promise.resolve({ tasks: [] as CrmTask[] }),
        allowed('ViewDashboard') ? crmWorkSummary() : Promise.resolve(initialWorkSummary()),
        allowed('ViewAccounts') ? listCrmAccounts() : Promise.resolve({ accounts: [] as CrmAccount[] }),
        allowed('ViewOpportunities') ? listCrmOpportunities() : Promise.resolve({ opportunities: [] as CrmOpportunity[] }),
        allowed('ViewTeam') ? listCrmTeam() : Promise.resolve({ members: [sessionResult.member] }),
        allowed('ViewDashboard') ? listCrmRoles() : Promise.resolve({ roles: [] as CrmRole[] }),
        readiness(),
      ])
      setLeads(leadResult.leads)
      setDashboard(dashResult)
      setFollowUps(followResult.followUps)
      setTasks(taskResult.tasks)
      setWorkSummary(workResult)
      setAccounts(accountResult.accounts)
      setOpportunities(opportunityResult.opportunities)
      setTeamMembers(teamResult.members)
      setRoles(roleResult.roles)
      setStorageLabel(readyResult.storageMode === 'Postgres' ? 'Saved online data' : 'Temporary test data')
      setMessage(`Customer enquiries refreshed - ${sessionResult.canViewAllOwnedRecords ? 'Team view' : 'My view'}`)
    } catch (error) {
      setMessage(error instanceof Error ? error.message : String(error))
    } finally {
      setLoading(false)
    }
  }

  async function switchUser(userId: string) {
    setCrmDemoUserId(userId)
    setSelectedLeadId(null)
    setView('overview')
    await refresh()
  }

  async function addLead() {
    if (!title.trim()) return
    setLoading(true)
    try {
      const created = await createCrmLead({
        title: title.trim(), leadSource: source, contactName, mobileNumber: mobile,
        email, productInterest: product, notes, priority,
      })
      setShowAddLead(false)
      setTitle(''); setContactName(''); setMobile(''); setEmail(''); setNotes('')
      setMessage('New lead added')
      await refresh()
      setSelectedLeadId(created.id)
    } catch (error) {
      setMessage(error instanceof Error ? error.message : String(error))
      setLoading(false)
    }
  }
  async function moveLead(leadId: string, status: string) {
    setLoading(true)
    try {
      await changeCrmLeadStatus(leadId, status, status === 'Unqualified' ? 'Not ready now' : undefined)
      setMessage(`Customer enquiry moved to ${statusLabels[status] ?? status}`)
      await refresh()
    } catch (error) {
      setMessage(error instanceof Error ? error.message : String(error))
      setLoading(false)
    }
  }

  async function finishFollowUp(id: string) {
    setLoading(true)
    try {
      await completeCrmFollowUp(id, 'Completed from follow-up centre')
      setMessage('Follow-up completed')
      await refresh()
    } catch (error) {
      setMessage(error instanceof Error ? error.message : String(error)); setLoading(false)
    }
  }

  async function finishTask(id: string) {
    setLoading(true)
    try {
      await completeCrmTask(id)
      setMessage('Task completed')
      await refresh()
    } catch (error) {
      setMessage(error instanceof Error ? error.message : String(error)); setLoading(false)
    }
  }

  useEffect(() => { void refresh() }, [])
  return (
    <div className="crm2-app">
      <aside className="crm2-sidebar">
        <div className="crm2-brand"><div className="crm2-brand-mark">o</div><div><strong>oRRbit</strong><span>BusinessOS CRM</span></div></div>
        <nav className="crm2-nav" aria-label="CRM navigation">
          {can('ViewDashboard') ? <button className={view === 'overview' ? 'active' : ''} onClick={() => setView('overview')}><span>H</span>Home</button> : null}
          {can('ViewLeads') ? <button className={view === 'leads' ? 'active' : ''} onClick={() => setView('leads')}><span>E</span>Customer enquiries <b>{dashboard.totalLeads}</b></button> : null}
          {can('ViewLeads') ? <button className={view === 'pipeline' ? 'active' : ''} onClick={() => setView('pipeline')}><span>P</span>Sales progress</button> : null}
          {can('ViewAccounts') ? <button className={view === 'accounts' ? 'active' : ''} onClick={() => setView('accounts')}><span>C</span>Customers <b>{accounts.length}</b></button> : null}
          {can('ViewOpportunities') ? <button className={view === 'opportunities' ? 'active' : ''} onClick={() => setView('opportunities')}><span>D</span>Deals <b>{opportunities.length}</b></button> : null}
          {can('ManageFollowUps') ? <button className={view === 'followups' ? 'active' : ''} onClick={() => setView('followups')}><span>F</span>Calls / follow-ups <b>{workSummary.openFollowUps}</b></button> : null}
          {can('ManageTasks') ? <button className={view === 'tasks' ? 'active' : ''} onClick={() => setView('tasks')}><span>W</span>Today work <b>{workSummary.openTasks}</b></button> : null}
          {can('ViewReports') ? <button className={view === 'reports' ? 'active' : ''} onClick={() => setView('reports')}><span>R</span>Reports</button> : null}
          {can('ViewTeam') ? <button className={view === 'team' ? 'active' : ''} onClick={() => setView('team')}><span>S</span>Staff <b>{teamMembers.filter((member) => member.active).length}</b></button> : null}
        </nav>
        <div className="crm2-module-nav" aria-label="Advanced CRM modules">
          <span className="crm2-module-title">More tools</span>
          <a href="/crm/advanced">More CRM options</a>
          <a href="/crm/manage">Settings</a>
          <a href="/crm/maintenance">Clean / update data</a>
          <a href="/crm/pipeline-board">Move deals by drag</a>
          <a href="/crm/addresses">Customer addresses</a>
          <a href="/crm/leads-query">Find enquiries</a>
          <a href="/crm/analytics">Detailed reports</a>
          <a href="/crm/opportunity-products">Products in deals</a>
          <a href="/crm/inbox">My day</a>
          <a href="/crm/communications">Calls & messages</a>
          <a href="/crm/intelligence">AI sales help</a>
          <a href="/crm/export">Excel export</a>
          <a href="/crm/contacts">Contact list</a>
          <a href="/crm/deal-aging">Old pending deals</a>
        </div>
        <div className="crm2-sidebar-foot"><strong>BUSINESSOS CRM</strong><small>Simple CRM for daily customer follow-up</small></div>
      </aside>

      <main className="crm2-main">
        <header className="crm2-topbar">
          <div className="crm2-title-block"><span className="crm2-kicker">SIMPLE CRM WORKSPACE</span><h1>{view === 'overview' ? "Today\'s business summary" : view === 'accounts' ? 'Customer list' : view === 'opportunities' ? 'Deals to close' : view === 'followups' ? 'Calls and follow-ups' : view === 'tasks' ? "Today\'s work" : view === 'reports' ? 'Business reports' : view === 'team' ? 'Staff and access' : view === 'pipeline' ? 'Sales progress' : 'Customer enquiries'}</h1><p>Simple flow: add enquiry, call customer, update status, follow up, and close the deal.</p></div>
          <div className="crm2-top-actions">
            {session && teamMembers.length > 0 ? <select className="crm2-user-switch" value={session.member.id} disabled={loading} onChange={(e) => void switchUser(e.target.value)} aria-label="Select staff user">{teamMembers.filter((member) => member.active).map((member) => <option key={member.id} value={member.id}>{member.displayName} · {member.role}</option>)}</select> : null}
            <button className="crm2-refresh" disabled={loading} onClick={refresh}>Refresh</button>
            {can('CreateLead') ? <button className="crm2-primary" onClick={() => setShowAddLead(true)}>+ Add new enquiry</button> : null}
          </div>
        </header>
        <section className="crm2-statusbar"><div><span className={loading ? 'pulse busy' : 'pulse'} />{message}</div><span>{session ? `${session.member.displayName} · ${session.member.role} · ${session.canViewAllOwnedRecords ? 'Team view' : 'My view'} · ` : ''}Testing mode · {storageLabel}</span></section>
        <section className="crm2-layman-guide" aria-label="Start here guide">
          <div><strong>Start here</strong><span>1. Add customer enquiry</span></div>
          <div><strong>Next</strong><span>2. Call and update status</span></div>
          <div><strong>Then</strong><span>3. Set follow-up or close deal</span></div>
        </section>
        {view !== 'overview' ? <section className="crm2-context-strip" aria-label="CRM summary">
          <article><span>Customer enquiries</span><strong>{dashboard.totalLeads}</strong><small>All people who showed interest</small></article>
          <article><span>Calls pending</span><strong>{workSummary.openFollowUps}</strong><small>Customers to call again</small></article>
          <article><span>Work pending</span><strong>{workSummary.openTasks}</strong><small>Today's pending work</small></article>
          <article><span>Conversion</span><strong>{conversionRate}%</strong><small>{dashboard.converted} converted</small></article>
        </section> : null}
        {view === 'overview' ? (
          <>
            <section className="crm2-metrics">
              <article><span>Customer enquiries</span><strong>{dashboard.totalLeads}</strong><small>People interested in your business</small></article>
              <article><span>Talked once</span><strong>{dashboard.contacted}</strong><small>First call/message done</small></article>
              <article><span>Interested</span><strong>{dashboard.qualified}</strong><small>Customers likely to buy</small></article>
              <article className="accent"><span>Converted</span><strong>{conversionRate}%</strong><small>{dashboard.converted} became customers</small></article>
            </section>

            <section className="crm2-action-metrics">
              <button onClick={() => setView('followups')}><span>Calls pending</span><strong>{workSummary.openFollowUps}</strong><small>{workSummary.overdueFollowUps} late / {workSummary.dueTodayFollowUps} today</small></button>
              <button onClick={() => setView('tasks')}><span>Work pending</span><strong>{workSummary.openTasks}</strong><small>{workSummary.overdueTasks} late</small></button>
              <button onClick={() => { setView('leads'); setStatusFilter('Qualified') }}><span>Interested customers</span><strong>{dashboard.qualified}</strong><small>Call now and close faster</small></button>
            </section>

            <section className="crm2-pipeline-card">
              <div className="crm2-section-head"><div><span>SALES PROGRESS</span><h2>Where every enquiry is stuck</h2></div><button onClick={() => setView('pipeline')}>See progress</button></div>
              <div className="crm2-pipeline">
                {pipeline.map((item) => {
                  const width = dashboard.totalLeads > 0 ? Math.max(8, Math.round((item.count / dashboard.totalLeads) * 100)) : 8
                  return <button key={item.status} onClick={() => { setStatusFilter(item.status); setView('leads') }}><div><span>{statusLabels[item.status]}</span><strong>{item.count}</strong></div><i><b style={{ width: `${width}%` }} /></i></button>
                })}
              </div>
            </section>
          </>
        ) : null}
        {view === 'leads' ? (
          <section className="crm2-table-card crm2-module-table">
            <div className="crm2-table-tools">
              <div><span className="crm2-kicker">CUSTOMER ENQUIRIES</span><h2>{session?.canViewAllOwnedRecords ? 'All customer enquiries' : 'My customer enquiries'}</h2><p className="crm2-help-text">Click any row to see details, change status, or plan the next call.</p></div>
              <div className="crm2-filters">
                <input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Search customer, mobile, business..." />
                <select value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)}><option value="All">All stages</option>{statuses.map((status) => <option key={status}>{status}</option>)}</select>
              </div>
            </div>
            <div className="crm2-table-head crm2-rich-head"><span>Customer enquiry</span><span>Contact</span><span>Interest</span><span>Status</span><span>Next call</span><span /></div>
            <div className="crm2-table-body">
              {filteredLeads.length === 0 ? <div className="crm2-empty"><strong>No customer enquiry found</strong><span>Clear the filter or add a new enquiry.</span></div> : filteredLeads.map((lead) => (
                <article className="crm2-row crm2-rich-row" key={lead.id} onClick={() => setSelectedLeadId(lead.id)}>
                  <div className="crm2-lead-name"><i>{lead.title.slice(0, 1).toUpperCase()}</i><span><strong>{lead.title}</strong><small>{lead.leadSource || 'Direct'} - {lead.priority || 'Normal'}</small></span></div>
                  <span><b className="crm2-cell-main">{lead.contactName || '—'}</b><small>{lead.mobileNumber || lead.email || 'Mobile not added'}</small></span>
                  <span>{lead.productInterest || '—'}</span>
                  <span><em className={`crm2-stage stage-${lead.status.toLowerCase()}`}>{statusLabels[lead.status] || lead.status}</em></span>
                  <span className="crm2-created">{formatCreated(lead.nextFollowUpAtUtc)}</span>
                  <button className="crm2-more">›</button>
                </article>
              ))}
            </div>
            <div className="crm2-table-foot">Showing {filteredLeads.length} of {leads.length} leads</div>
          </section>
        ) : null}
        {view === 'pipeline' ? (
          <section className="crm2-kanban-wrap">
            {statuses.map((status) => {
              const stageLeads = leads.filter((lead) => lead.status === status)
              return <div className="crm2-kanban-column" key={status}>
                <header><span>{statusLabels[status]}</span><b>{stageLeads.length}</b></header>
                <div className="crm2-kanban-stack">
                  {stageLeads.length === 0 ? <p>No leads</p> : stageLeads.map((lead) => (
                    <article key={lead.id} onClick={() => setSelectedLeadId(lead.id)}>
                      <div><strong>{lead.title}</strong><em>{lead.priority || 'Normal'}</em></div>
                      <span>{lead.contactName || lead.mobileNumber || lead.leadSource || 'Direct lead'}</span>
                      <small>{lead.productInterest || 'No product selected'}</small>
                      <select value={lead.status} disabled={loading || lead.status === 'Converted' || !can('EditLead')} onClick={(e) => e.stopPropagation()} onChange={(e) => void moveLead(lead.id, e.target.value)}>{statuses.filter((item) => item !== 'Converted' || lead.status === 'Converted').map((item) => <option key={item}>{item}</option>)}</select>
                    </article>
                  ))}
                </div>
              </div>
            })}
          </section>
        ) : null}

        {view === 'accounts' || view === 'opportunities' ? (
          <CrmSalesView view={view} accounts={accounts} opportunities={opportunities} busy={loading} refresh={refresh} notify={setMessage} canManageAccounts={can('ManageAccounts')} canManageOpportunities={can('ManageOpportunities')} />
        ) : null}
        {view === 'followups' || view === 'tasks' || view === 'reports' ? (
          <CrmWorkView view={view} leads={leads} followUps={followUps} tasks={tasks} summary={workSummary} dashboard={dashboard} busy={loading} openLead={setSelectedLeadId} completeFollowUp={finishFollowUp} completeTask={finishTask} />
        ) : null}
        {view === 'team' ? (
          <CrmTeamView members={teamMembers} roles={roles} busy={loading} refresh={refresh} notify={setMessage} canManageTeam={can('ManageTeam')} />
        ) : null}
        {showAddLead ? (
          <div className="crm2-overlay" onMouseDown={() => setShowAddLead(false)}>
            <section className="crm2-drawer crm2-wide-drawer" onMouseDown={(e) => e.stopPropagation()}>
              <div className="crm2-drawer-head"><div><span className="crm2-kicker">NEW LEAD</span><h2>Create lead</h2></div><button onClick={() => setShowAddLead(false)}>×</button></div>
              <div className="crm2-form-grid">
                <label>Business / lead name<input autoFocus value={title} onChange={(e) => setTitle(e.target.value)} placeholder="e.g. Sharma Mobile Care" /></label>
                <label>Contact person<input value={contactName} onChange={(e) => setContactName(e.target.value)} placeholder="Owner / decision maker" /></label>
                <label>Mobile<input value={mobile} onChange={(e) => setMobile(e.target.value)} placeholder="10-digit mobile" /></label>
                <label>Email<input type="email" value={email} onChange={(e) => setEmail(e.target.value)} /></label>
                <label>Lead source<select value={source} onChange={(e) => setSource(e.target.value)}><option>WhatsApp</option><option>Website</option><option>Referral</option><option>Partner</option><option>Calling</option><option>Facebook</option><option>Instagram</option><option>Other</option></select></label>
                <label>Priority<select value={priority} onChange={(e) => setPriority(e.target.value)}><option>Low</option><option>Normal</option><option>High</option><option>Urgent</option></select></label>
              </div>
              <label>Product interest<input value={product} onChange={(e) => setProduct(e.target.value)} placeholder="Product / service" /></label>
              <label>Initial notes<textarea value={notes} onChange={(e) => setNotes(e.target.value)} rows={4} placeholder="Requirement, budget, next action…" /></label>
              <div className="crm2-drawer-note"><strong>Starts in New</strong><span>After saving, the full lead workspace opens automatically for follow-up, task and activity entry.</span></div>
              <div className="crm2-drawer-actions"><button className="crm2-cancel" onClick={() => setShowAddLead(false)}>Cancel</button><button className="crm2-primary" disabled={loading || !title.trim()} onClick={() => void addLead()}>{loading ? 'Adding…' : 'Create lead'}</button></div>
            </section>
          </div>
        ) : null}
        {selectedLeadId ? (
          <CrmLeadDrawer leadId={selectedLeadId} teamMembers={teamMembers} onClose={() => setSelectedLeadId(null)} onChanged={refresh} notify={setMessage} permissions={session?.member.permissions ?? []} currentUserId={session?.member.id} />
        ) : null}
      </main>
    </div>
  )
}
