import { useEffect, useMemo, useState } from 'react'
import './CrmDemo.css'
import './CrmAdvancedHub.css'
import { listCrmAccounts, listCrmTeam, type CrmAccount, type CrmTeamMember } from './crmApi'
import { listDetailedCrmOpportunities, type CrmDetailedOpportunity } from './crmOpportunityProductApi'
import { addCrmEntityActivity, listCrmEntityActivities, type CrmEntityActivity } from './crmEntityActivityApi'

type EntityType = 'Account' | 'Contact' | 'Opportunity'

function when(value: string) {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : new Intl.DateTimeFormat('en-IN', { dateStyle: 'medium', timeStyle: 'short' }).format(date)
}

export function CrmCommunicationHub() {
  const [accounts, setAccounts] = useState<CrmAccount[]>([])
  const [opportunities, setOpportunities] = useState<CrmDetailedOpportunity[]>([])
  const [team, setTeam] = useState<CrmTeamMember[]>([])
  const [entityType, setEntityType] = useState<EntityType>('Account')
  const [entityId, setEntityId] = useState('')
  const [activities, setActivities] = useState<CrmEntityActivity[]>([])
  const [channel, setChannel] = useState('Call')
  const [summary, setSummary] = useState('')
  const [details, setDetails] = useState('')
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState('Communication timeline ready')

  const contacts = useMemo(() => accounts.flatMap(account => account.contacts.map(contact => ({ ...contact, accountId: account.id, accountName: account.name }))), [accounts])
  const selectedContact = entityType === 'Contact' ? contacts.find(x => x.id === entityId) : undefined
  const actorName = (id?: string | null) => team.find(x => x.id === id)?.displayName || 'CRM user'

  const options = entityType === 'Account'
    ? accounts.map(x => ({ id: x.id, label: x.name }))
    : entityType === 'Contact'
      ? contacts.map(x => ({ id: x.id, label: `${x.name} · ${x.accountName}` }))
      : opportunities.map(x => ({ id: x.id, label: `${x.title} · ${x.stage}` }))

  async function bootstrap() {
    setBusy(true)
    try {
      const [accountResult, opportunityResult, teamResult] = await Promise.all([
        listCrmAccounts(), listDetailedCrmOpportunities(), listCrmTeam(),
      ])
      setAccounts(accountResult.accounts)
      setOpportunities(opportunityResult.opportunities)
      setTeam(teamResult.members)
      const firstId = entityType === 'Account' ? accountResult.accounts[0]?.id : ''
      if (!entityId && firstId) setEntityId(firstId)
      setMessage('Communication data loaded')
    } catch (error) { setMessage(error instanceof Error ? error.message : String(error)) }
    finally { setBusy(false) }
  }

  useEffect(() => { void bootstrap() }, [])

  useEffect(() => {
    const nextOptions = entityType === 'Account'
      ? accounts.map(x => x.id)
      : entityType === 'Contact'
        ? contacts.map(x => x.id)
        : opportunities.map(x => x.id)
    const next = nextOptions.includes(entityId) ? entityId : nextOptions[0] || ''
    if (next !== entityId) setEntityId(next)
    if (!next) setActivities([])
  }, [entityType, accounts, contacts, opportunities])

  useEffect(() => { if (entityId) void loadTimeline(entityType, entityId) }, [entityType, entityId])

  async function loadTimeline(type = entityType, id = entityId) {
    if (!id) return
    setBusy(true)
    try {
      const result = await listCrmEntityActivities(type, id)
      setActivities(result.activities)
      setMessage(`${result.activities.length} communication/activity record(s)`)
    } catch (error) { setMessage(error instanceof Error ? error.message : String(error)) }
    finally { setBusy(false) }
  }

  async function logActivity(openWhatsApp = false) {
    if (!entityId || !summary.trim()) { setMessage('Select a record and enter summary'); return }
    setBusy(true)
    try {
      await addCrmEntityActivity(entityType, entityId, { channel, summary: summary.trim(), details: details.trim() || undefined })
      if (openWhatsApp && selectedContact?.phone) {
        const digits = selectedContact.phone.replace(/\D/g, '')
        if (digits) window.open(`https://wa.me/${digits}`, '_blank', 'noopener,noreferrer')
      }
      setSummary(''); setDetails('')
      await loadTimeline()
      setMessage(`${channel} activity logged`)
    } catch (error) { setMessage(error instanceof Error ? error.message : String(error)); setBusy(false) }
  }

  return <div className="crm2-app crm-advanced-app">
    <aside className="crm2-sidebar">
      <div className="crm2-brand"><div className="crm2-brand-mark">o</div><div><strong>oRRbit</strong><span>BusinessOS</span></div></div>
      <nav className="crm2-nav">
        <a className="crm-advanced-back" href="/crm"><span>←</span>CRM Home</a>
        <a className="crm-advanced-back" href="/crm/advanced"><span>◎</span>Advanced CRM</a>
        <a className="crm-advanced-back" href="/crm/inbox"><span>!</span>My CRM Day</a>
      </nav>
      <div className="crm2-sidebar-foot"><strong>COMMUNICATION 360</strong><small>Account · Contact · Opportunity history</small></div>
    </aside>
    <main className="crm2-main">
      <header className="crm2-topbar"><div><span className="crm2-kicker">COMMUNICATION LAYER</span><h1>Customer & deal activity timeline</h1></div><div className="crm2-top-actions"><button className="crm2-refresh" disabled={busy} onClick={() => void bootstrap()}>↻ Refresh</button></div></header>
      <section className="crm2-statusbar"><div><span className={busy ? 'pulse busy' : 'pulse'} />{message}</div><span>Persistent PostgreSQL timeline</span></section>

      <section className="crm-advanced-grid" style={{ marginBottom: 18 }}>
        <article className="crm2-table-card">
          <div className="crm2-section-head"><div><span>RECORD</span><h2>Select timeline</h2></div></div>
          <div className="crm-advanced-form stacked">
            <select value={entityType} onChange={e => setEntityType(e.target.value as EntityType)}><option>Account</option><option>Contact</option><option>Opportunity</option></select>
            <select value={entityId} onChange={e => setEntityId(e.target.value)}><option value="">Select record</option>{options.map(x => <option key={x.id} value={x.id}>{x.label}</option>)}</select>
          </div>
        </article>
        <article className="crm2-table-card">
          <div className="crm2-section-head"><div><span>LOG ACTIVITY</span><h2>Communication / note</h2></div></div>
          <div className="crm-advanced-form stacked">
            <select value={channel} onChange={e => setChannel(e.target.value)}><option>Call</option><option>WhatsApp</option><option>Email</option><option>Meeting</option><option>Note</option><option>Other</option></select>
            <input value={summary} onChange={e => setSummary(e.target.value)} placeholder="Outcome / activity summary" />
            <textarea rows={3} value={details} onChange={e => setDetails(e.target.value)} placeholder="Detailed communication notes" />
            <div className="crm2-top-actions"><button className="crm2-primary" disabled={busy || !entityId || !summary.trim()} onClick={() => void logActivity(false)}>Save activity</button>{entityType === 'Contact' && channel === 'WhatsApp' && selectedContact?.phone ? <button disabled={busy} onClick={() => void logActivity(true)}>Save & open WhatsApp</button> : null}</div>
          </div>
        </article>
      </section>

      <section className="crm2-table-card">
        <div className="crm2-section-head"><div><span>CHRONOLOGICAL HISTORY</span><h2>{activities.length} activity record(s)</h2></div></div>
        <div className="crm-advanced-list">{activities.map(item => <article key={item.id}><div><b>{item.channel}</b><strong>{item.summary}</strong><small>{item.details || 'No additional notes'} · by {actorName(item.actorUserId)}</small></div><span>{when(item.occurredAtUtc)}</span></article>)}{!activities.length ? <p>No communication history for this record yet.</p> : null}</div>
      </section>
    </main>
  </div>
}
