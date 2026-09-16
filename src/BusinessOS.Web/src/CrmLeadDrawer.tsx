import { useEffect, useState } from 'react'
import {
  addCrmActivity,
  changeCrmLeadPriority,
  completeCrmFollowUp,
  completeCrmTask,
  createCrmFollowUp,
  createCrmTask,
  convertCrmLead,
  getCrmLeadWorkspace,
  updateCrmLeadProfile,
  type CrmLeadWorkspace,
} from './crmApi'

type DrawerTab = 'profile' | 'activity' | 'followups' | 'tasks' | 'convert'

type Props = {
  leadId: string
  onClose: () => void
  onChanged: () => Promise<void>
  notify: (message: string) => void
}

function formatDate(value?: string | null) {
  if (!value) return '—'
  const parsed = new Date(value)
  if (Number.isNaN(parsed.getTime())) return value
  return new Intl.DateTimeFormat('en-IN', { dateStyle: 'medium', timeStyle: 'short' }).format(parsed)
}
function defaultFollowUpTime() {
  const date = new Date(Date.now() + 24 * 60 * 60 * 1000)
  const offset = date.getTimezoneOffset() * 60_000
  return new Date(date.getTime() - offset).toISOString().slice(0, 16)
}

export function CrmLeadDrawer({ leadId, onClose, onChanged, notify }: Props) {
  const [workspace, setWorkspace] = useState<CrmLeadWorkspace | null>(null)
  const [tab, setTab] = useState<DrawerTab>('profile')
  const [busy, setBusy] = useState(false)
  const [title, setTitle] = useState('')
  const [contactName, setContactName] = useState('')
  const [mobile, setMobile] = useState('')
  const [email, setEmail] = useState('')
  const [product, setProduct] = useState('')
  const [notes, setNotes] = useState('')
  const [activityType, setActivityType] = useState('Note')
  const [activitySummary, setActivitySummary] = useState('')
  const [activityDetails, setActivityDetails] = useState('')
  const [followUpChannel, setFollowUpChannel] = useState('Call')
  const [followUpPurpose, setFollowUpPurpose] = useState('Follow up with customer')
  const [followUpAt, setFollowUpAt] = useState(defaultFollowUpTime())
  const [taskTitle, setTaskTitle] = useState('')
  const [taskDetails, setTaskDetails] = useState('')
  const [taskPriority, setTaskPriority] = useState('Normal')
  const [accountName, setAccountName] = useState('')
  const [opportunityTitle, setOpportunityTitle] = useState('')
  const [estimatedValue, setEstimatedValue] = useState('29999')
  const [probability, setProbability] = useState('60')
  const [expectedCloseDate, setExpectedCloseDate] = useState('')
  async function load() {
    setBusy(true)
    try {
      const data = await getCrmLeadWorkspace(leadId)
      setWorkspace(data)
      setTitle(data.lead.title)
      setContactName(data.lead.contactName || '')
      setMobile(data.lead.mobileNumber || '')
      setEmail(data.lead.email || '')
      setProduct(data.lead.productInterest || '')
      setNotes(data.lead.notes || '')
      setAccountName((current) => current || data.lead.title)
      setOpportunityTitle((current) => current || (data.lead.productInterest || data.lead.title) + ' Deal')
    } catch (error) {
      notify(error instanceof Error ? error.message : String(error))
    } finally {
      setBusy(false)
    }
  }

  useEffect(() => { void load() }, [leadId])

  async function run(action: () => Promise<unknown>, success: string) {
    setBusy(true)
    try {
      await action()
      notify(success)
      await Promise.all([load(), onChanged()])
    } catch (error) {
      notify(error instanceof Error ? error.message : String(error))
      setBusy(false)
    }
  }

  if (!workspace) {
    return <div className="crm2-overlay"><section className="crm2-drawer"><div className="crm2-loading">Loading lead workspace…</div></section></div>
  }

  const lead = workspace.lead
  return (
    <div className="crm2-overlay" onMouseDown={onClose}>
      <section className="crm2-drawer crm2-detail crm2-wide-drawer" onMouseDown={(e) => e.stopPropagation()}>
        <div className="crm2-drawer-head">
          <div><span className="crm2-kicker">LEAD WORKSPACE</span><h2>{lead.title}</h2></div>
          <button onClick={onClose}>×</button>
        </div>

        <div className="crm2-detail-summary">
          <div><span>Stage</span><strong>{lead.status}</strong></div>
          <div><span>Priority</span><strong>{lead.priority || 'Normal'}</strong></div>
          <div><span>Next follow-up</span><strong>{formatDate(lead.nextFollowUpAtUtc)}</strong></div>
          <div><span>Last contact</span><strong>{formatDate(lead.lastContactAtUtc)}</strong></div>
        </div>

        <div className="crm2-tabs">
          {(['profile', 'activity', 'followups', 'tasks', 'convert'] as DrawerTab[]).map((item) => (
            <button key={item} className={tab === item ? 'active' : ''} onClick={() => setTab(item)}>{item}</button>
          ))}
        </div>

        {tab === 'profile' ? (
          <div className="crm2-form-section">
            <div className="crm2-form-grid">
              <label>Business / lead name<input value={title} onChange={(e) => setTitle(e.target.value)} /></label>
              <label>Contact person<input value={contactName} onChange={(e) => setContactName(e.target.value)} /></label>
              <label>Mobile<input value={mobile} onChange={(e) => setMobile(e.target.value)} /></label>
              <label>Email<input type="email" value={email} onChange={(e) => setEmail(e.target.value)} /></label>
              <label>Product interest<input value={product} onChange={(e) => setProduct(e.target.value)} placeholder="AI Repair, School, etc." /></label>
              <label>Priority<select value={lead.priority || 'Normal'} disabled={busy} onChange={(e) => void run(() => changeCrmLeadPriority(lead.id, e.target.value), 'Priority updated')}><option>Low</option><option>Normal</option><option>High</option><option>Urgent</option></select></label>
            </div>
            <label>Internal notes<textarea value={notes} onChange={(e) => setNotes(e.target.value)} rows={4} /></label>
            <div className="crm2-drawer-actions">
              <button className="crm2-primary" disabled={busy || !title.trim()} onClick={() => void run(
                () => updateCrmLeadProfile(lead.id, {
                  title: title.trim(), contactName, mobileNumber: mobile, email,
                  productInterest: product, notes,
                }),
                'Lead profile saved',
              )}>{busy ? 'Saving…' : 'Save profile'}</button>
            </div>
          </div>
        ) : null}

        {tab === 'activity' ? (
          <div className="crm2-form-section">
            <div className="crm2-form-grid">
              <label>Activity type<select value={activityType} onChange={(e) => setActivityType(e.target.value)}><option>Note</option><option>Call</option><option>WhatsApp</option><option>Email</option><option>Meeting</option></select></label>
              <label>Summary<input value={activitySummary} onChange={(e) => setActivitySummary(e.target.value)} placeholder="What happened?" /></label>
            </div>
            <label>Details<textarea value={activityDetails} onChange={(e) => setActivityDetails(e.target.value)} rows={3} /></label>
            <div className="crm2-drawer-actions"><button className="crm2-primary" disabled={busy || !activitySummary.trim()} onClick={() => void run(async () => {
              await addCrmActivity(lead.id, { type: activityType, summary: activitySummary.trim(), details: activityDetails })
              setActivitySummary(''); setActivityDetails('')
            }, 'Activity added')}>Add activity</button></div>
            <div className="crm2-timeline">
              {workspace.activities.length === 0 ? <p className="crm2-muted">No activity yet.</p> : workspace.activities.map((item) => (
                <article key={item.id}><i>{item.type.slice(0, 1)}</i><div><strong>{item.summary}</strong><span>{item.type} · {formatDate(item.occurredAtUtc)}</span>{item.details ? <p>{item.details}</p> : null}</div></article>
              ))}
            </div>
          </div>
        ) : null}
        {tab === 'followups' ? (
          <div className="crm2-form-section">
            <div className="crm2-form-grid">
              <label>Channel<select value={followUpChannel} onChange={(e) => setFollowUpChannel(e.target.value)}><option>Call</option><option>WhatsApp</option><option>Email</option><option>Meeting</option><option>Other</option></select></label>
              <label>Due date & time<input type="datetime-local" value={followUpAt} onChange={(e) => setFollowUpAt(e.target.value)} /></label>
            </div>
            <label>Purpose<input value={followUpPurpose} onChange={(e) => setFollowUpPurpose(e.target.value)} /></label>
            <div className="crm2-drawer-actions"><button className="crm2-primary" disabled={busy || !followUpPurpose.trim() || !followUpAt} onClick={() => void run(
              () => createCrmFollowUp(lead.id, { dueAtUtc: new Date(followUpAt).toISOString(), channel: followUpChannel, purpose: followUpPurpose.trim() }),
              'Follow-up scheduled',
            )}>Schedule follow-up</button></div>
            <div className="crm2-work-list">
              {workspace.followUps.length === 0 ? <p className="crm2-muted">No follow-ups scheduled.</p> : workspace.followUps.map((item) => (
                <article key={item.id} className={item.status === 'Open' && new Date(item.dueAtUtc) < new Date() ? 'overdue' : ''}>
                  <div><strong>{item.purpose}</strong><span>{item.channel} · {formatDate(item.dueAtUtc)}</span>{item.outcome ? <small>{item.outcome}</small> : null}</div>
                  <em>{item.status}</em>
                  {item.status === 'Open' ? <button disabled={busy} onClick={() => void run(() => completeCrmFollowUp(item.id, 'Completed from CRM'), 'Follow-up completed')}>Complete</button> : null}
                </article>
              ))}
            </div>
          </div>
        ) : null}

        {tab === 'tasks' ? (
          <div className="crm2-form-section">
            <div className="crm2-form-grid">
              <label>Task title<input value={taskTitle} onChange={(e) => setTaskTitle(e.target.value)} placeholder="Prepare demo / send quotation" /></label>
              <label>Priority<select value={taskPriority} onChange={(e) => setTaskPriority(e.target.value)}><option>Low</option><option>Normal</option><option>High</option><option>Urgent</option></select></label>
            </div>
            <label>Task details<textarea value={taskDetails} onChange={(e) => setTaskDetails(e.target.value)} rows={3} /></label>
            <div className="crm2-drawer-actions"><button className="crm2-primary" disabled={busy || !taskTitle.trim()} onClick={() => void run(async () => {
              await createCrmTask({ title: taskTitle.trim(), leadId: lead.id, details: taskDetails, priority: taskPriority })
              setTaskTitle(''); setTaskDetails('')
            }, 'Task created')}>Create task</button></div>
            <div className="crm2-work-list">
              {workspace.tasks.length === 0 ? <p className="crm2-muted">No tasks for this lead.</p> : workspace.tasks.map((item) => (
                <article key={item.id}>
                  <div><strong>{item.title}</strong><span>{item.priority}{item.dueAtUtc ? ` · ${formatDate(item.dueAtUtc)}` : ''}</span>{item.details ? <small>{item.details}</small> : null}</div>
                  <em>{item.status}</em>
                  {item.status === 'Open' ? <button disabled={busy} onClick={() => void run(() => completeCrmTask(item.id), 'Task completed')}>Complete</button> : null}
                </article>
              ))}
            </div>
          </div>
        ) : null}

        {tab === 'convert' ? (
          <div className="crm2-form-section">
            {lead.status === 'Converted' ? (
              <div className="crm2-converted-card"><strong>Converted successfully</strong><span>This lead is linked to a customer account and its sales opportunity.</span></div>
            ) : (
              <>
                <div className="crm2-conversion-note"><strong>Lead → Customer + Opportunity</strong><span>This action creates the customer account, copies the lead contact, creates a deal and then marks the lead Converted.</span></div>
                <div className="crm2-form-grid">
                  <label>Customer account name<input value={accountName} onChange={(e) => setAccountName(e.target.value)} /></label>
                  <label>Opportunity title<input value={opportunityTitle} onChange={(e) => setOpportunityTitle(e.target.value)} /></label>
                  <label>Estimated value<input type="number" min="0" value={estimatedValue} onChange={(e) => setEstimatedValue(e.target.value)} /></label>
                  <label>Probability %<input type="number" min="0" max="100" value={probability} onChange={(e) => setProbability(e.target.value)} /></label>
                  <label>Expected close date<input type="date" value={expectedCloseDate} onChange={(e) => setExpectedCloseDate(e.target.value)} /></label>
                </div>
                <div className="crm2-drawer-actions"><button className="crm2-primary" disabled={busy || !accountName.trim() || !opportunityTitle.trim() || Number(estimatedValue) < 0} onClick={() => void run(
                  () => convertCrmLead(lead.id, { accountName: accountName.trim(), opportunityTitle: opportunityTitle.trim(), estimatedValue: Number(estimatedValue || 0), probabilityPercent: Number(probability || 0), expectedCloseDate: expectedCloseDate || undefined }),
                  'Lead converted to customer and opportunity',
                )}>{busy ? 'Converting…' : 'Convert lead'}</button></div>
              </>
            )}
          </div>
        ) : null}
      </section>
    </div>
  )
}
