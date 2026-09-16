import { useEffect, useState } from 'react'
import './CrmDemo.css'
import './CrmAdvancedHub.css'
import { getCrmSession, type CrmSession } from './crmApi'
import {
  getCrmDailySummary,
  getCrmNotificationInbox,
  markAllCrmNotificationsRead,
  setCrmNotificationRead,
  type CrmDailySummary,
  type CrmPersistentNotification,
} from './crmNotificationApi'

function when(value?: string | null) {
  if (!value) return 'No due time'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : new Intl.DateTimeFormat('en-IN', { dateStyle: 'medium', timeStyle: 'short' }).format(date)
}

function money(value: number) {
  return new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR', maximumFractionDigits: 0 }).format(value || 0)
}

export function CrmInboxHub() {
  const [session, setSession] = useState<CrmSession | null>(null)
  const [summary, setSummary] = useState<CrmDailySummary | null>(null)
  const [items, setItems] = useState<CrmPersistentNotification[]>([])
  const [unread, setUnread] = useState(0)
  const [showRead, setShowRead] = useState(true)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState('Notification inbox ready')

  async function refresh(includeRead = showRead) {
    setBusy(true)
    try {
      const [sessionResult, inbox, daily] = await Promise.all([
        getCrmSession(), getCrmNotificationInbox(includeRead), getCrmDailySummary(),
      ])
      setSession(sessionResult)
      setItems(inbox.items)
      setUnread(inbox.unreadCount)
      setSummary(daily)
      setMessage(`${inbox.unreadCount} unread · ${inbox.totalCount} active notification(s)`)
    } catch (error) {
      setMessage(error instanceof Error ? error.message : String(error))
    } finally { setBusy(false) }
  }

  useEffect(() => { void refresh() }, [])

  async function toggle(item: CrmPersistentNotification) {
    setBusy(true)
    try {
      await setCrmNotificationRead(item.id, !item.isRead)
      await refresh()
    } catch (error) { setMessage(error instanceof Error ? error.message : String(error)); setBusy(false) }
  }

  async function markAll() {
    setBusy(true)
    try {
      const result = await markAllCrmNotificationsRead()
      setMessage(`${result.markedRead} notification(s) marked read`)
      await refresh()
    } catch (error) { setMessage(error instanceof Error ? error.message : String(error)); setBusy(false) }
  }

  return <div className="crm2-app crm-advanced-app">
    <aside className="crm2-sidebar">
      <div className="crm2-brand"><div className="crm2-brand-mark">o</div><div><strong>oRRbit</strong><span>BusinessOS</span></div></div>
      <nav className="crm2-nav">
        <a className="crm-advanced-back" href="/crm"><span>←</span>CRM Home</a>
        <a className="crm-advanced-back" href="/crm/advanced"><span>◎</span>Advanced CRM</a>
        <a className="crm-advanced-back" href="/crm/analytics"><span>↗</span>Analytics</a>
      </nav>
      <div className="crm2-sidebar-foot"><strong>PERSISTENT ACTION CENTRE</strong><small>Unread state · reminders · daily summary</small></div>
    </aside>

    <main className="crm2-main">
      <header className="crm2-topbar"><div><span className="crm2-kicker">MY CRM DAY</span><h1>Notification centre & daily summary</h1></div><div className="crm2-top-actions"><button className="crm2-refresh" disabled={busy} onClick={() => void refresh()}>↻ Refresh</button><button className="crm2-primary" disabled={busy || unread === 0} onClick={() => void markAll()}>Mark all read</button></div></header>
      <section className="crm2-statusbar"><div><span className={busy ? 'pulse busy' : 'pulse'} />{message}</div><span>{session ? `${session.member.displayName} · ${session.canViewAllOwnedRecords ? 'Team scope' : 'My scope'}` : 'CRM'}</span></section>

      {summary ? <>
        <section className="crm2-metrics crm-advanced-metrics">
          <article><span>New leads</span><strong>{summary.newLeads}</strong><small>{summary.qualifiedLeads} qualified</small></article>
          <article className={summary.overdueFollowUps ? 'accent' : ''}><span>Follow-ups</span><strong>{summary.dueTodayFollowUps}</strong><small>{summary.overdueFollowUps} overdue</small></article>
          <article className={summary.overdueTasks ? 'accent' : ''}><span>Tasks due today</span><strong>{summary.dueTodayTasks}</strong><small>{summary.overdueTasks} overdue</small></article>
          <article><span>Weighted pipeline</span><strong>{money(summary.weightedPipelineValue)}</strong><small>{summary.openOpportunities} open deals</small></article>
        </section>
        <section className="crm2-table-card" style={{ marginBottom: 18 }}>
          <div className="crm2-section-head"><div><span>TOP ACTIONS</span><h2>What needs attention first</h2></div></div>
          <div className="crm-advanced-list">{summary.topActions.map(item => <article key={`top-${item.id}`} className={item.severity === 'High' ? 'danger-card' : ''}><div><b>{item.type}</b><strong>{item.title}</strong><small>{item.detail}</small></div><span>{when(item.dueAtUtc)}</span></article>)}{!summary.topActions.length ? <p>No urgent actions right now.</p> : null}</div>
        </section>
      </> : null}

      <section className="crm2-table-card">
        <div className="crm2-section-head"><div><span>INBOX</span><h2>{unread} unread notification(s)</h2></div><label><input type="checkbox" checked={showRead} onChange={e => { setShowRead(e.target.checked); void refresh(e.target.checked) }} /> Show read</label></div>
        <div className="crm-advanced-list">
          {items.map(item => <article key={item.id} className={`${item.severity === 'High' ? 'danger-card' : ''} ${item.isRead ? 'is-read' : ''}`}>
            <div><b>{item.isRead ? 'Read' : 'Unread'} · {item.type}</b><strong>{item.title}</strong><small>{item.detail} · {when(item.dueAtUtc)}</small></div>
            <button disabled={busy} onClick={() => void toggle(item)}>{item.isRead ? 'Mark unread' : 'Mark read'}</button>
          </article>)}
          {!items.length ? <p>No active notifications.</p> : null}
        </div>
      </section>
    </main>
  </div>
}
