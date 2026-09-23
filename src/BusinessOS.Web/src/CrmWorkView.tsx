import { useState } from 'react'
import { cancelCrmFollowUp, cancelCrmTask, rescheduleCrmFollowUp, updateCrmTask } from './crmAdvancedApi'
import type { CrmDashboard, CrmFollowUp, CrmLead, CrmTask, CrmTeamMember, CrmWorkSummary } from './crmApi'

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
}

function formatDate(value?: string | null) {
  if (!value) return 'No due date'
  const parsed = new Date(value)
  if (Number.isNaN(parsed.getTime())) return value
  return new Intl.DateTimeFormat('en-IN', { dateStyle: 'medium', timeStyle: 'short' }).format(parsed)
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
  const [taskTitle, setTaskTitle] = useState('')
  const [taskDetails, setTaskDetails] = useState('')
  const [taskDue, setTaskDue] = useState('')
  const [taskPriority, setTaskPriority] = useState('Normal')
  const [taskAssignee, setTaskAssignee] = useState('')
  const [localBusy, setLocalBusy] = useState(false)

  const busy = props.busy || localBusy
  const leadTitle = (id?: string | null) => props.leads.find((x) => x.id === id)?.title || 'General task'
  const team = props.teamMembers ?? []
  const assignableUsers = props.canViewAllOwnedRecords
    ? team.filter((x) => x.active)
    : team.filter((x) => x.active && (!props.currentUserId || x.id === props.currentUserId))

  function editFollowUp(item: CrmFollowUp) {
    setEditingFollowUpId(item.id)
    setFollowDue(localInput(item.dueAtUtc))
    setFollowChannel(item.channel)
    setFollowPurpose(item.purpose)
    setFollowOwner(item.ownerUserId || props.currentUserId || '')
  }

  function editTask(item: CrmTask) {
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
          <button className="crm2-ref-primary" disabled title="Create tasks from a lead workspace">+ New Task</button>
          <button className="crm2-ref-square">▦</button>
          <span className="crm2-action-spacer" />
          <button className="crm2-tasks-overview">Tasks Overview</button>
          <button className="crm2-ref-square">▼</button>
        </div>
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
            <select><option>25</option><option>50</option></select><button>Export</button><button>Bulk Actions</button><button>↻</button><span />
            <label><b>⌕</b><input placeholder="Search..." /></label>
          </div>
          <div className="crm2-task-head"><span></span><span>#</span><span>Name</span><span>Status</span><span>Start Date</span><span>Due Date</span><span>Assigned to</span><span>Tags</span><span>Priority</span></div>
          {props.tasks.length === 0 ? <p className="crm2-reference-empty">No entries found</p> : props.tasks.map((item, index) => {
            const overdue = item.status === 'Open' && !!item.dueAtUtc && new Date(item.dueAtUtc) < new Date()
            const editing = editingTaskId === item.id
            const assignee = team.find(user => user.id === item.assigneeUserId)
            return <div className={'crm2-task-row-wrap' + (overdue ? ' overdue' : '')} key={item.id}>
              <div className="crm2-task-row">
                <span><input type="checkbox" /></span><span>{index + 1}</span>
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

  return (
    <section className="crm2-ref-list-page crm2-reports-reference">
      <div className="crm2-report-columns">
        <section>
          <h2>▧ Sales Report</h2>
          <details><summary>Invoices Report</summary><p>Open actions: {props.summary.openFollowUps + props.summary.openTasks}</p></details>
          <details><summary>Items Report</summary><p>CRM reporting workspace</p></details>
          <details><summary>Payments Received</summary><p>Use Sales → Payments for transaction-level details.</p></details>
          <details><summary>Credit Notes Report</summary><p>Use Sales → Credit Notes for document details.</p></details>
          <details><summary>Proposals Report</summary><p>Total leads: {props.dashboard.totalLeads}</p></details>
          <details><summary>Estimates Report</summary><p>Qualified leads: {props.dashboard.qualified}</p></details>
          <details><summary>Customers Report</summary><p>Converted leads: {props.dashboard.converted}</p></details>
        </section>
        <section>
          <h2>▥ Charts Based Report</h2>
          <details><summary>Total Income</summary><p>Open the detailed analytics workspace for revenue charts.</p></details>
          <details><summary>Payment Modes (Transactions)</summary><p>Use Sales → Payments for payment-mode details.</p></details>
          <details><summary>Total Value By Customer Groups</summary><p>Use Customers and Analytics for customer-group analysis.</p></details>
        </section>
      </div>
      <p className="crm2-report-note">ⓘ Cancelled/void records are excluded where the underlying report applies that rule.</p>
    </section>
  )

}
