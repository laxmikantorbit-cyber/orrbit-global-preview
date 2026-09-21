import { useEffect, useMemo, useState } from 'react'
import './CrmDemo.css'
import { readiness } from './businessosApi'
import {
  assignCrmLead,
  bulkUpdateCrmLeads,
  changeCrmLeadStatus,
  completeCrmFollowUp,
  completeCrmTask,
  createCrmLead,
  crmDashboard,
  crmWorkSummary,
  getCrmSession,
  globalCrmSearch,
  getCrmNotifications,
  setCrmDemoUserId,
  updateCrmLeadTag,
  listCrmAccounts,
  listCrmFollowUps,
  listCrmInvoices,
  listCrmCreditNotes,
  listCrmSalesItems,
  listCrmLeads,
  listCrmOpportunities,
  listCrmRoles,
  listCrmSalesDocuments,
  listCrmTasks,
  listCrmTeam,
  type CrmAccount,
  type CrmDashboard,
  type CrmFollowUp,
  type CrmGlobalSearchHit,
  type CrmNotificationItem,
  type CrmInvoice,
  type CrmCreditNote,
  type CrmSalesItem,
  type CrmLead,
  type CrmOpportunity,
  type CrmRole,
  type CrmSalesDocument,
  type CrmSession,
  type CrmTask,
  type CrmTeamMember,
  type CrmWorkSummary,
} from './crmApi'
import { CrmLeadDrawer } from './CrmLeadDrawer'
import { CrmWorkView } from './CrmWorkView'
import { CrmSalesView } from './CrmSalesView'
import { CrmSalesWorkspace } from './CrmSalesWorkspace'
import { CrmTeamView } from './CrmTeamView'
import { exportCrmSpreadsheet, pickCrmSpreadsheet, type CrmSpreadsheetFormat } from './crmSpreadsheet'

type CrmView = 'overview' | 'leads' | 'pipeline' | 'accounts' | 'sales' | 'subscriptions' | 'expenses' | 'contracts' | 'projects' | 'support' | 'estimateRequests' | 'knowledgeBase' | 'utilities' | 'opportunities' | 'followups' | 'tasks' | 'reports' | 'team'
const statuses = ['New', 'Contacted', 'Qualified', 'Converted', 'Unqualified']
const statusLabels: Record<string, string> = { New: 'New enquiry', Contacted: 'Talked once', Qualified: 'Interested customer', Converted: 'Became customer', Unqualified: 'Not interested now' }

function formatLeadValue(value?: number | null) {
  if (value == null) return '—'
  return new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR', maximumFractionDigits: 0 }).format(value)
}

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
  const [salesDocuments, setSalesDocuments] = useState<CrmSalesDocument[]>([])
  const [invoices, setInvoices] = useState<CrmInvoice[]>([])
  const [salesItems, setSalesItems] = useState<CrmSalesItem[]>([])
  const [creditNotes, setCreditNotes] = useState<CrmCreditNote[]>([])
  const [teamMembers, setTeamMembers] = useState<CrmTeamMember[]>([])
  const [roles, setRoles] = useState<CrmRole[]>([])
  const [session, setSession] = useState<CrmSession | null>(null)
  const [dashboard, setDashboard] = useState<CrmDashboard>(initialDashboard())
  const [workSummary, setWorkSummary] = useState<CrmWorkSummary>(initialWorkSummary())
  const [message, setMessage] = useState('CRM workspace ready')
  const [storageLabel, setStorageLabel] = useState('Staging data')
  const [loading, setLoading] = useState(false)
  const [query, setQuery] = useState('')
  const [globalQuery, setGlobalQuery] = useState('')
  const [globalHits, setGlobalHits] = useState<CrmGlobalSearchHit[]>([])
  const [globalSearching, setGlobalSearching] = useState(false)
  const [notifications, setNotifications] = useState<CrmNotificationItem[]>([])
  const [showNotifications, setShowNotifications] = useState(false)
  const [statusFilter, setStatusFilter] = useState('All')
  const [sourceFilter, setSourceFilter] = useState('All')
  const [assignedFilter, setAssignedFilter] = useState('All')
  const [extraFilter, setExtraFilter] = useState('All')
  const [tagFilter, setTagFilter] = useState('All')
  const [leadViewMode, setLeadViewMode] = useState<'list' | 'grid'>('list')
  const [selectedLeadIds, setSelectedLeadIds] = useState<string[]>([])
  const [bulkStatus, setBulkStatus] = useState('')
  const [bulkPriority, setBulkPriority] = useState('')
  const [bulkOwnerId, setBulkOwnerId] = useState('')
  const [showAddLead, setShowAddLead] = useState(false)
  const [selectedLeadId, setSelectedLeadId] = useState<string | null>(null)
  const [title, setTitle] = useState('')
  const [source, setSource] = useState('WhatsApp')
  const [contactName, setContactName] = useState('')
  const [mobile, setMobile] = useState('')
  const [email, setEmail] = useState('')
  const [product, setProduct] = useState('AI Repair Business Management Software')
  const [priority, setPriority] = useState('Normal')
  const [leadValue, setLeadValue] = useState('')
  const [notes, setNotes] = useState('')

  const pipeline = useMemo(() => statuses.map((status) => ({
    status,
    count: leads.filter((lead) => lead.status === status).length,
  })), [leads])
  const leadSources = useMemo(
    () => Array.from(new Set(leads.map((lead) => lead.leadSource).filter((value): value is string => !!value))).sort((a, b) => a.localeCompare(b)),
    [leads],
  )
  const leadTags = useMemo(
    () => Array.from(new Set(leads.flatMap((lead) => lead.tags || []))).sort((a, b) => a.localeCompare(b)),
    [leads],
  )

  const filteredLeads = useMemo(() => {
    const search = query.trim().toLowerCase()
    return leads.filter((lead) => {
      const matchesStatus = statusFilter === 'All' || lead.status === statusFilter
      const matchesSource = sourceFilter === 'All' || (lead.leadSource || '').toLowerCase() === sourceFilter.toLowerCase()
      const matchesAssigned = assignedFilter === 'All' || (assignedFilter === 'Unassigned' ? !lead.ownerUserId : lead.ownerUserId === assignedFilter)
      const matchesTag = tagFilter === 'All' || (lead.tags || []).some((tag) => tag.toLowerCase() === tagFilter.toLowerCase())
      const matchesExtra = extraFilter === 'All'
        || (extraFilter === 'HighPriority' && ['High', 'Urgent'].includes(lead.priority || ''))
        || (extraFilter === 'WithMobile' && !!lead.mobileNumber)
        || (extraFilter === 'WithEmail' && !!lead.email)
        || (extraFilter === 'WithNextFollowUp' && !!lead.nextFollowUpAtUtc)
        || (extraFilter === 'WithValue' && Number(lead.estimatedValue || 0) > 0)
        || (extraFilter === 'Unassigned' && !lead.ownerUserId)
      const haystack = [lead.title, lead.leadSource, lead.contactName, lead.mobileNumber, lead.email, lead.productInterest, lead.priority, ...(lead.tags || [])].filter(Boolean).join(' ').toLowerCase()
      return matchesStatus && matchesSource && matchesAssigned && matchesTag && matchesExtra && (!search || haystack.includes(search))
    })
  }, [leads, query, statusFilter, sourceFilter, assignedFilter, tagFilter, extraFilter])

  const conversionRate = dashboard.totalLeads > 0 ? Math.round((dashboard.converted / dashboard.totalLeads) * 100) : 0
  const can = (permission: string) => session?.member.permissions.includes(permission) ?? false

  async function exportLeads(format: CrmSpreadsheetFormat) {
    await exportCrmSpreadsheet('crm-leads', {
      headers: ['Name', 'Company', 'Email', 'Phone', 'Value', 'Source', 'Status', 'Priority', 'Product', 'Tags', 'Assigned', 'Next Follow-up'],
      rows: filteredLeads.map((lead) => {
        const owner = teamMembers.find((member) => member.id === lead.ownerUserId)
        return [
          lead.contactName || lead.title, lead.title, lead.email || '', lead.mobileNumber || '',
          lead.estimatedValue ?? '', lead.leadSource || '', lead.status, lead.priority || '',
          lead.productInterest || '', (lead.tags || []).join('; '),
          owner?.email || owner?.displayName || '', lead.nextFollowUpAtUtc || '',
        ]
      }),
    }, format)
    setMessage(`Exported ${filteredLeads.length} lead(s) to ${format === 'xlsx' ? 'Excel' : 'CSV'}`)
  }

  function importLeads() {
    pickCrmSpreadsheet((sheetRows, fileName) => {
      const [header, ...rows] = sheetRows
      if (!header || rows.length === 0) { setMessage('Spreadsheet has no lead rows'); return }
      const keys = header.map((cell) => cell.toLowerCase().replace(/[^a-z0-9]/g, ''))
      const value = (row: string[], names: string[]) => {
        const index = names.map((name) => keys.indexOf(name)).find((item) => item >= 0) ?? -1
        return index >= 0 ? row[index] : ''
      }
      void (async () => {
        setLoading(true)
        let created = 0
        let failed = 0
        try {
          for (const row of rows) {
            const company = value(row, ['company', 'business', 'name', 'customer', 'lead'])
            const person = value(row, ['contact', 'contactperson', 'person'])
            const phone = value(row, ['phone', 'mobile', 'mobilenumber'])
            const mail = value(row, ['email', 'mail'])
            if (!company && !person && !phone && !mail) continue
            try {
              const rawValue = value(row, ['value', 'leadvalue', 'estimatedvalue', 'amount']).replace(/[^0-9.-]/g, '')
              const parsedValue = rawValue ? Number(rawValue) : null
              const lead = await createCrmLead({
                title: company || person || phone || mail,
                contactName: person || undefined,
                mobileNumber: phone || undefined,
                email: mail || undefined,
                leadSource: value(row, ['source', 'leadsource']) || 'Import',
                productInterest: value(row, ['product', 'requirement', 'interest']) || undefined,
                priority: value(row, ['priority']) || 'Normal',
                notes: value(row, ['notes', 'remark', 'remarks']) || undefined,
                estimatedValue: parsedValue != null && Number.isFinite(parsedValue) && parsedValue >= 0 ? parsedValue : null,
              })
              const assigned = value(row, ['assigned', 'assignee', 'assignedto', 'owner']).trim().toLowerCase()
              const owner = assigned ? teamMembers.find((member) => member.active && [member.id, member.email, member.displayName].some((item) => item.toLowerCase() === assigned)) : undefined
              if (owner && can('AssignLead')) await assignCrmLead(lead.id, owner.id)
              const importedStatus = value(row, ['status', 'leadstatus'])
              if (importedStatus && importedStatus !== 'New' && importedStatus !== 'Converted' && statuses.includes(importedStatus) && can('EditLead')) {
                await changeCrmLeadStatus(lead.id, importedStatus, importedStatus === 'Unqualified' ? 'Imported as unqualified' : undefined)
              }
              if (can('EditLead')) {
                for (const tag of value(row, ['tags', 'tag']).split(/[;,|]/).map((item) => item.trim()).filter(Boolean)) {
                  await updateCrmLeadTag(lead.id, tag)
                }
              }
              created += 1
            } catch { failed += 1 }
          }
          setMessage(`Imported ${created} lead(s) from ${fileName}${failed ? `; ${failed} failed` : ''}`)
          await refresh()
        } catch (error) {
          setMessage(error instanceof Error ? error.message : String(error))
        } finally { setLoading(false) }
      })()
    }, setMessage)
  }

  function toggleSelectedLead(id: string, checked: boolean) {
    setSelectedLeadIds((ids) => checked ? Array.from(new Set([...ids, id])) : ids.filter((item) => item !== id))
  }

  function openSearchHit(hit: CrmGlobalSearchHit) {
    setGlobalHits([])
    setGlobalQuery('')
    if (hit.type === 'Lead') { setView('leads'); setSelectedLeadId(hit.id); return }
    if (hit.type === 'Account') { setView('accounts'); return }
    if (hit.type === 'Opportunity') { setView('opportunities'); return }
  }

  async function applyBulkLeadUpdate() {
    if (selectedLeadIds.length === 0) { setMessage('Select at least one lead first'); return }
    if (!bulkStatus && !bulkPriority && !bulkOwnerId) { setMessage('Choose a bulk status, priority or assignee first'); return }
    setLoading(true)
    try {
      const result = await bulkUpdateCrmLeads({
        leadIds: selectedLeadIds,
        status: bulkStatus || undefined,
        priority: bulkPriority || undefined,
        changeOwner: !!bulkOwnerId,
        ownerUserId: bulkOwnerId === '__unassigned__' ? null : bulkOwnerId || undefined,
        note: 'Updated from CRM lead bulk action',
      })
      setMessage(`Bulk updated ${result.updatedLeadIds.length} lead(s)${result.failed.length ? `, ${result.failed.length} failed` : ''}`)
      setSelectedLeadIds([]); setBulkStatus(''); setBulkPriority(''); setBulkOwnerId('')
      await refresh()
    } catch (error) {
      setMessage(error instanceof Error ? error.message : String(error)); setLoading(false)
    }
  }


  async function openNotifications() {
    try {
      const result = await getCrmNotifications()
      setNotifications(result.items)
      setShowNotifications((value) => !value)
      setMessage(`Loaded ${result.items.length} notification(s)`)
    } catch (error) {
      setMessage(error instanceof Error ? error.message : String(error))
    }
  }

  async function refresh() {
    setLoading(true)
    try {
      const sessionResult = await getCrmSession()
      setSession(sessionResult)
      const allowed = (permission: string) => sessionResult.member.permissions.includes(permission)
      const [leadResult, dashResult, followResult, taskResult, workResult, accountResult, opportunityResult, salesResult, invoiceResult, salesItemResult, creditNoteResult, teamResult, roleResult, readyResult] = await Promise.all([
        allowed('ViewLeads') ? listCrmLeads() : Promise.resolve({ leads: [] as CrmLead[] }),
        allowed('ViewDashboard') ? crmDashboard() : Promise.resolve(initialDashboard()),
        allowed('ManageFollowUps') ? listCrmFollowUps() : Promise.resolve({ followUps: [] as CrmFollowUp[] }),
        allowed('ManageTasks') ? listCrmTasks() : Promise.resolve({ tasks: [] as CrmTask[] }),
        allowed('ViewDashboard') ? crmWorkSummary() : Promise.resolve(initialWorkSummary()),
        allowed('ViewAccounts') ? listCrmAccounts() : Promise.resolve({ accounts: [] as CrmAccount[] }),
        allowed('ViewOpportunities') ? listCrmOpportunities() : Promise.resolve({ opportunities: [] as CrmOpportunity[] }),
        allowed('ViewSales') ? listCrmSalesDocuments() : Promise.resolve({ documents: [] as CrmSalesDocument[] }),
        allowed('ViewSales') ? listCrmInvoices() : Promise.resolve({ invoices: [] as CrmInvoice[] }),
        allowed('ViewSales') ? listCrmSalesItems() : Promise.resolve({ items: [] as CrmSalesItem[] }),
        allowed('ViewSales') ? listCrmCreditNotes() : Promise.resolve({ creditNotes: [] as CrmCreditNote[] }),
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
      setSalesDocuments(salesResult.documents)
      setInvoices(invoiceResult.invoices)
      setSalesItems(salesItemResult.items)
      setCreditNotes(creditNoteResult.creditNotes)
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
        estimatedValue: leadValue.trim() ? Number(leadValue) : null,
      })
      setShowAddLead(false)
      setTitle(''); setContactName(''); setMobile(''); setEmail(''); setLeadValue(''); setNotes('')
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

  async function reassignLead(leadId: string, ownerUserId: string | null) {
    setLoading(true)
    try {
      await assignCrmLead(leadId, ownerUserId)
      setMessage(ownerUserId ? 'Lead assignee updated' : 'Lead unassigned')
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

  useEffect(() => {
    const search = globalQuery.trim()
    if (search.length < 2) { setGlobalHits([]); return }
    let cancelled = false
    setGlobalSearching(true)
    const handle = window.setTimeout(() => {
      void globalCrmSearch(search)
        .then((result) => { if (!cancelled) setGlobalHits(result.results) })
        .catch((error) => { if (!cancelled) setMessage(error instanceof Error ? error.message : String(error)) })
        .finally(() => { if (!cancelled) setGlobalSearching(false) })
    }, 350)
    return () => { cancelled = true; window.clearTimeout(handle) }
  }, [globalQuery])

  return (
    <div className="crm2-app">
      <aside className="crm2-sidebar">
        <div className="crm2-brand crm2-ref-brand"><div className="crm2-ref-tree"><i></i><i></i><i></i><i></i><b></b></div></div>
        <nav className="crm2-nav crm2-reference-nav" aria-label="CRM navigation">
          <button className={view === 'overview' ? 'active' : ''} onClick={() => setView('overview')}><span>⌂</span>Dashboard</button>
          <button className={view === 'accounts' ? 'active' : ''} onClick={() => setView('accounts')}><span>○</span>Customers <b>{accounts.length}</b></button>
          {can('ViewSales') ? <button className={view === 'sales' ? 'active' : ''} onClick={() => setView('sales')}><span>▣</span>Sales <b>{salesDocuments.length}</b></button> : null}
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
          <label className="crm2-ref-search"><input value={globalQuery} onChange={(e) => setGlobalQuery(e.target.value)} placeholder="Search customers, leads, opportunities..." /><span>{globalSearching ? '...' : '⌕'}</span></label>
          {can('CreateLead') ? <button className="crm2-ref-plus" onClick={() => setShowAddLead(true)} aria-label="Add new lead">+</button> : null}
          <div className="crm2-ref-toolbar-spacer" />
          <button className="crm2-ref-icon" title="Export leads CSV" onClick={() => void exportLeads('csv')}>⌯</button>
          <button className="crm2-ref-icon" title="Tasks" onClick={() => setView('tasks')}>✓</button>
          <button className="crm2-ref-avatar" title={session?.member.displayName ?? 'User'} onDoubleClick={() => session ? void switchUser(session.member.id) : undefined}></button>
          <button className="crm2-ref-icon" title="Timer">◷</button>
          <button className="crm2-ref-icon crm2-ref-bell" title="Notifications" onClick={() => void openNotifications()}>♢<b>{notifications.length || 1}</b></button>
        </header>
        {globalHits.length > 0 ? <div className="crm2-global-results">{globalHits.map((hit) => <button key={`${hit.type}-${hit.id}`} onClick={() => openSearchHit(hit)}><strong>{hit.title}</strong><span>{hit.type} · {hit.status}</span><small>{hit.subtitle || hit.secondary || ''}</small></button>)}</div> : null}
        {showNotifications ? <div className="crm2-notification-panel">{notifications.length === 0 ? <p>No notifications right now</p> : notifications.map((item) => <button key={`${item.type}-${item.recordId}`} onClick={() => { if (item.leadId) { setSelectedLeadId(item.leadId); setView('leads') } setShowNotifications(false) }}><strong>{item.title}</strong><span>{item.detail}</span><small>{item.severity}</small></button>)}</div> : null}
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
              {can('CreateLead') ? <button className="crm2-ref-primary" onClick={() => setShowAddLead(true)}>+ New Lead</button> : null}
              {can('CreateLead') ? <button className="crm2-ref-primary" onClick={importLeads}>Import CSV / Excel</button> : null}
              <button className={`crm2-ref-square ${leadViewMode === 'list' ? 'active' : ''}`} onClick={() => setLeadViewMode('list')}>☰</button>
              <button className={`crm2-ref-square ${leadViewMode === 'grid' ? 'active' : ''}`} onClick={() => setLeadViewMode('grid')}>▦</button>
            </div>
            <section className="crm2-ref-filter-card">
              <strong>Filter by</strong>
              <div className="crm2-ref-filter-grid">
                <select value={assignedFilter} onChange={(e) => setAssignedFilter(e.target.value)}><option value="All">All assignees</option><option value="Unassigned">Unassigned</option>{teamMembers.filter((member) => member.active).map((member) => <option key={member.id} value={member.id}>{member.displayName}</option>)}</select>
                <select value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)}><option value="All">All statuses</option>{statuses.map((status) => <option key={status} value={status}>{statusLabels[status] || status}</option>)}</select>
                <select value={sourceFilter} onChange={(e) => setSourceFilter(e.target.value)}><option value="All">All sources</option>{leadSources.map((leadSource) => <option key={leadSource}>{leadSource}</option>)}</select>
                <select value={tagFilter} onChange={(e) => setTagFilter(e.target.value)}><option value="All">All tags</option>{leadTags.map((tag) => <option key={tag}>{tag}</option>)}</select>
                <select value={extraFilter} onChange={(e) => setExtraFilter(e.target.value)}><option value="All">Additional Filters</option><option value="HighPriority">High priority</option><option value="WithMobile">With mobile</option><option value="WithEmail">With email</option><option value="WithNextFollowUp">With next follow-up</option><option value="WithValue">With lead value</option><option value="Unassigned">Unassigned</option></select>
              </div>
            </section>
            <section className="crm2-ref-table-card">
              <div className="crm2-ref-table-tools">
                <select><option>25</option><option>50</option></select>
                <button onClick={() => void exportLeads('csv')}>Export CSV</button>
                <button onClick={() => void exportLeads('xlsx')}>Export Excel</button>
                {can('EditLead') ? <select value={bulkStatus} onChange={(e) => setBulkStatus(e.target.value)}><option value="">Bulk Status</option><option value="New">New</option><option value="Contacted">Contacted</option><option value="Qualified">Qualified</option><option value="Unqualified">Unqualified</option></select> : null}
                {can('EditLead') ? <select value={bulkPriority} onChange={(e) => setBulkPriority(e.target.value)}><option value="">Bulk Priority</option><option>Low</option><option>Normal</option><option>High</option><option>Urgent</option></select> : null}
                {can('AssignLead') ? <select value={bulkOwnerId} onChange={(e) => setBulkOwnerId(e.target.value)}><option value="">Bulk Assignee</option><option value="__unassigned__">Unassigned</option>{teamMembers.filter((member) => member.active).map((member) => <option key={member.id} value={member.id}>{member.displayName}</option>)}</select> : null}
                {can('EditLead') || can('AssignLead') ? <button disabled={loading} onClick={() => void applyBulkLeadUpdate()}>Apply Bulk</button> : null}
                <button onClick={refresh}>Refresh</button><span /><label><b>⌕</b><input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Search..." /></label>
              </div>
              <div className="crm2-ref-leads-head"><span><input type="checkbox" checked={filteredLeads.length > 0 && filteredLeads.every((lead) => selectedLeadIds.includes(lead.id))} onChange={(e) => setSelectedLeadIds((ids) => e.target.checked ? Array.from(new Set([...ids, ...filteredLeads.map((lead) => lead.id)])) : ids.filter((id) => !filteredLeads.some((lead) => lead.id === id)))} /></span><span>#</span><span>Name</span><span>Company</span><span>Email</span><span>Phone</span><span>Value</span><span>Tags</span><span>Assigned</span><span>Status</span></div>
              {filteredLeads.length === 0 ? <p className="crm2-reference-empty">No entries found</p> : leadViewMode === 'grid' ? <div className="crm2-ref-lead-grid">{filteredLeads.map((lead) => {
                const owner = teamMembers.find((member) => member.id === lead.ownerUserId)
                return <article key={lead.id} onClick={() => setSelectedLeadId(lead.id)}><strong>{lead.contactName || lead.title}</strong><span>{lead.title}</span><small>{lead.mobileNumber || lead.email || 'No contact'}</small><small>{formatLeadValue(lead.estimatedValue)} · {owner?.displayName || 'Unassigned'}</small><div>{(lead.tags || []).map((tag) => <em key={tag}>{tag}</em>)}</div><em>{statusLabels[lead.status] || lead.status}</em></article>
              })}</div> : filteredLeads.map((lead, index) => {
                const owner = teamMembers.find((member) => member.id === lead.ownerUserId)
                return <article className="crm2-ref-leads-row" key={lead.id} onClick={() => setSelectedLeadId(lead.id)}>
                  <span><input type="checkbox" checked={selectedLeadIds.includes(lead.id)} onClick={(e) => e.stopPropagation()} onChange={(e) => toggleSelectedLead(lead.id, e.target.checked)} /></span>
                  <span>{index + 1}</span><span><a>{lead.contactName || lead.title}</a></span><span>{lead.title}</span><span>{lead.email || '-'}</span><span>{lead.mobileNumber || '-'}</span><span>{formatLeadValue(lead.estimatedValue)}</span>
                  <span>{(lead.tags || []).length ? (lead.tags || []).map((tag) => <em key={tag}>{tag}</em>) : <small>—</small>}</span>
                  <span>{can('AssignLead') ? <select value={lead.ownerUserId || ''} disabled={loading} onClick={(e) => e.stopPropagation()} onChange={(e) => void reassignLead(lead.id, e.target.value || null)}><option value="">Unassigned</option>{teamMembers.filter((member) => member.active).map((member) => <option key={member.id} value={member.id}>{member.displayName}</option>)}</select> : <i className="crm2-ref-avatar-mini" title={owner?.displayName || 'Unassigned'}>{(owner?.displayName || 'U').slice(0, 1).toUpperCase()}</i>}</span>
                  <span><select value={lead.status} disabled={loading || !can('EditLead') || lead.status === 'Converted'} onClick={(e) => e.stopPropagation()} onChange={(e) => void moveLead(lead.id, e.target.value)}>{statuses.filter((status) => status !== 'Converted' || lead.status === 'Converted').map((status) => <option key={status}>{status}</option>)}</select></span>
                </article>
              })}
            </section>
          </section>
        ) : null}
        {referenceModuleContent[view] && view !== 'sales' ? (
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

        {view === 'sales' && can('ViewSales') ? (
          <CrmSalesWorkspace accounts={accounts} opportunities={opportunities} documents={salesDocuments} invoices={invoices} salesItems={salesItems} creditNotes={creditNotes} busy={loading} refresh={refresh} notify={setMessage} canManageSales={can('ManageSales')} />
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
                <label>Lead value (₹)<input type="number" min="0" value={leadValue} onChange={(e) => setLeadValue(e.target.value)} placeholder="e.g. 29999" /></label>
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
