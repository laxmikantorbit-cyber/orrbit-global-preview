import { useMemo, useState } from 'react'
import {
  changeCrmTimesheetStatus,
  createCrmTimesheet,
  updateCrmTimesheet,
  type CrmAccount,
  type CrmBusinessRecord,
  type CrmTask,
  type CrmTeamMember,
  type CrmTimesheet,
} from './crmApi'
import { exportCrmSpreadsheet, type CrmSpreadsheetFormat } from './crmSpreadsheet'

type Props = {
  timesheets: CrmTimesheet[]
  accounts: CrmAccount[]
  businessRecords: CrmBusinessRecord[]
  tasks: CrmTask[]
  teamMembers: CrmTeamMember[]
  currentUserId?: string
  canManage: boolean
  canReview: boolean
  busy: boolean
  refresh: () => Promise<void>
  notify: (message: string) => void
}

const today = () => new Date().toISOString().slice(0, 10)
const hours = (minutes: number) => Math.floor(minutes / 60) + 'h ' + (minutes % 60) + 'm'

export function CrmTimesheetsView({
  timesheets, accounts, businessRecords, tasks, teamMembers, currentUserId, canManage, canReview,
  busy, refresh, notify,
}: Props) {
  const [query, setQuery] = useState('')
  const [statusFilter, setStatusFilter] = useState('All')
  const [userFilter, setUserFilter] = useState('All')
  const [billableFilter, setBillableFilter] = useState('All')
  const [pageSize, setPageSize] = useState(25)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [open, setOpen] = useState(false)
  const [userId, setUserId] = useState(currentUserId || '')
  const [workDate, setWorkDate] = useState(today())
  const [minutes, setMinutes] = useState('60')
  const [activity, setActivity] = useState('')
  const [billable, setBillable] = useState(true)
  const [projectId, setProjectId] = useState('')
  const [taskId, setTaskId] = useState('')
  const [accountId, setAccountId] = useState('')
  const [notes, setNotes] = useState('')

  const projects = useMemo(
    () => businessRecords.filter(x => x.module === 'Project' && !['Completed', 'Cancelled'].includes(x.status)),
    [businessRecords],
  )
  const activeUsers = useMemo(
    () => teamMembers.filter(x => x.active && (canReview || x.id === currentUserId)),
    [canReview, currentUserId, teamMembers],
  )
  const visibleTasks = useMemo(
    () => tasks.filter(x => x.status === 'Open' && (!userId || !x.assigneeUserId || x.assigneeUserId === userId)),
    [tasks, userId],
  )

  const filtered = useMemo(() => {
    const needle = query.trim().toLowerCase()
    return timesheets.filter(entry => {
      const member = teamMembers.find(x => x.id === entry.userId)
      const project = businessRecords.find(x => x.id === entry.projectId)
      const task = tasks.find(x => x.id === entry.taskId)
      const account = accounts.find(x => x.id === entry.accountId)
      const search = [entry.activity, entry.notes, entry.status, member?.displayName, project?.title, task?.title, account?.name]
        .filter(Boolean).join(' ').toLowerCase()
      return (statusFilter === 'All' || entry.status === statusFilter)
        && (userFilter === 'All' || entry.userId === userFilter)
        && (billableFilter === 'All' || (billableFilter === 'Billable' ? entry.billable : !entry.billable))
        && (!needle || search.includes(needle))
    })
  }, [accounts, billableFilter, businessRecords, query, statusFilter, tasks, teamMembers, timesheets, userFilter])

  const visible = filtered.slice(0, pageSize)
  const totalMinutes = filtered.reduce((sum, x) => sum + x.minutes, 0)
  const billableMinutes = filtered.filter(x => x.billable).reduce((sum, x) => sum + x.minutes, 0)
  const approvedMinutes = filtered.filter(x => x.status === 'Approved').reduce((sum, x) => sum + x.minutes, 0)
  const submittedCount = filtered.filter(x => x.status === 'Submitted').length

  function reset() {
    setEditingId(null)
    setUserId(currentUserId || activeUsers[0]?.id || '')
    setWorkDate(today())
    setMinutes('60')
    setActivity('')
    setBillable(true)
    setProjectId('')
    setTaskId('')
    setAccountId('')
    setNotes('')
  }

  function openNew() {
    reset()
    setOpen(true)
  }

  function openEdit(entry: CrmTimesheet) {
    if (!['Draft', 'Rejected'].includes(entry.status)) {
      notify('Only Draft or Rejected timesheets can be edited')
      return
    }
    setEditingId(entry.id)
    setUserId(entry.userId)
    setWorkDate(entry.workDate)
    setMinutes(String(entry.minutes))
    setActivity(entry.activity)
    setBillable(entry.billable)
    setProjectId(entry.projectId || '')
    setTaskId(entry.taskId || '')
    setAccountId(entry.accountId || '')
    setNotes(entry.notes || '')
    setOpen(true)
  }

  async function save() {
    const duration = Number(minutes)
    if (!userId) { notify('Select a team member'); return }
    if (!workDate) { notify('Work date is required'); return }
    if (!activity.trim()) { notify('Activity is required'); return }
    if (!Number.isInteger(duration) || duration < 1 || duration > 1440) {
      notify('Duration must be between 1 and 1440 minutes'); return
    }
    const payload = {
      userId,
      workDate,
      minutes: duration,
      activity: activity.trim(),
      billable,
      projectId: projectId || null,
      taskId: taskId || null,
      accountId: accountId || null,
      notes: notes.trim() || undefined,
    }
    try {
      if (editingId) {
        await updateCrmTimesheet(editingId, payload)
        notify('Timesheet updated')
      } else {
        await createCrmTimesheet(payload)
        notify('Timesheet saved as Draft')
      }
      setOpen(false)
      reset()
      await refresh()
    } catch (error) {
      notify(error instanceof Error ? error.message : String(error))
    }
  }

  async function move(entry: CrmTimesheet, status: CrmTimesheet['status']) {
    try {
      const updated = await changeCrmTimesheetStatus(entry.id, status)
      notify('Timesheet moved to ' + updated.status)
      await refresh()
    } catch (error) {
      notify(error instanceof Error ? error.message : String(error))
    }
  }

  async function exportRows(format: CrmSpreadsheetFormat) {
    await exportCrmSpreadsheet('crm-timesheets-' + today(), {
      headers: ['Date', 'Team Member', 'Activity', 'Duration Minutes', 'Duration', 'Billable', 'Project', 'Task', 'Customer', 'Status', 'Notes'],
      rows: filtered.map(entry => [
        entry.workDate,
        teamMembers.find(x => x.id === entry.userId)?.displayName || entry.userId,
        entry.activity,
        entry.minutes,
        hours(entry.minutes),
        entry.billable ? 'Yes' : 'No',
        businessRecords.find(x => x.id === entry.projectId)?.title || '',
        tasks.find(x => x.id === entry.taskId)?.title || '',
        accounts.find(x => x.id === entry.accountId)?.name || '',
        entry.status,
        entry.notes || '',
      ]),
    }, format)
    notify('Exported ' + filtered.length + ' timesheet row(s)')
  }

  return <section className="crm2-ref-list-page">
    <div className="crm2-ref-action-row">
      {canManage ? <button className="crm2-ref-primary" onClick={openNew}>+ Add Timesheet</button> : null}
      <span className="crm2-action-spacer" />
      <button disabled={busy || filtered.length === 0} onClick={() => void exportRows('xlsx')}>Export XLSX</button>
      <button disabled={busy || filtered.length === 0} onClick={() => void exportRows('csv')}>CSV</button>
    </div>

    <section className="crm2-metrics">
      <article><span>Visible time</span><strong>{hours(totalMinutes)}</strong><small>{filtered.length} entries</small></article>
      <article><span>Billable time</span><strong>{hours(billableMinutes)}</strong><small>{totalMinutes ? Math.round(billableMinutes * 100 / totalMinutes) : 0}% of visible</small></article>
      <article><span>Awaiting approval</span><strong>{submittedCount}</strong><small>Submitted entries</small></article>
      <article className="accent"><span>Approved time</span><strong>{hours(approvedMinutes)}</strong><small>Finalized work</small></article>
    </section>

    <section className="crm2-ref-table-card">
      <div className="crm2-ref-table-tools">
        <select value={pageSize} onChange={e => setPageSize(Number(e.target.value))}><option value={25}>25</option><option value={50}>50</option><option value={100}>100</option></select>
        <button onClick={() => void refresh()} disabled={busy}>↻</button>
        <select value={statusFilter} onChange={e => setStatusFilter(e.target.value)}><option>All</option><option>Draft</option><option>Submitted</option><option>Approved</option><option>Rejected</option></select>
        <select value={userFilter} onChange={e => setUserFilter(e.target.value)}><option value="All">All team members</option>{activeUsers.map(x => <option key={x.id} value={x.id}>{x.displayName}</option>)}</select>
        <select value={billableFilter} onChange={e => setBillableFilter(e.target.value)}><option>All</option><option>Billable</option><option>Non-billable</option></select>
        <span /><label><b>⌕</b><input value={query} onChange={e => setQuery(e.target.value)} placeholder="Search timesheets..." /></label>
      </div>

      <div className="crm-advanced-list">
        {visible.map(entry => {
          const member = teamMembers.find(x => x.id === entry.userId)
          const project = businessRecords.find(x => x.id === entry.projectId)
          const task = tasks.find(x => x.id === entry.taskId)
          return <article key={entry.id}>
            <div>
              <b>{entry.workDate} · {member?.displayName || 'Unknown user'} · {entry.billable ? 'Billable' : 'Non-billable'}</b>
              <strong>{entry.activity}</strong>
              <small>{project ? 'Project: ' + project.title : 'No project'}{task ? ' · Task: ' + task.title : ''}{entry.notes ? ' · ' + entry.notes : ''}</small>
              <small>
                {canManage && ['Draft', 'Rejected'].includes(entry.status) ? <><button onClick={() => openEdit(entry)}>Edit</button> <button onClick={() => void move(entry, 'Submitted')}>Submit</button></> : null}
                {entry.status === 'Submitted' && canReview ? <><button onClick={() => void move(entry, 'Approved')}>Approve</button> <button onClick={() => void move(entry, 'Rejected')}>Reject</button></> : null}
              </small>
            </div>
            <span><strong>{hours(entry.minutes)}</strong><small>{entry.status}</small></span>
          </article>
        })}
        {visible.length === 0 ? <p>No timesheet entries found.</p> : null}
      </div>
    </section>

    {open ? <div className="crm2-overlay" onMouseDown={() => setOpen(false)}>
      <section className="crm2-drawer crm2-wide-drawer" onMouseDown={e => e.stopPropagation()}>
        <div className="crm2-drawer-head"><div><span className="crm2-kicker">{editingId ? 'EDIT' : 'NEW'} TIMESHEET</span><h2>Record work time</h2></div><button onClick={() => setOpen(false)}>×</button></div>
        <div className="crm2-form-grid">
          <label>Team member<select value={userId} disabled={!canReview} onChange={e => setUserId(e.target.value)}><option value="">Select user</option>{activeUsers.map(x => <option key={x.id} value={x.id}>{x.displayName}</option>)}</select></label>
          <label>Work date<input type="date" value={workDate} onChange={e => setWorkDate(e.target.value)} /></label>
          <label>Duration (minutes)<input type="number" min={1} max={1440} value={minutes} onChange={e => setMinutes(e.target.value)} /></label>
          <label>Billable<select value={billable ? 'Yes' : 'No'} onChange={e => setBillable(e.target.value === 'Yes')}><option>Yes</option><option>No</option></select></label>
          <label>Project<select value={projectId} onChange={e => setProjectId(e.target.value)}><option value="">No project</option>{projects.map(x => <option key={x.id} value={x.id}>{x.title}</option>)}</select></label>
          <label>Task<select value={taskId} onChange={e => setTaskId(e.target.value)}><option value="">No task</option>{visibleTasks.map(x => <option key={x.id} value={x.id}>{x.title}</option>)}</select></label>
          <label>Customer<select value={accountId} onChange={e => setAccountId(e.target.value)}><option value="">No customer</option>{accounts.filter(x => x.status === 'Active').map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
        </div>
        <label>Activity<input value={activity} onChange={e => setActivity(e.target.value)} placeholder="e.g. Customer demo, follow-up, implementation" /></label>
        <label>Notes<textarea rows={4} value={notes} onChange={e => setNotes(e.target.value)} /></label>
        <div className="crm2-drawer-actions"><button onClick={() => setOpen(false)}>Cancel</button><button className="crm2-primary" disabled={busy} onClick={() => void save()}>Save Draft</button></div>
      </section>
    </div> : null}
  </section>
}
