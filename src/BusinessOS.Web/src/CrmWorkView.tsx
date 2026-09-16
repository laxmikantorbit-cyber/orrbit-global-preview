import { useState } from 'react'
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
  teamMembers: CrmTeamMember[]
  currentUserId?: string
  canViewAllOwnedRecords: boolean
  summary: CrmWorkSummary
  dashboard: CrmDashboard
  busy: boolean
  openLead: (leadId: string) => void
  completeFollowUp: (id: string) => Promise<void>
  rescheduleFollowUp: (id: string, input: FollowUpUpdate) => Promise<void>
  cancelFollowUp: (id: string) => Promise<void>
  completeTask: (id: string) => Promise<void>
  updateTask: (id: string, input: TaskUpdate) => Promise<void>
  cancelTask: (id: string) => Promise<void>
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

  const leadTitle = (id?: string | null) => props.leads.find((x) => x.id === id)?.title || 'General task'
  const assignableUsers = props.canViewAllOwnedRecords
    ? props.teamMembers.filter((x) => x.active)
    : props.teamMembers.filter((x) => x.active && x.id === props.currentUserId)

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
                  <select value={followOwner} onChange={(e) => setFollowOwner(e.target.value)}><option value="">Unassigned</option>{assignableUsers.map((user) => <option key={user.id} value={user.id}>{user.displayName} · {user.role}</option>)}</select>
                  <div className="crm2-top-actions"><button className="crm2-primary" disabled={props.busy || !followDue || !followPurpose.trim()} onClick={() => void props.rescheduleFollowUp(item.id, { dueAtUtc: utcInput(followDue)!, channel: followChannel, purpose: followPurpose, ownerUserId: followOwner || null }).then(() => setEditingFollowUpId(null))}>Save</button><button className="crm2-refresh" onClick={() => setEditingFollowUpId(null)}>Close</button></div>
                </div> : null}
              </div>
              <em className={item.status.toLowerCase()}>{overdue ? 'Overdue' : item.status}</em>
              {item.status === 'Open' ? <div className="crm2-top-actions"><button disabled={props.busy} onClick={() => editFollowUp(item)}>Reschedule</button><button disabled={props.busy} onClick={() => void props.completeFollowUp(item.id)}>Complete</button><button disabled={props.busy} onClick={() => void props.cancelFollowUp(item.id)}>Cancel</button></div> : null}
            </article>
          })}
        </div>
      </section>
    )
  }

  if (props.view === 'tasks') {
    const open = props.tasks.filter((x) => x.status === 'Open')
    return (
      <section className="crm2-module-page">
        <div className="crm2-module-head"><div><span className="crm2-kicker">TASK CENTRE</span><h2>Sales tasks</h2><p>Edit, reassign, complete and cancel tasks from the same workspace.</p></div><div className="crm2-module-stat"><strong>{open.length}</strong><span>open</span></div></div>
        <div className="crm2-summary-strip"><div><span>Open tasks</span><b>{props.summary.openTasks}</b></div><div className="danger"><span>Overdue</span><b>{props.summary.overdueTasks}</b></div><div><span>Completed</span><b>{props.tasks.filter((x) => x.status === 'Completed').length}</b></div></div>
        <div className="crm2-module-list">
          {props.tasks.length === 0 ? <div className="crm2-empty"><strong>No tasks yet</strong><span>Create tasks from any lead workspace.</span></div> : props.tasks.map((item) => {
            const overdue = item.status === 'Open' && !!item.dueAtUtc && new Date(item.dueAtUtc) < new Date()
            const editing = editingTaskId === item.id
            return <article key={item.id} className={overdue ? 'overdue' : ''} style={{ alignItems: editing ? 'flex-start' : undefined }}>
              {item.leadId ? <button className="crm2-link-button" onClick={() => props.openLead(item.leadId!)}>{leadTitle(item.leadId)}</button> : <span className="crm2-link-label">General</span>}
              <div style={{ flex: 1 }}>
                <strong>{item.title}</strong><span>{item.priority} · {formatDate(item.dueAtUtc)}</span>{item.details ? <small>{item.details}</small> : null}
                {editing ? <div className="crm-advanced-form stacked" style={{ marginTop: 12 }}>
                  <input value={taskTitle} onChange={(e) => setTaskTitle(e.target.value)} placeholder="Task title" />
                  <textarea rows={3} value={taskDetails} onChange={(e) => setTaskDetails(e.target.value)} placeholder="Task details" />
                  <input type="datetime-local" value={taskDue} onChange={(e) => setTaskDue(e.target.value)} />
                  <select value={taskPriority} onChange={(e) => setTaskPriority(e.target.value)}><option>Low</option><option>Normal</option><option>High</option><option>Urgent</option></select>
                  <select value={taskAssignee} onChange={(e) => setTaskAssignee(e.target.value)}><option value="">Unassigned</option>{assignableUsers.map((user) => <option key={user.id} value={user.id}>{user.displayName} · {user.role}</option>)}</select>
                  <div className="crm2-top-actions"><button className="crm2-primary" disabled={props.busy || !taskTitle.trim()} onClick={() => void props.updateTask(item.id, { title: taskTitle, details: taskDetails, dueAtUtc: utcInput(taskDue), priority: taskPriority, assigneeUserId: taskAssignee || null }).then(() => setEditingTaskId(null))}>Save</button><button className="crm2-refresh" onClick={() => setEditingTaskId(null)}>Close</button></div>
                </div> : null}
              </div>
              <em className={item.status.toLowerCase()}>{overdue ? 'Overdue' : item.status}</em>
              {item.status === 'Open' ? <div className="crm2-top-actions"><button disabled={props.busy} onClick={() => editTask(item)}>Edit</button><button disabled={props.busy} onClick={() => void props.completeTask(item.id)}>Complete</button><button disabled={props.busy} onClick={() => void props.cancelTask(item.id)}>Cancel</button></div> : null}
            </article>
          })}
        </div>
      </section>
    )
  }

  const total = props.dashboard.totalLeads || 1
  const conversion = Math.round((props.dashboard.converted / total) * 100)
  const qualification = Math.round((props.dashboard.qualified / total) * 100)
  return (
    <section className="crm2-module-page">
      <div className="crm2-module-head"><div><span className="crm2-kicker">CRM REPORTS</span><h2>Sales performance</h2><p>Live staging metrics from the current CRM dataset.</p></div></div>
      <div className="crm2-report-grid">
        <article><span>Total leads</span><strong>{props.dashboard.totalLeads}</strong><small>All captured leads</small></article>
        <article><span>Conversion rate</span><strong>{conversion}%</strong><small>{props.dashboard.converted} converted</small></article>
        <article><span>Qualification rate</span><strong>{qualification}%</strong><small>{props.dashboard.qualified} currently qualified</small></article>
        <article><span>Open actions</span><strong>{props.summary.openFollowUps + props.summary.openTasks}</strong><small>Follow-ups + tasks</small></article>
      </div>
      <div className="crm2-funnel-report">
        {[
          ['New', props.dashboard.new], ['Contacted', props.dashboard.contacted],
          ['Qualified', props.dashboard.qualified], ['Converted', props.dashboard.converted],
          ['Unqualified', props.dashboard.unqualified],
        ].map(([label, value]) => {
          const count = Number(value)
          const width = Math.max(3, Math.round((count / total) * 100))
          return <div key={String(label)}><span>{label}</span><i><b style={{ width: `${width}%` }} /></i><strong>{count}</strong></div>
        })}
      </div>
    </section>
  )
}
