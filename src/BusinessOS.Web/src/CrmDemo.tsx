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

type CrmView = 'overview' | 'leads' | 'pipeline' | 'accounts' | 'sales' | 'subscriptions' | 'expenses' | 'contracts' | 'projects' | 'support' | 'estimateRequests' | 'knowledgeBase' | 'utilities' | 'opportunities' | 'followups' | 'tasks' | 'reports' | 'team'
const statuses = ['New', 'Contacted', 'Qualified', 'Converted', 'Unqualified']
const statusLabels: Record<string, string> = { New: 'New enquiry', Contacted: 'Talked once', Qualified: 'Interested customer', Converted: 'Became customer', Unqualified: 'Not interested now' }

const referenceModuleContent: Record<string, { title: string; subtitle: string; actions: string[]; columns: string[]; rows: string[][] }> = {
  sales: {
    title: 'Sales', subtitle: 'Create and track proposals, estimates, invoices, payments, credit notes and items.',
    actions: ['New Proposal', 'New Estimate', 'New Invoice', 'Record Payment', 'Add Item'],
    columns: ['Document', 'Customer', 'Amount', 'Status', 'Date'],
    rows: [['INV-000490', 'Existing customer', '₹41,300.00', 'Due', '08 Sep 2026'], ['EST-000128', 'New enquiry', '₹9,999.00', 'Draft', 'Today']]
  },
  subscriptions: {
    title: 'Subscriptions', subtitle: 'Manage recurring software plans, renewal dates, billing cycles and active customers.',
    actions: ['New Subscription', 'Renew Subscription', 'Export'],
    columns: ['Customer', 'Plan', 'Renewal', 'Status', 'Owner'],
    rows: [['Bismi mobiles', 'AI Repair Pro', 'Annual', 'Active', 'Sales Owner'], ['Perfect Solutions', 'BusinessOS CRM', 'Monthly', 'Trial', 'CRM Owner']]
  },
  expenses: {
    title: 'Expenses', subtitle: 'Record office expenses, sales expenses, staff expenses and vendor payments.',
    actions: ['Add Expense', 'Import', 'Export'],
    columns: ['Expense', 'Category', 'Amount', 'Paid By', 'Date'],
    rows: [['Calling recharge', 'Sales', '₹799.00', 'Office', 'Today'], ['Demo travel', 'Business', '₹1,250.00', 'Staff', 'Yesterday']]
  },
  contracts: {
    title: 'Contracts', subtitle: 'Keep signed agreements, service contracts, AMC documents and renewal commitments.',
    actions: ['New Contract', 'Upload Document', 'Export'],
    columns: ['Contract', 'Customer', 'Start Date', 'End Date', 'Status'],
    rows: [['AMC-2026-001', 'A2ZTECH.IN', '01 Sep 2026', '31 Aug 2027', 'Active']]
  },
  projects: {
    title: 'Projects', subtitle: 'Track implementation, onboarding, customisation, delivery and internal project work.',
    actions: ['New Project', 'Assign Staff', 'Export'],
    columns: ['Project', 'Customer', 'Owner', 'Progress', 'Status'],
    rows: [['CRM onboarding', 'Bhilai Public School', 'Support Team', '45%', 'In progress']]
  },
  support: {
    title: 'Support', subtitle: 'Handle customer complaints, service tickets, help requests and pending support calls.',
    actions: ['New Ticket', 'Assign Ticket', 'Export'],
    columns: ['Ticket', 'Customer', 'Issue', 'Priority', 'Status'],
    rows: [['SUP-00041', 'BOYFRIEND SPORTSWEAR', 'Invoice help', 'Normal', 'Open']]
  },
  estimateRequests: {
    title: 'Estimate Request', subtitle: 'Collect website/WhatsApp estimate requests and convert them into enquiries or estimates.',
    actions: ['Review Request', 'Convert to Enquiry', 'Export'],
    columns: ['Email', 'Requirement', 'Assigned', 'Status', 'Created'],
    rows: [['demo@example.com', 'Repair CRM pricing', 'Sales Owner', 'New', 'Today']]
  },
  knowledgeBase: {
    title: 'Knowledge Base', subtitle: 'Store FAQs, training notes, sales answers, onboarding guides and support articles.',
    actions: ['New Article', 'New Category', 'Export'],
    columns: ['Article', 'Category', 'Owner', 'Visibility', 'Updated'],
    rows: [['How to follow up a lead', 'Sales Training', 'Admin', 'Team', 'Today']]
  },
  utilities: {
    title: 'Utilities', subtitle: 'Manage media files, imports, exports and helper tools used by the CRM team.',
    actions: ['Open Media', 'Import File', 'Export Data'],
    columns: ['Utility', 'Purpose', 'Owner', 'Status', 'Last Used'],
    rows: [['Media', 'Files and attachments', 'Admin', 'Ready', 'Today']]
  }
}
function initialDashboard(): CrmDashboard {
  return { totalLeads: 0, new: 0, contacted: 0, qualified: 0, converted: 0, unqualified: 0, statusCounts: {} }
}

function initialWorkSummary(): CrmWorkSummary {
  return { openFollowUps: 0, overdueFollowUps: 0, dueTodayFollowUps: 0, openTasks: 0, overdueTasks: 0 }
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
      setMessage('New customer enquiry added')
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
        <div className="crm2-brand crm2-ref-brand"><div className="crm2-ref-tree"><i></i><i></i><i></i><i></i><b></b></div></div>
        <nav className="crm2-nav crm2-reference-nav" aria-label="CRM navigation">
          <button className={view === 'overview' ? 'active' : ''} onClick={() => setView('overview')}><span>⌂</span>Dashboard</button>
          <button className={view === 'accounts' ? 'active' : ''} onClick={() => setView('accounts')}><span>○</span>Customers <b>{accounts.length}</b></button>
          <button className={view === 'sales' ? 'active' : ''} onClick={() => setView('sales')}><span>▣</span>Sales</button>
          <button className={view === 'subscriptions' ? 'active' : ''} onClick={() => setView('subscriptions')}><span>↻</span>Subscriptions</button>
          <button className={view === 'expenses' ? 'active' : ''} onClick={() => setView('expenses')}><span>□</span>Expenses</button>
          <button className={view === 'contracts' ? 'active' : ''} onClick={() => setView('contracts')}><span>▤</span>Contracts</button>
          <button className={view === 'projects' ? 'active' : ''} onClick={() => setView('projects')}><span>⌙</span>Projects</button>
          <button className={view === 'tasks' ? 'active' : ''} onClick={() => setView('tasks')}><span>✓</span>Tasks <b>{workSummary.openTasks}</b></button>
          <button className={view === 'support' ? 'active' : ''} onClick={() => setView('support')}><span>◎</span>Support</button>
          <button className={view === 'leads' ? 'active' : ''} onClick={() => setView('leads')}><span>▥</span>Leads <b>{dashboard.totalLeads}</b></button>
          <button className={view === 'estimateRequests' ? 'active' : ''} onClick={() => setView('estimateRequests')}><span>◇</span>Estimate Request</button>
          <button className={view === 'knowledgeBase' ? 'active' : ''} onClick={() => setView('knowledgeBase')}><span>▭</span>Knowledge Base</button>
          <button className={view === 'utilities' ? 'active' : ''} onClick={() => setView('utilities')}><span>⚙</span>Utilities</button>
          <button className={view === 'reports' ? 'active' : ''} onClick={() => setView('reports')}><span>≡</span>Reports</button>
        </nav>
        <div className="crm2-module-nav" aria-label="Advanced CRM modules">
          <span className="crm2-module-title">More tools</span>
          <a href="/crm/advanced">More options</a>
          <a href="/crm/manage">Software settings</a>
          <a href="/crm/maintenance">Clean / update data</a>
          <a href="/crm/pipeline-board">Move deals by drag</a>
          <a href="/crm/addresses">Customer addresses</a>
          <a href="/crm/leads-query">Search enquiries</a>
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
        <header className="crm2-topbar crm2-ref-topbar">
          <button className="crm2-ref-menu" aria-label="Toggle menu">☰</button>
          <label className="crm2-ref-search"><input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Search..." /><span>⌕</span></label>
          <button className="crm2-ref-plus" onClick={() => setShowAddLead(true)} aria-label="Add new">+</button>
          <div className="crm2-ref-toolbar-spacer" />
          <button className="crm2-ref-icon" title="Share">⌯</button>
          <button className="crm2-ref-icon" title="Tasks">✓</button>
          <button className="crm2-ref-avatar" title={session?.member.displayName ?? 'User'} onDoubleClick={() => session ? void switchUser(session.member.id) : undefined}></button>
          <button className="crm2-ref-icon" title="Timer">◷</button>
          <button className="crm2-ref-icon crm2-ref-bell" title="Notifications">♢<b>1</b></button>
        </header>
        <div className="crm2-ref-options"><button>⚙ Dashboard Options</button></div>
        <section className="crm2-statusbar"><div><span className={loading ? 'pulse busy' : 'pulse'} />{message}</div><span>{session ? `${session.member.displayName} · ${session.member.role} · ${session.canViewAllOwnedRecords ? 'Team view' : 'My view'} · ` : ''}Testing mode · {storageLabel}</span></section>
        <section className="crm2-workflow-board" aria-label="Simple working process">
          <div className="crm2-workflow-title">
            <span>Daily working process</span>
            <strong>Follow these 5 steps only</strong>
            <p>This is made for a normal business owner or staff. No technical knowledge is needed.</p>
          </div>
          <button onClick={() => setShowAddLead(true)}><b>1</b><strong>Add enquiry</strong><span>Enter customer name, mobile and requirement.</span></button>
          <button onClick={() => { setView('leads'); setStatusFilter('New') }}><b>2</b><strong>Call customer</strong><span>Open new enquiries and call them today.</span></button>
          <button onClick={() => { setView('pipeline'); setStatusFilter('Contacted') }}><b>3</b><strong>Move progress</strong><span>Mark called, interested, won or not interested.</span></button>
          <button onClick={() => setView('followups')}><b>4</b><strong>Set reminder</strong><span>Plan next call so no customer is missed.</span></button>
          <button onClick={() => { setView('leads'); setStatusFilter('Qualified') }}><b>5</b><strong>Close deal</strong><span>Focus on interested customers first.</span></button>
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

            <section className="crm2-reference-calendar">
              <div className="crm2-calendar-toolbar">
                <div className="crm2-calendar-left"><button>‹</button><button>›</button><button>Today</button><button>Expand</button></div>
                <h2>September 2026</h2>
                <div className="crm2-calendar-right"><button className="active">Month</button><button>Week</button><button>Day</button><button>Filter By</button></div>
              </div>
              <div className="crm2-calendar-grid">
                {['Mon','Tue','Wed','Thu','Fri','Sat','Sun'].map(day => <strong key={day}>{day}</strong>)}
                {Array.from({ length: 35 }).map((_, index) => {
                  const label = index === 0 ? '31' : String(index)
                  return <article key={index} className={label === '18' ? 'today' : ''}><span>{label}</span>{label === '8' ? <em>INV-000490</em> : null}</article>
                })}
              </div>
            </section>
          </>
        ) : null}
        {view === 'leads' ? (
          <section className="crm2-ref-list-page">
            <div className="crm2-ref-action-row">
              <button className="crm2-ref-primary" onClick={() => setShowAddLead(true)}>+ New Lead</button>
              <button className="crm2-ref-primary" onClick={() => setMessage('Import leads is scheduled for the next backend block')}>Import Leads</button>
              <button className="crm2-ref-square active">☰</button>
              <button className="crm2-ref-square">▦</button>
            </div>
            <section className="crm2-ref-filter-card">
              <strong>Filter by</strong>
              <div className="crm2-ref-filter-grid">
                <select><option>Assigned</option><option>CRM Owner</option><option>Sales QA</option></select>
                <select value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)}><option value="All">New Lead, Follow Up, Demo...</option>{statuses.map((status) => <option key={status} value={status}>{statusLabels[status] || status}</option>)}</select>
                <select><option>Source</option><option>WhatsApp</option><option>Website</option><option>Calling</option><option>Referral</option></select>
                <select><option>Additional Filters</option><option>High priority</option><option>With mobile</option><option>With next follow-up</option></select>
              </div>
            </section>
            <section className="crm2-ref-table-card">
              <div className="crm2-ref-table-tools"><select><option>25</option><option>50</option></select><button>Export</button><button>Bulk Actions</button><button onClick={refresh}>Refresh</button><span /><label><b>⌕</b><input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Search..." /></label></div>
              <div className="crm2-ref-leads-head"><span><input type="checkbox" /></span><span>#</span><span>Name</span><span>Company</span><span>Email</span><span>Phone</span><span>Value</span><span>Tags</span><span>Assigned</span><span>Status</span></div>
              {filteredLeads.length === 0 ? <p className="crm2-reference-empty">No entries found</p> : filteredLeads.map((lead, index) => (
                <article className="crm2-ref-leads-row" key={lead.id} onClick={() => setSelectedLeadId(lead.id)}>
                  <span><input type="checkbox" onClick={(e) => e.stopPropagation()} /></span><span>{1261 - index}</span><span><a>{lead.contactName || lead.title}</a></span><span>{lead.title}</span><span>{lead.email || '-'}</span><span>{lead.mobileNumber || '-'}</span><span>{lead.productInterest ? '₹29,999.00' : '-'}</span><span><em>{lead.priority || 'Normal'}</em></span><span><i className="crm2-ref-avatar-mini">{(lead.contactName || lead.title).slice(0,1).toUpperCase()}</i></span><span><select value={lead.status} onClick={(e) => e.stopPropagation()} onChange={(e) => void moveLead(lead.id, e.target.value)}>{statuses.map((status) => <option key={status}>{status}</option>)}</select></span>
                </article>
              ))}
            </section>
          </section>
        ) : null}
        {referenceModuleContent[view] ? (
          <section className="crm2-reference-module">
            <div className="crm2-reference-module-head">
              <div><span className="crm2-kicker">BUSINESS MODULE</span><h2>{referenceModuleContent[view].title}</h2><p>{referenceModuleContent[view].subtitle}</p></div>
              <button className="crm2-filter-button">Filter</button>
            </div>
            <div className="crm2-reference-actions">
              {referenceModuleContent[view].actions.map(action => <button key={action}>{action}</button>)}
            </div>
            <div className="crm2-reference-table">
              <div className="crm2-reference-table-tools"><select><option>25</option><option>50</option></select><button>Export</button><button>Bulk Actions</button><button>Refresh</button><span /><label><b>⌕</b><input placeholder="Search..." /></label></div>
              <div className="crm2-reference-head">{referenceModuleContent[view].columns.map(column => <span key={column}>{column}</span>)}</div>
              {referenceModuleContent[view].rows.length === 0 ? <p className="crm2-reference-empty">No entries found</p> : referenceModuleContent[view].rows.map((row, rowIndex) => <div className="crm2-reference-row" key={rowIndex}>{row.map((cell, index) => <span key={`${rowIndex}-${index}`}>{cell}</span>)}</div>)}
            </div>
          </section>
        ) : null}
        {view === 'pipeline' ? (
          <section className="crm2-kanban-wrap">
            {statuses.map((status) => {
              const stageLeads = leads.filter((lead) => lead.status === status)
              return <div className="crm2-kanban-column" key={status}>
                <header><span>{statusLabels[status]}</span><b>{stageLeads.length}</b></header>
                <div className="crm2-kanban-stack">
                  {stageLeads.length === 0 ? <p>No enquiries</p> : stageLeads.map((lead) => (
                    <article key={lead.id} onClick={() => setSelectedLeadId(lead.id)}>
                      <div><strong>{lead.title}</strong><em>{lead.priority || 'Normal'}</em></div>
                      <span>{lead.contactName || lead.mobileNumber || lead.leadSource || 'Direct enquiry'}</span>
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
              <div className="crm2-drawer-head"><div><span className="crm2-kicker">NEW CUSTOMER ENQUIRY</span><h2>Add enquiry</h2><p>Fill only basic customer details. Call notes and reminders can be added later.</p></div><button onClick={() => setShowAddLead(false)}>×</button></div>
              <div className="crm2-form-grid">
                <label>Customer / business name<input autoFocus value={title} onChange={(e) => setTitle(e.target.value)} placeholder="e.g. Sharma Mobile Care" /></label>
                <label>Contact person<input value={contactName} onChange={(e) => setContactName(e.target.value)} placeholder="Owner / decision maker" /></label>
                <label>Mobile<input value={mobile} onChange={(e) => setMobile(e.target.value)} placeholder="10-digit mobile" /></label>
                <label>Email<input type="email" value={email} onChange={(e) => setEmail(e.target.value)} /></label>
                <label>Enquiry source<select value={source} onChange={(e) => setSource(e.target.value)}><option>WhatsApp</option><option>Website</option><option>Referral</option><option>Partner</option><option>Calling</option><option>Facebook</option><option>Instagram</option><option>Other</option></select></label>
                <label>Priority<select value={priority} onChange={(e) => setPriority(e.target.value)}><option>Low</option><option>Normal</option><option>High</option><option>Urgent</option></select></label>
              </div>
              <label>Product interest<input value={product} onChange={(e) => setProduct(e.target.value)} placeholder="Product / service" /></label>
              <label>Initial notes<textarea value={notes} onChange={(e) => setNotes(e.target.value)} rows={4} placeholder="What customer wants, budget, and what to do next." /></label>
              <div className="crm2-drawer-note"><strong>After saving</strong><span>This enquiry will appear in Customer enquiries. Open it to add call note, reminder or deal status.</span></div>
              <div className="crm2-drawer-actions"><button className="crm2-cancel" onClick={() => setShowAddLead(false)}>Cancel</button><button className="crm2-primary" disabled={loading || !title.trim()} onClick={() => void addLead()}>{loading ? 'Saving...' : 'Save enquiry'}</button></div>
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
