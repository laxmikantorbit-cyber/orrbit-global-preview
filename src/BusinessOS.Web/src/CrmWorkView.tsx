import { useMemo, useState } from 'react'
import { cancelCrmFollowUp, cancelCrmTask, rescheduleCrmFollowUp, updateCrmTask } from './crmAdvancedApi'
import { createCrmTask, type CrmAccount, type CrmBusinessRecord, type CrmCreditNote, type CrmDashboard, type CrmFollowUp, type CrmInvoice, type CrmInvoicePayment, type CrmLead, type CrmSalesDocument, type CrmSalesItem, type CrmSalesItemGroup, type CrmTask, type CrmTeamMember, type CrmWorkSummary } from './crmApi'
import { exportCrmSpreadsheet, type CrmSpreadsheetFormat } from './crmSpreadsheet'

type View = 'followups' | 'tasks' | 'reports'

type FollowUpUpdate = {
  dueAtUtc: string
  channel: string
  purpose: string
  ownerUserId?: string | null
}

type TaskUpdate = {
  title: string
  details?: string
  dueAtUtc?: string
  priority: string
  assigneeUserId?: string | null
}

type Props = {
  view: View
  leads: CrmLead[]
  followUps: CrmFollowUp[]
  tasks: CrmTask[]
  accounts?: CrmAccount[]
  salesDocuments?: CrmSalesDocument[]
  invoices?: CrmInvoice[]
  invoicePayments?: CrmInvoicePayment[]
  salesItems?: CrmSalesItem[]
  salesItemGroups?: CrmSalesItemGroup[]
  creditNotes?: CrmCreditNote[]
  businessRecords?: CrmBusinessRecord[]
  reportSection?: 'sales' | 'expenses' | 'profit'
  teamMembers?: CrmTeamMember[]
  currentUserId?: string
  canViewAllOwnedRecords?: boolean
  summary: CrmWorkSummary
  dashboard: CrmDashboard
  busy: boolean
  openLead: (leadId: string) => void
  completeFollowUp: (id: string) => Promise<void>
  rescheduleFollowUp?: (id: string, input: FollowUpUpdate) => Promise<void>
  cancelFollowUp?: (id: string) => Promise<void>
  completeTask: (id: string) => Promise<void>
  updateTask?: (id: string, input: TaskUpdate) => Promise<void>
  cancelTask?: (id: string) => Promise<void>
  refresh?: () => Promise<void>
  notify?: (message: string) => void
}

function formatDate(value?: string | null) {
  if (!value) return 'No due date'
  const parsed = new Date(value)
  if (Number.isNaN(parsed.getTime())) return value
  return new Intl.DateTimeFormat('en-IN', { dateStyle: 'medium', timeStyle: 'short' }).format(parsed)
}

function money(value: number) {
  return new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR', maximumFractionDigits: 2 }).format(value)
}

function localInput(value?: string | null) {
  if (!value) return ''
  const parsed = new Date(value)
  if (Number.isNaN(parsed.getTime())) return ''
  const local = new Date(parsed.getTime() - parsed.getTimezoneOffset() * 60000)
  return local.toISOString().slice(0, 16)
}

function utcInput(value?: string) {
  if (!value) return undefined
  const parsed = new Date(value)
  return Number.isNaN(parsed.getTime()) ? undefined : parsed.toISOString()
}

export function CrmWorkView(props: Props) {
  const [editingFollowUpId, setEditingFollowUpId] = useState<string | null>(null)
  const [followDue, setFollowDue] = useState('')
  const [followChannel, setFollowChannel] = useState('Call')
  const [followPurpose, setFollowPurpose] = useState('')
  const [followOwner, setFollowOwner] = useState('')
  const [editingTaskId, setEditingTaskId] = useState<string | null>(null)
  const [creatingTask, setCreatingTask] = useState(false)
  const [taskTitle, setTaskTitle] = useState('')
  const [taskDetails, setTaskDetails] = useState('')
  const [taskDue, setTaskDue] = useState('')
  const [taskPriority, setTaskPriority] = useState('Normal')
  const [taskAssignee, setTaskAssignee] = useState('')
  const [taskQuery, setTaskQuery] = useState('')
  const [taskStatusFilter, setTaskStatusFilter] = useState('All')
  const [taskScope, setTaskScope] = useState<'all' | 'mine' | 'overdue'>('all')
  const [taskPageSize, setTaskPageSize] = useState(25)
  const [selectedTaskIds, setSelectedTaskIds] = useState<string[]>([])
  const [localBusy, setLocalBusy] = useState(false)

  const busy = props.busy || localBusy
  const leadTitle = (id?: string | null) => props.leads.find((x) => x.id === id)?.title || 'General task'
  const team = props.teamMembers ?? []
  const assignableUsers = props.canViewAllOwnedRecords
    ? team.filter((x) => x.active)
    : team.filter((x) => x.active && (!props.currentUserId || x.id === props.currentUserId))

  const filteredTasks = useMemo(() => {
    const search = taskQuery.trim().toLowerCase()
    const now = Date.now()
    return props.tasks.filter((item) => {
      if (taskStatusFilter !== 'All' && item.status !== taskStatusFilter) return false
      if (taskScope === 'mine' && props.currentUserId && item.assigneeUserId !== props.currentUserId) return false
      if (taskScope === 'overdue') {
        if (!item.dueAtUtc || ['Completed', 'Cancelled'].includes(item.status)) return false
        if (new Date(item.dueAtUtc).getTime() >= now) return false
      }
      if (!search) return true
      const assignee = team.find(user => user.id === item.assigneeUserId)
      const haystack = [item.title, item.details, item.status, item.priority, assignee?.displayName, leadTitle(item.leadId)]
        .filter(Boolean).join(' ').toLowerCase()
      return haystack.includes(search)
    })
  }, [leadTitle, props.currentUserId, props.tasks, taskQuery, taskScope, taskStatusFilter, team])
  const visibleTasks = filteredTasks.slice(0, taskPageSize)

  function editFollowUp(item: CrmFollowUp) {
    setEditingFollowUpId(item.id)
    setFollowDue(localInput(item.dueAtUtc))
    setFollowChannel(item.channel)
    setFollowPurpose(item.purpose)
    setFollowOwner(item.ownerUserId || props.currentUserId || '')
  }

  function beginTask() {
    setEditingTaskId(null)
    setCreatingTask(true)
    setTaskTitle('')
    setTaskDetails('')
    setTaskDue('')
    setTaskPriority('Normal')
    setTaskAssignee(props.currentUserId || '')
  }

  function editTask(item: CrmTask) {
    setCreatingTask(false)
    setEditingTaskId(item.id)
    setTaskTitle(item.title)
    setTaskDetails(item.details || '')
    setTaskDue(localInput(item.dueAtUtc))
    setTaskPriority(item.priority)
    setTaskAssignee(item.assigneeUserId || props.currentUserId || '')
  }

  async function saveFollowUp(id: string) {
    const input = { dueAtUtc: utcInput(followDue)!, channel: followChannel, purpose: followPurpose, ownerUserId: followOwner || null }
    setLocalBusy(true)
    try {
      if (props.rescheduleFollowUp) await props.rescheduleFollowUp(id, input)
      else await rescheduleCrmFollowUp(id, input)
      setEditingFollowUpId(null)
      if (!props.rescheduleFollowUp) window.location.reload()
    } finally { setLocalBusy(false) }
  }

  async function cancelFollowUp(id: string) {
    setLocalBusy(true)
    try {
      if (props.cancelFollowUp) await props.cancelFollowUp(id)
      else await cancelCrmFollowUp(id, 'Cancelled from follow-up centre')
      if (!props.cancelFollowUp) window.location.reload()
    } finally { setLocalBusy(false) }
  }

  async function createTask() {
    if (!taskTitle.trim()) return
    setLocalBusy(true)
    try {
      await createCrmTask({
        title: taskTitle.trim(),
        details: taskDetails.trim() || undefined,
        dueAtUtc: utcInput(taskDue),
        priority: taskPriority,
        assigneeUserId: taskAssignee || undefined,
      })
      setCreatingTask(false)
      setTaskTitle(''); setTaskDetails(''); setTaskDue(''); setTaskPriority('Normal'); setTaskAssignee('')
      props.notify?.('Task created')
      if (props.refresh) await props.refresh()
      else window.location.reload()
    } catch (error) {
      props.notify?.(error instanceof Error ? error.message : String(error))
    } finally { setLocalBusy(false) }
  }

  async function saveTask(id: string) {
    const input = { title: taskTitle, details: taskDetails, dueAtUtc: utcInput(taskDue), priority: taskPriority, assigneeUserId: taskAssignee || null }
    setLocalBusy(true)
    try {
      if (props.updateTask) await props.updateTask(id, input)
      else await updateCrmTask(id, input)
      setEditingTaskId(null)
      if (!props.updateTask) window.location.reload()
    } finally { setLocalBusy(false) }
  }

  async function cancelTask(id: string) {
    setLocalBusy(true)
    try {
      if (props.cancelTask) await props.cancelTask(id)
      else await cancelCrmTask(id)
      if (!props.cancelTask) window.location.reload()
    } finally { setLocalBusy(false) }
  }

  function toggleTaskSelection(id: string) {
    setSelectedTaskIds(current => current.includes(id) ? current.filter(value => value !== id) : [...current, id])
  }

  function toggleVisibleTaskSelection() {
    const ids = visibleTasks.map(item => item.id)
    const allSelected = ids.length > 0 && ids.every(id => selectedTaskIds.includes(id))
    setSelectedTaskIds(allSelected
      ? selectedTaskIds.filter(id => !ids.includes(id))
      : Array.from(new Set([...selectedTaskIds, ...ids])))
  }

  async function exportTasks(format: CrmSpreadsheetFormat) {
    const table = {
      headers: ['Task', 'Lead / Customer', 'Status', 'Created', 'Due', 'Assigned To', 'Priority', 'Details'],
      rows: filteredTasks.map(item => {
        const assignee = team.find(user => user.id === item.assigneeUserId)
        return [
          item.title, leadTitle(item.leadId), item.status, item.createdAtUtc,
          item.dueAtUtc || '', assignee?.displayName || '', item.priority, item.details || '',
        ]
      }),
    }
    await exportCrmSpreadsheet(`crm-tasks-${new Date().toISOString().slice(0, 10)}`, table, format)
  }

  async function bulkCompleteTasks() {
    const ids = selectedTaskIds.filter(id => {
      const item = props.tasks.find(task => task.id === id)
      return item && !['Completed', 'Cancelled'].includes(item.status)
    })
    if (ids.length === 0) return
    setLocalBusy(true)
    try {
      for (const id of ids) await props.completeTask(id)
      setSelectedTaskIds(current => current.filter(id => !ids.includes(id)))
    } finally { setLocalBusy(false) }
  }

  async function bulkCancelTasks() {
    const ids = selectedTaskIds.filter(id => {
      const item = props.tasks.find(task => task.id === id)
      return item && !['Completed', 'Cancelled'].includes(item.status)
    })
    if (ids.length === 0) return
    setLocalBusy(true)
    try {
      for (const id of ids) {
        if (props.cancelTask) await props.cancelTask(id)
        else await cancelCrmTask(id)
      }
      setSelectedTaskIds(current => current.filter(id => !ids.includes(id)))
      if (!props.cancelTask) window.location.reload()
    } finally { setLocalBusy(false) }
  }

  if (props.view === 'followups') {
    const open = props.followUps.filter((x) => x.status === 'Open')
    return (
      <section className="crm2-module-page">
        <div className="crm2-module-head"><div><span className="crm2-kicker">FOLLOW-UP CENTRE</span><h2>Customer follow-ups</h2><p>Calls, WhatsApp, email and meeting reminders in one queue.</p></div><div className="crm2-module-stat"><strong>{open.length}</strong><span>open</span></div></div>
        <div className="crm2-summary-strip"><div><span>Open</span><b>{props.summary.openFollowUps}</b></div><div className="danger"><span>Overdue</span><b>{props.summary.overdueFollowUps}</b></div><div><span>Due today</span><b>{props.summary.dueTodayFollowUps}</b></div></div>
        <div className="crm2-module-list">
          {props.followUps.length === 0 ? <div className="crm2-empty"><strong>No follow-ups yet</strong><span>Open a lead and schedule the first follow-up.</span></div> : props.followUps.map((item) => {
            const overdue = item.status === 'Open' && new Date(item.dueAtUtc) < new Date()
            const editing = editingFollowUpId === item.id
            return <article key={item.id} className={overdue ? 'overdue' : ''} style={{ alignItems: editing ? 'flex-start' : undefined }}>
              <button className="crm2-link-button" onClick={() => props.openLead(item.leadId)}>{leadTitle(item.leadId)}</button>
              <div style={{ flex: 1 }}>
                <strong>{item.purpose}</strong><span>{item.channel} · {formatDate(item.dueAtUtc)}</span>{item.outcome ? <small>{item.outcome}</small> : null}
                {editing ? <div className="crm-advanced-form stacked" style={{ marginTop: 12 }}>
                  <input type="datetime-local" value={followDue} onChange={(e) => setFollowDue(e.target.value)} />
                  <select value={followChannel} onChange={(e) => setFollowChannel(e.target.value)}><option>Call</option><option>WhatsApp</option><option>Email</option><option>Meeting</option><option>Other</option></select>
                  <input value={followPurpose} onChange={(e) => setFollowPurpose(e.target.value)} placeholder="Follow-up purpose" />
                  {team.length ? <select value={followOwner} onChange={(e) => setFollowOwner(e.target.value)}><option value="">Unassigned</option>{assignableUsers.map((user) => <option key={user.id} value={user.id}>{user.displayName} · {user.role}</option>)}</select> : null}
                  <div className="crm2-top-actions"><button className="crm2-primary" disabled={busy || !followDue || !followPurpose.trim()} onClick={() => void saveFollowUp(item.id)}>Save</button><button className="crm2-refresh" onClick={() => setEditingFollowUpId(null)}>Close</button></div>
                </div> : null}
              </div>
              <em className={item.status.toLowerCase()}>{overdue ? 'Overdue' : item.status}</em>
              {item.status === 'Open' ? <div className="crm2-top-actions"><button disabled={busy} onClick={() => editFollowUp(item)}>Reschedule</button><button disabled={busy} onClick={() => void props.completeFollowUp(item.id)}>Complete</button><button disabled={busy} onClick={() => void cancelFollowUp(item.id)}>Cancel</button></div> : null}
            </article>
          })}
        </div>
      </section>
    )
  }

  if (props.view === 'tasks') {
    const count = (status: string) => props.tasks.filter(x => x.status === status).length
    const assigned = (status: string) => props.tasks.filter(x => x.status === status && (!props.currentUserId || x.assigneeUserId === props.currentUserId)).length
    return (
      <section className="crm2-ref-list-page crm2-tasks-reference">
        <div className="crm2-ref-action-row">
          <button className="crm2-ref-primary" onClick={beginTask} disabled={busy}>+ New Task</button>
          <button className={taskScope === 'mine' ? 'crm2-ref-square active' : 'crm2-ref-square'} onClick={() => setTaskScope(taskScope === 'mine' ? 'all' : 'mine')} title="My tasks">▦</button>
          <span className="crm2-action-spacer" />
          <button className="crm2-tasks-overview" onClick={() => { setTaskScope('all'); setTaskStatusFilter('All'); setTaskQuery('') }}>Tasks Overview</button>
          <button className={taskScope === 'overdue' ? 'crm2-ref-square active' : 'crm2-ref-square'} onClick={() => setTaskScope(taskScope === 'overdue' ? 'all' : 'overdue')} title="Overdue tasks">▼</button>
        </div>
        {creatingTask ? <section className="crm2-ref-filter-card"><strong>New Task</strong><div className="crm2-form-grid">
          <label>Task title<input value={taskTitle} onChange={(e) => setTaskTitle(e.target.value)} placeholder="What needs to be done?" /></label>
          <label>Due date/time<input type="datetime-local" value={taskDue} onChange={(e) => setTaskDue(e.target.value)} /></label>
          <label>Priority<select value={taskPriority} onChange={(e) => setTaskPriority(e.target.value)}><option>Low</option><option>Normal</option><option>High</option><option>Urgent</option></select></label>
          {team.length ? <label>Assigned to<select value={taskAssignee} onChange={(e) => setTaskAssignee(e.target.value)}><option value="">Unassigned</option>{assignableUsers.map((user) => <option key={user.id} value={user.id}>{user.displayName} · {user.role}</option>)}</select></label> : null}
        </div><label>Details<textarea rows={3} value={taskDetails} onChange={(e) => setTaskDetails(e.target.value)} /></label>
        <div className="crm2-drawer-actions"><button onClick={() => setCreatingTask(false)}>Cancel</button><button className="crm2-primary" disabled={busy || !taskTitle.trim()} onClick={() => void createTask()}>Create Task</button></div></section> : null}
        <section className="crm2-reference-status-summary crm2-task-summary">
          <h2>▧ Tasks Summary</h2>
          <div>
            <span><b>{count('Open')}</b><em>Not Started<small>Tasks assigned to me: {assigned('Open')}</small></em></span>
            <span><b>{count('InProgress')}</b><em className="blue">In Progress<small>Tasks assigned to me: {assigned('InProgress')}</small></em></span>
            <span><b>{count('Testing')}</b><em className="blue">Testing<small>Tasks assigned to me: {assigned('Testing')}</small></em></span>
            <span><b>{count('AwaitingFeedback')}</b><em className="warn">Awaiting Feedback<small>Tasks assigned to me: {assigned('AwaitingFeedback')}</small></em></span>
            <span><b>{count('Completed')}</b><em className="good">Complete<small>Tasks assigned to me: {assigned('Completed')}</small></em></span>
          </div>
        </section>
        <section className="crm2-ref-table-card">
          <div className="crm2-ref-table-tools">
            <select value={taskPageSize} onChange={e => setTaskPageSize(Number(e.target.value))}><option value={25}>25</option><option value={50}>50</option><option value={100}>100</option></select>
            <button onClick={() => void exportTasks('xlsx')} disabled={busy || filteredTasks.length === 0}>Export XLSX</button>
            <button onClick={() => void exportTasks('csv')} disabled={busy || filteredTasks.length === 0}>CSV</button>
            <button onClick={toggleVisibleTaskSelection} disabled={busy || visibleTasks.length === 0}>{visibleTasks.length > 0 && visibleTasks.every(item => selectedTaskIds.includes(item.id)) ? 'Clear Selection' : 'Select Visible'}</button>
            <button onClick={() => props.refresh ? void props.refresh() : window.location.reload()} disabled={busy}>↻</button><span />
            <select value={taskStatusFilter} onChange={e => setTaskStatusFilter(e.target.value)}><option>All</option><option>Open</option><option>InProgress</option><option>Testing</option><option>AwaitingFeedback</option><option>Completed</option><option>Cancelled</option></select>
            <label><b>⌕</b><input value={taskQuery} onChange={e => setTaskQuery(e.target.value)} placeholder="Search..." /></label>
          </div>
          {selectedTaskIds.length > 0 ? <div className="crm2-ref-action-row">
            <strong>{selectedTaskIds.length} selected</strong>
            <button onClick={() => void bulkCompleteTasks()} disabled={busy}>Mark Complete</button>
            <button onClick={() => void bulkCancelTasks()} disabled={busy}>Cancel Tasks</button>
            <button onClick={() => setSelectedTaskIds([])} disabled={busy}>Clear</button>
          </div> : null}
          <div className="crm2-task-head"><span></span><span>#</span><span>Name</span><span>Status</span><span>Start Date</span><span>Due Date</span><span>Assigned to</span><span>Tags</span><span>Priority</span></div>
          {filteredTasks.length === 0 ? <p className="crm2-reference-empty">No entries found</p> : visibleTasks.map((item, index) => {
            const overdue = !['Completed', 'Cancelled'].includes(item.status) && !!item.dueAtUtc && new Date(item.dueAtUtc) < new Date()
            const editing = editingTaskId === item.id
            const assignee = team.find(user => user.id === item.assigneeUserId)
            return <div className={'crm2-task-row-wrap' + (overdue ? ' overdue' : '')} key={item.id}>
              <div className="crm2-task-row">
                <span><input type="checkbox" checked={selectedTaskIds.includes(item.id)} onChange={() => toggleTaskSelection(item.id)} /></span><span>{index + 1}</span>
                <span><a onClick={() => item.leadId && props.openLead(item.leadId)}>{item.title}</a>{item.status === 'Open' ? <small><button disabled={busy} onClick={() => editTask(item)}>Edit</button><button disabled={busy} onClick={() => void props.completeTask(item.id)}>Complete</button><button disabled={busy} onClick={() => void cancelTask(item.id)}>Cancel</button></small> : null}</span>
                <span><em className={'crm2-task-status ' + item.status.toLowerCase()}>{item.status === 'Open' ? 'Not Started' : item.status}</em></span>
                <span>{new Date(item.createdAtUtc).toLocaleDateString('en-IN')}</span><span>{item.dueAtUtc ? new Date(item.dueAtUtc).toLocaleDateString('en-IN') : '—'}</span>
                <span>{assignee?.displayName || '—'}</span><span>—</span><span className={'priority-' + item.priority.toLowerCase()}>{item.priority}</span>
              </div>
              {editing ? <div className="crm2-task-inline-edit">
                <input value={taskTitle} onChange={(e) => setTaskTitle(e.target.value)} placeholder="Task title" />
                <textarea rows={2} value={taskDetails} onChange={(e) => setTaskDetails(e.target.value)} placeholder="Task details" />
                <input type="datetime-local" value={taskDue} onChange={(e) => setTaskDue(e.target.value)} />
                <select value={taskPriority} onChange={(e) => setTaskPriority(e.target.value)}><option>Low</option><option>Normal</option><option>High</option><option>Urgent</option></select>
                {team.length ? <select value={taskAssignee} onChange={(e) => setTaskAssignee(e.target.value)}><option value="">Unassigned</option>{assignableUsers.map((user) => <option key={user.id} value={user.id}>{user.displayName} · {user.role}</option>)}</select> : null}
                <button className="crm2-primary" disabled={busy || !taskTitle.trim()} onClick={() => void saveTask(item.id)}>Save</button><button onClick={() => setEditingTaskId(null)}>Close</button>
              </div> : null}
            </div>
          })}
        </section>
      </section>
    )
  }

  const reportAccounts = props.accounts ?? []
  const reportDocuments = props.salesDocuments ?? []
  const reportInvoices = props.invoices ?? []
  const reportPayments = props.invoicePayments ?? []
  const reportItems = props.salesItems ?? []
  const reportItemGroups = props.salesItemGroups ?? []
  const reportCredits = props.creditNotes ?? []
  const accountName = (id: string) => reportAccounts.find(x => x.id === id)?.name || id
  const invoiceNumber = (id: string) => reportInvoices.find(x => x.id === id)?.invoiceNumber || id
  const itemGroupName = (id?: string | null) => reportItemGroups.find(x => x.id === id)?.name || 'Ungrouped'
  const nonVoidInvoices = reportInvoices.filter(x => x.status !== 'Void')
  const proposals = reportDocuments.filter(x => x.kind === 'Proposal')
  const estimates = reportDocuments.filter(x => x.kind === 'Estimate')
  const issuedCredits = reportCredits.filter(x => x.status === 'Issued')
  const totalInvoiced = nonVoidInvoices.reduce((sum, x) => sum + x.netTotal, 0)
  const totalReceived = reportPayments.reduce((sum, x) => sum + x.amount, 0)
  const totalOutstanding = nonVoidInvoices.reduce((sum, x) => sum + x.balance, 0)
  const totalCredits = issuedCredits.reduce((sum, x) => sum + x.amount, 0)
  const activeItems = reportItems.filter(x => x.status === 'Active').length
  const activeCustomers = reportAccounts.filter(x => x.status === 'Active').length

  const paymentModes = Array.from(reportPayments.reduce((map, payment) => {
    const current = map.get(payment.method) ?? { count: 0, amount: 0 }
    current.count += 1
    current.amount += payment.amount
    map.set(payment.method, current)
    return map
  }, new Map<string, { count: number; amount: number }>()).entries())
    .map(([method, value]) => ({ method, ...value }))
    .sort((a, b) => b.amount - a.amount)

  const monthlyIncome = Array.from(reportPayments.reduce((map, payment) => {
    const key = payment.receivedAtUtc.slice(0, 7)
    map.set(key, (map.get(key) ?? 0) + payment.amount)
    return map
  }, new Map<string, number>()).entries())
    .sort(([a], [b]) => b.localeCompare(a))
    .map(([month, amount]) => ({ month, amount }))

  const customerGroupMap = new Map<string, { accountIds: Set<string>; invoiced: number; received: number; outstanding: number }>()
  for (const account of reportAccounts) {
    const labels = account.groups.length ? account.groups : ['Ungrouped']
    const accountInvoices = nonVoidInvoices.filter(invoice => invoice.accountId === account.id)
    for (const label of labels) {
      const row = customerGroupMap.get(label) ?? { accountIds: new Set<string>(), invoiced: 0, received: 0, outstanding: 0 }
      row.accountIds.add(account.id)
      row.invoiced += accountInvoices.reduce((sum, invoice) => sum + invoice.netTotal, 0)
      row.received += accountInvoices.reduce((sum, invoice) => sum + invoice.amountPaid, 0)
      row.outstanding += accountInvoices.reduce((sum, invoice) => sum + invoice.balance, 0)
      customerGroupMap.set(label, row)
    }
  }
  const customerGroupRows = Array.from(customerGroupMap.entries())
    .map(([group, value]) => ({ group, accounts: value.accountIds.size, invoiced: value.invoiced, received: value.received, outstanding: value.outstanding }))
    .sort((a, b) => b.invoiced - a.invoiced)

  async function exportReport(
    name: string,
    headers: string[],
    rows: Array<Array<string | number | boolean | null | undefined>>,
    format: CrmSpreadsheetFormat,
  ) {
    await exportCrmSpreadsheet(`${name}-${new Date().toISOString().slice(0, 10)}`, { headers, rows }, format)
  }

  const maxMonthlyIncome = Math.max(1, ...monthlyIncome.map(x => x.amount))
  const maxPaymentMode = Math.max(1, ...paymentModes.map(x => x.amount))
  const maxCustomerGroup = Math.max(1, ...customerGroupRows.map(x => x.invoiced))

  const reportSection = props.reportSection ?? 'sales'
  const expenses = (props.businessRecords ?? []).filter(x => x.module === 'Expense')
  const nonRejectedExpenses = expenses.filter(x => x.status !== 'Rejected')
  const paidExpenses = expenses.filter(x => x.status === 'Paid')
  const recordedExpenseTotal = nonRejectedExpenses.reduce((sum, x) => sum + (x.amount ?? 0), 0)
  const paidExpenseTotal = paidExpenses.reduce((sum, x) => sum + (x.amount ?? 0), 0)
  const approvedExpenseTotal = expenses.filter(x => x.status === 'Approved').reduce((sum, x) => sum + (x.amount ?? 0), 0)
  const rejectedExpenseTotal = expenses.filter(x => x.status === 'Rejected').reduce((sum, x) => sum + (x.amount ?? 0), 0)
  const expenseCategoryRows = Array.from(nonRejectedExpenses.reduce((map, expense) => {
    const key = expense.category || 'Uncategorized'
    const current = map.get(key) ?? { count: 0, amount: 0 }
    current.count += 1
    current.amount += expense.amount ?? 0
    map.set(key, current)
    return map
  }, new Map<string, { count: number; amount: number }>()).entries())
    .map(([category, value]) => ({ category, ...value }))
    .sort((a, b) => b.amount - a.amount)

  const cashFlowMonths = new Map<string, { income: number; expenses: number }>()
  for (const payment of reportPayments) {
    const key = payment.receivedAtUtc.slice(0, 7)
    const row = cashFlowMonths.get(key) ?? { income: 0, expenses: 0 }
    row.income += payment.amount
    cashFlowMonths.set(key, row)
  }
  for (const expense of paidExpenses) {
    const date = expense.startDate || expense.createdAtUtc
    const key = date.slice(0, 7)
    const row = cashFlowMonths.get(key) ?? { income: 0, expenses: 0 }
    row.expenses += expense.amount ?? 0
    cashFlowMonths.set(key, row)
  }
  const cashFlowRows = Array.from(cashFlowMonths.entries())
    .map(([month, value]) => ({ month, ...value, net: value.income - value.expenses }))
    .sort((a, b) => b.month.localeCompare(a.month))

  if (reportSection === 'expenses') {
    return (
      <section className="crm2-ref-list-page crm2-reports-reference">
        <div className="crm2-ref-action-row">
          <strong>Expense Report</strong><span className="crm2-action-spacer" />
          <button disabled={expenses.length === 0} onClick={() => void exportReport('crm-expenses-report', ['Expense', 'Category', 'Status', 'Amount', 'Date', 'Payment Mode', 'Reference'], expenses.map(x => [x.title, x.category || '', x.status, x.amount || 0, x.startDate || x.createdAtUtc, x.metadata?.paymentMode || '', x.metadata?.reference || '']), 'xlsx')}>Export XLSX</button>
          <button disabled={expenses.length === 0} onClick={() => void exportReport('crm-expenses-report', ['Expense', 'Category', 'Status', 'Amount', 'Date', 'Payment Mode', 'Reference'], expenses.map(x => [x.title, x.category || '', x.status, x.amount || 0, x.startDate || x.createdAtUtc, x.metadata?.paymentMode || '', x.metadata?.reference || '']), 'csv')}>CSV</button>
        </div>
        <section className="crm2-metrics">
          <article><span>Recorded (non-rejected)</span><strong>{money(recordedExpenseTotal)}</strong><small>{nonRejectedExpenses.length} record(s)</small></article>
          <article><span>Approved, not paid</span><strong>{money(approvedExpenseTotal)}</strong><small>{expenses.filter(x => x.status === 'Approved').length} record(s)</small></article>
          <article className="accent"><span>Actually paid</span><strong>{money(paidExpenseTotal)}</strong><small>{paidExpenses.length} paid expense(s)</small></article>
          <article><span>Rejected</span><strong>{money(rejectedExpenseTotal)}</strong><small>{expenses.filter(x => x.status === 'Rejected').length} record(s)</small></article>
        </section>
        <div className="crm2-report-columns">
          <section>
            <h2>Expenses by category</h2>
            <div className="crm-advanced-list">{expenseCategoryRows.map(x => <article key={x.category}><div><b>{x.category}</b><strong>{x.count} expense(s)</strong></div><span>{money(x.amount)}</span></article>)}{expenseCategoryRows.length === 0 ? <p>No expense data yet.</p> : null}</div>
          </section>
          <section>
            <h2>Recent expenses</h2>
            <div className="crm-advanced-list">{[...expenses].sort((a,b) => (b.startDate || b.createdAtUtc).localeCompare(a.startDate || a.createdAtUtc)).slice(0, 15).map(x => <article key={x.id}><div><b>{x.category || 'Uncategorized'} · {x.status}</b><strong>{x.title}</strong><small>{x.startDate || x.createdAtUtc.slice(0,10)} · {x.metadata?.paymentMode || 'Payment mode not set'}</small></div><span>{money(x.amount || 0)}</span></article>)}{expenses.length === 0 ? <p>No expense records yet.</p> : null}</div>
          </section>
        </div>
        <p className="crm2-report-note">ⓘ Cash outflow uses only expenses whose workflow status is Paid. Rejected expenses are excluded from recorded totals.</p>
      </section>
    )
  }

  if (reportSection === 'profit') {
    const netCash = totalReceived - paidExpenseTotal
    return (
      <section className="crm2-ref-list-page crm2-reports-reference">
        <div className="crm2-ref-action-row">
          <strong>Expenses vs Income</strong><span className="crm2-action-spacer" />
          <button disabled={cashFlowRows.length === 0} onClick={() => void exportReport('crm-income-vs-expenses', ['Month', 'Received Income', 'Paid Expenses', 'Net Cash'], cashFlowRows.map(x => [x.month, x.income, x.expenses, x.net]), 'xlsx')}>Export XLSX</button>
          <button disabled={cashFlowRows.length === 0} onClick={() => void exportReport('crm-income-vs-expenses', ['Month', 'Received Income', 'Paid Expenses', 'Net Cash'], cashFlowRows.map(x => [x.month, x.income, x.expenses, x.net]), 'csv')}>CSV</button>
        </div>
        <section className="crm2-metrics">
          <article><span>Actually received</span><strong>{money(totalReceived)}</strong><small>{reportPayments.length} payment transaction(s)</small></article>
          <article><span>Actually paid expenses</span><strong>{money(paidExpenseTotal)}</strong><small>{paidExpenses.length} paid expense(s)</small></article>
          <article className="accent"><span>Net cash movement</span><strong>{money(netCash)}</strong><small>Received minus paid expenses</small></article>
          <article><span>Outstanding invoices</span><strong>{money(totalOutstanding)}</strong><small>Not counted as received income</small></article>
        </section>
        <section className="crm2-table-card">
          <div className="crm2-section-head"><div><span>CASH FLOW</span><h2>Monthly received income vs paid expenses</h2></div></div>
          <div className="crm-advanced-list">{cashFlowRows.map(x => <article key={x.month}><div><b>{x.month}</b><strong>Income {money(x.income)} · Expense {money(x.expenses)}</strong><small>Only posted payments and Paid expenses are included.</small></div><span>{money(x.net)} net</span></article>)}{cashFlowRows.length === 0 ? <p>No settled cash-flow data yet.</p> : null}</div>
        </section>
        <p className="crm2-report-note">ⓘ This is a cash comparison, not an accrual P&amp;L: invoice balances are excluded until payment is recorded.</p>
      </section>
    )
  }

  return (
    <section className="crm2-ref-list-page crm2-reports-reference">
      <div className="crm2-report-columns">
        <section>
          <h2>▧ Sales Report</h2>
          <details open><summary>Invoices Report · {nonVoidInvoices.length} active invoice(s)</summary>
            <p><b>{money(totalInvoiced)}</b> invoiced · <b>{money(totalReceived)}</b> received · <b>{money(totalOutstanding)}</b> outstanding</p>
            <div className="crm2-top-actions"><button onClick={() => void exportReport('crm-invoices-report', ['Invoice', 'Customer', 'Status', 'Issue Date', 'Due Date', 'Net Total', 'Paid', 'Balance'], nonVoidInvoices.map(x => [x.invoiceNumber, accountName(x.accountId), x.status, x.issueDate, x.dueDate, x.netTotal, x.amountPaid, x.balance]), 'xlsx')}>Export XLSX</button><button onClick={() => void exportReport('crm-invoices-report', ['Invoice', 'Customer', 'Status', 'Issue Date', 'Due Date', 'Net Total', 'Paid', 'Balance'], nonVoidInvoices.map(x => [x.invoiceNumber, accountName(x.accountId), x.status, x.issueDate, x.dueDate, x.netTotal, x.amountPaid, x.balance]), 'csv')}>CSV</button></div>
            <div className="crm-advanced-list">{nonVoidInvoices.slice(0, 5).map(x => <article key={x.id}><div><b>{x.invoiceNumber}</b><strong>{accountName(x.accountId)}</strong><small>{x.status} · Due {x.dueDate}</small></div><span>{money(x.balance)} due</span></article>)}{nonVoidInvoices.length === 0 ? <p>No invoice data yet.</p> : null}</div>
          </details>
          <details><summary>Items Report · {reportItems.length} item(s)</summary>
            <p><b>{activeItems}</b> active · <b>{reportItems.length - activeItems}</b> inactive · <b>{reportItemGroups.filter(x => x.active).length}</b> active group(s)</p>
            <div className="crm2-top-actions"><button onClick={() => void exportReport('crm-items-report', ['Code', 'Name', 'Group', 'Status', 'Rate', 'Tax %'], reportItems.map(x => [x.code, x.name, itemGroupName(x.groupId), x.status, x.defaultRate, x.defaultTaxPercent]), 'xlsx')}>Export XLSX</button><button onClick={() => void exportReport('crm-items-report', ['Code', 'Name', 'Group', 'Status', 'Rate', 'Tax %'], reportItems.map(x => [x.code, x.name, itemGroupName(x.groupId), x.status, x.defaultRate, x.defaultTaxPercent]), 'csv')}>CSV</button></div>
          </details>
          <details><summary>Payments Received · {reportPayments.length} transaction(s)</summary>
            <p><b>{money(totalReceived)}</b> received from the persisted payment ledger.</p>
            <div className="crm2-top-actions"><button onClick={() => void exportReport('crm-payments-report', ['Payment', 'Invoice', 'Method', 'Amount', 'Reference', 'Received At'], reportPayments.map(x => [x.paymentNumber, invoiceNumber(x.invoiceId), x.method, x.amount, x.reference || '', x.receivedAtUtc]), 'xlsx')}>Export XLSX</button><button onClick={() => void exportReport('crm-payments-report', ['Payment', 'Invoice', 'Method', 'Amount', 'Reference', 'Received At'], reportPayments.map(x => [x.paymentNumber, invoiceNumber(x.invoiceId), x.method, x.amount, x.reference || '', x.receivedAtUtc]), 'csv')}>CSV</button></div>
            <div className="crm-advanced-list">{reportPayments.slice(0, 5).map(x => <article key={x.id}><div><b>{x.paymentNumber}</b><strong>{invoiceNumber(x.invoiceId)}</strong><small>{x.method} · {formatDate(x.receivedAtUtc)}</small></div><span>{money(x.amount)}</span></article>)}{reportPayments.length === 0 ? <p>No payment transactions yet.</p> : null}</div>
          </details>
          <details><summary>Credit Notes Report · {issuedCredits.length} issued</summary>
            <p><b>{money(totalCredits)}</b> issued credits. Draft and void credit notes are excluded from this total.</p>
            <div className="crm2-top-actions"><button onClick={() => void exportReport('crm-credit-notes-report', ['Credit Note', 'Invoice', 'Customer', 'Status', 'Issue Date', 'Amount', 'Reason'], reportCredits.map(x => [x.creditNoteNumber, invoiceNumber(x.invoiceId), accountName(x.accountId), x.status, x.issueDate, x.amount, x.reason]), 'xlsx')}>Export XLSX</button><button onClick={() => void exportReport('crm-credit-notes-report', ['Credit Note', 'Invoice', 'Customer', 'Status', 'Issue Date', 'Amount', 'Reason'], reportCredits.map(x => [x.creditNoteNumber, invoiceNumber(x.invoiceId), accountName(x.accountId), x.status, x.issueDate, x.amount, x.reason]), 'csv')}>CSV</button></div>
          </details>
          <details><summary>Proposals Report · {proposals.length} proposal(s)</summary>
            <p><b>{proposals.filter(x => x.status === 'Accepted').length}</b> accepted · accepted value <b>{money(proposals.filter(x => x.status === 'Accepted').reduce((sum, x) => sum + x.total, 0))}</b></p>
            <div className="crm2-top-actions"><button onClick={() => void exportReport('crm-proposals-report', ['Proposal', 'Customer', 'Subject', 'Status', 'Issue Date', 'Expiry Date', 'Total'], proposals.map(x => [x.documentNumber, accountName(x.accountId), x.subject, x.status, x.issueDate, x.expiryDate || '', x.total]), 'xlsx')}>Export XLSX</button><button onClick={() => void exportReport('crm-proposals-report', ['Proposal', 'Customer', 'Subject', 'Status', 'Issue Date', 'Expiry Date', 'Total'], proposals.map(x => [x.documentNumber, accountName(x.accountId), x.subject, x.status, x.issueDate, x.expiryDate || '', x.total]), 'csv')}>CSV</button></div>
          </details>
          <details><summary>Estimates Report · {estimates.length} estimate(s)</summary>
            <p><b>{estimates.filter(x => x.status === 'Accepted').length}</b> accepted · accepted value <b>{money(estimates.filter(x => x.status === 'Accepted').reduce((sum, x) => sum + x.total, 0))}</b></p>
            <div className="crm2-top-actions"><button onClick={() => void exportReport('crm-estimates-report', ['Estimate', 'Customer', 'Subject', 'Status', 'Issue Date', 'Expiry Date', 'Total'], estimates.map(x => [x.documentNumber, accountName(x.accountId), x.subject, x.status, x.issueDate, x.expiryDate || '', x.total]), 'xlsx')}>Export XLSX</button><button onClick={() => void exportReport('crm-estimates-report', ['Estimate', 'Customer', 'Subject', 'Status', 'Issue Date', 'Expiry Date', 'Total'], estimates.map(x => [x.documentNumber, accountName(x.accountId), x.subject, x.status, x.issueDate, x.expiryDate || '', x.total]), 'csv')}>CSV</button></div>
          </details>
          <details><summary>Customers Report · {reportAccounts.length} customer(s)</summary>
            <p><b>{activeCustomers}</b> active · <b>{new Set(reportAccounts.flatMap(x => x.groups)).size}</b> customer group(s)</p>
            <div className="crm2-top-actions"><button onClick={() => void exportReport('crm-customers-report', ['Customer', 'Legal Name', 'GSTIN', 'Code', 'Status', 'Groups', 'Contacts'], reportAccounts.map(x => [x.name, x.legalName || '', x.gstin || '', x.displayCode || '', x.status, x.groups.join(', '), x.contacts.length]), 'xlsx')}>Export XLSX</button><button onClick={() => void exportReport('crm-customers-report', ['Customer', 'Legal Name', 'GSTIN', 'Code', 'Status', 'Groups', 'Contacts'], reportAccounts.map(x => [x.name, x.legalName || '', x.gstin || '', x.displayCode || '', x.status, x.groups.join(', '), x.contacts.length]), 'csv')}>CSV</button></div>
          </details>
        </section>
        <section>
          <h2>▥ Charts Based Report</h2>
          <details open><summary>Total Income · {money(totalReceived)}</summary>
            <div className="crm-advanced-list">{monthlyIncome.map(x => <article key={x.month}><div><b>{x.month}</b><progress max={maxMonthlyIncome} value={x.amount} /></div><span>{money(x.amount)}</span></article>)}{monthlyIncome.length === 0 ? <p>No received-payment data yet.</p> : null}</div>
          </details>
          <details><summary>Payment Modes (Transactions)</summary>
            <div className="crm-advanced-list">{paymentModes.map(x => <article key={x.method}><div><b>{x.method}</b><progress max={maxPaymentMode} value={x.amount} /><small>{x.count} transaction(s)</small></div><span>{money(x.amount)}</span></article>)}{paymentModes.length === 0 ? <p>No payment-mode data yet.</p> : null}</div>
            <div className="crm2-top-actions"><button onClick={() => void exportReport('crm-payment-modes-report', ['Payment Mode', 'Transactions', 'Amount'], paymentModes.map(x => [x.method, x.count, x.amount]), 'xlsx')}>Export XLSX</button></div>
          </details>
          <details><summary>Total Value By Customer Groups</summary>
            <div className="crm-advanced-list">{customerGroupRows.map(x => <article key={x.group}><div><b>{x.group}</b><progress max={maxCustomerGroup} value={x.invoiced} /><small>{x.accounts} customer(s) · received {money(x.received)} · outstanding {money(x.outstanding)}</small></div><span>{money(x.invoiced)}</span></article>)}{customerGroupRows.length === 0 ? <p>No customer-group data yet.</p> : null}</div>
            <div className="crm2-top-actions"><button onClick={() => void exportReport('crm-customer-groups-report', ['Customer Group', 'Customers', 'Invoiced', 'Received', 'Outstanding'], customerGroupRows.map(x => [x.group, x.accounts, x.invoiced, x.received, x.outstanding]), 'xlsx')}>Export XLSX</button></div>
            <small>Customers assigned to multiple groups are represented in each of their groups.</small>
          </details>
        </section>
      </div>
      <p className="crm2-report-note">ⓘ Cancelled/void records are excluded where the underlying report applies that rule.</p>
    </section>
  )

}
