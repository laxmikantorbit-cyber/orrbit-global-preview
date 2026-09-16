import type { CrmDashboard, CrmFollowUp, CrmLead, CrmTask, CrmWorkSummary } from './crmApi'

type View = 'followups' | 'tasks' | 'reports'

type Props = {
  view: View
  leads: CrmLead[]
  followUps: CrmFollowUp[]
  tasks: CrmTask[]
  summary: CrmWorkSummary
  dashboard: CrmDashboard
  busy: boolean
  openLead: (leadId: string) => void
  completeFollowUp: (id: string) => Promise<void>
  completeTask: (id: string) => Promise<void>
}

function formatDate(value?: string | null) {
  if (!value) return 'No due date'
  const parsed = new Date(value)
  if (Number.isNaN(parsed.getTime())) return value
  return new Intl.DateTimeFormat('en-IN', { dateStyle: 'medium', timeStyle: 'short' }).format(parsed)
}

export function CrmWorkView(props: Props) {
  const leadTitle = (id?: string | null) => props.leads.find((x) => x.id === id)?.title || 'General task'
  if (props.view === 'followups') {
    const open = props.followUps.filter((x) => x.status === 'Open')
    return (
      <section className="crm2-module-page">
        <div className="crm2-module-head"><div><span className="crm2-kicker">FOLLOW-UP CENTRE</span><h2>Customer follow-ups</h2><p>Calls, WhatsApp, email and meeting reminders in one queue.</p></div><div className="crm2-module-stat"><strong>{open.length}</strong><span>open</span></div></div>
        <div className="crm2-summary-strip"><div><span>Open</span><b>{props.summary.openFollowUps}</b></div><div className="danger"><span>Overdue</span><b>{props.summary.overdueFollowUps}</b></div><div><span>Due today</span><b>{props.summary.dueTodayFollowUps}</b></div></div>
        <div className="crm2-module-list">
          {props.followUps.length === 0 ? <div className="crm2-empty"><strong>No follow-ups yet</strong><span>Open a lead and schedule the first follow-up.</span></div> : props.followUps.map((item) => {
            const overdue = item.status === 'Open' && new Date(item.dueAtUtc) < new Date()
            return <article key={item.id} className={overdue ? 'overdue' : ''}>
              <button className="crm2-link-button" onClick={() => props.openLead(item.leadId)}>{leadTitle(item.leadId)}</button>
              <div><strong>{item.purpose}</strong><span>{item.channel} · {formatDate(item.dueAtUtc)}</span>{item.outcome ? <small>{item.outcome}</small> : null}</div>
              <em className={item.status.toLowerCase()}>{overdue ? 'Overdue' : item.status}</em>
              {item.status === 'Open' ? <button disabled={props.busy} onClick={() => void props.completeFollowUp(item.id)}>Complete</button> : null}
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
        <div className="crm2-module-head"><div><span className="crm2-kicker">TASK CENTRE</span><h2>Sales tasks</h2><p>Action items linked to leads and daily sales execution.</p></div><div className="crm2-module-stat"><strong>{open.length}</strong><span>open</span></div></div>
        <div className="crm2-summary-strip"><div><span>Open tasks</span><b>{props.summary.openTasks}</b></div><div className="danger"><span>Overdue</span><b>{props.summary.overdueTasks}</b></div><div><span>Completed</span><b>{props.tasks.filter((x) => x.status === 'Completed').length}</b></div></div>
        <div className="crm2-module-list">
          {props.tasks.length === 0 ? <div className="crm2-empty"><strong>No tasks yet</strong><span>Create tasks from any lead workspace.</span></div> : props.tasks.map((item) => {
            const overdue = item.status === 'Open' && !!item.dueAtUtc && new Date(item.dueAtUtc) < new Date()
            return <article key={item.id} className={overdue ? 'overdue' : ''}>
              {item.leadId ? <button className="crm2-link-button" onClick={() => props.openLead(item.leadId!)}>{leadTitle(item.leadId)}</button> : <span className="crm2-link-label">General</span>}
              <div><strong>{item.title}</strong><span>{item.priority} · {formatDate(item.dueAtUtc)}</span>{item.details ? <small>{item.details}</small> : null}</div>
              <em className={item.status.toLowerCase()}>{overdue ? 'Overdue' : item.status}</em>
              {item.status === 'Open' ? <button disabled={props.busy} onClick={() => void props.completeTask(item.id)}>Complete</button> : null}
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
