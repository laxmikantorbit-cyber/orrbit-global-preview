import { useEffect, useMemo, useState } from 'react'
import './CrmDemo.css'
import './CrmAdvancedHub.css'
import {
  addCrmContact,
  listCrmAccounts,
  listCrmLeads,
  listCrmOpportunities,
  listCrmTeam,
  type CrmAccount,
  type CrmLead,
  type CrmOpportunity,
  type CrmTeamMember,
} from './crmApi'
import {
  deactivateCrmContact,
  mergeCrmLead,
  updateCrmAccount,
  updateCrmContact,
  updateCrmOpportunity,
} from './crmMaintenanceApi'

type View = 'merge' | 'account' | 'opportunity'

export function CrmDataMaintenanceHub() {
  const [view, setView] = useState<View>('merge')
  const [leads, setLeads] = useState<CrmLead[]>([])
  const [accounts, setAccounts] = useState<CrmAccount[]>([])
  const [opportunities, setOpportunities] = useState<CrmOpportunity[]>([])
  const [team, setTeam] = useState<CrmTeamMember[]>([])
  const [message, setMessage] = useState('CRM data maintenance ready')
  const [busy, setBusy] = useState(false)

  const [sourceLeadId, setSourceLeadId] = useState('')
  const [targetLeadId, setTargetLeadId] = useState('')

  const [accountId, setAccountId] = useState('')
  const account = useMemo(() => accounts.find(x => x.id === accountId) || null, [accounts, accountId])
  const [accountName, setAccountName] = useState('')
  const [accountLegal, setAccountLegal] = useState('')
  const [accountGstin, setAccountGstin] = useState('')
  const [accountCode, setAccountCode] = useState('')
  const [accountStatus, setAccountStatus] = useState('Active')
  const [contactId, setContactId] = useState('')
  const [contactName, setContactName] = useState('')
  const [contactEmail, setContactEmail] = useState('')
  const [contactPhone, setContactPhone] = useState('')
  const [contactPrimary, setContactPrimary] = useState(false)

  const [opportunityId, setOpportunityId] = useState('')
  const opportunity = useMemo(() => opportunities.find(x => x.id === opportunityId) || null, [opportunities, opportunityId])
  const [oppTitle, setOppTitle] = useState('')
  const [oppValue, setOppValue] = useState(0)
  const [oppProbability, setOppProbability] = useState(50)
  const [oppCloseDate, setOppCloseDate] = useState('')
  const [oppOwner, setOppOwner] = useState('')

  async function refresh() {
    setBusy(true)
    try {
      const [leadResult, accountResult, oppResult, teamResult] = await Promise.all([
        listCrmLeads(), listCrmAccounts(), listCrmOpportunities(), listCrmTeam(),
      ])
      setLeads(leadResult.leads)
      setAccounts(accountResult.accounts)
      setOpportunities(oppResult.opportunities)
      setTeam(teamResult.members)
      setMessage('CRM maintenance data refreshed')
    } catch (error) { setMessage(error instanceof Error ? error.message : String(error)) }
    finally { setBusy(false) }
  }

  useEffect(() => { void refresh() }, [])

  useEffect(() => {
    if (!account) return
    setAccountName(account.name)
    setAccountLegal(account.legalName || '')
    setAccountGstin(account.gstin || '')
    setAccountCode(account.displayCode || '')
    setAccountStatus(account.status)
    setContactId('')
  }, [account])

  useEffect(() => {
    const contact = account?.contacts.find(x => x.id === contactId)
    if (!contact) { setContactName(''); setContactEmail(''); setContactPhone(''); setContactPrimary(false); return }
    setContactName(contact.name); setContactEmail(contact.email || ''); setContactPhone(contact.phone || ''); setContactPrimary(contact.isPrimary)
  }, [account, contactId])

  useEffect(() => {
    if (!opportunity) return
    setOppTitle(opportunity.title)
    setOppValue(opportunity.estimatedValue)
    setOppProbability(opportunity.probabilityPercent)
    setOppCloseDate(opportunity.expectedCloseDate || '')
    setOppOwner(opportunity.ownerUserId || '')
  }, [opportunity])

  async function runMerge() {
    if (!sourceLeadId || !targetLeadId || sourceLeadId === targetLeadId) { setMessage('Select two different leads'); return }
    setBusy(true)
    try {
      const result = await mergeCrmLead(sourceLeadId, targetLeadId)
      setMessage(`Merge completed · ${result.followUpsMoved} follow-ups and ${result.tasksMoved} tasks moved`)
      setSourceLeadId(''); setTargetLeadId('')
      await refresh()
    } catch (error) { setMessage(error instanceof Error ? error.message : String(error)); setBusy(false) }
  }

  async function saveAccount() {
    if (!account) return
    setBusy(true)
    try {
      await updateCrmAccount(account.id, { name: accountName, legalName: accountLegal, gstin: accountGstin, displayCode: accountCode, status: accountStatus })
      setMessage('Customer account updated with duplicate validation')
      await refresh()
    } catch (error) { setMessage(error instanceof Error ? error.message : String(error)); setBusy(false) }
  }

  async function saveContact() {
    if (!account || !contactId) return
    setBusy(true)
    try {
      await updateCrmContact(account.id, contactId, { name: contactName, email: contactEmail, phone: contactPhone, isPrimary: contactPrimary })
      setMessage('Contact updated with duplicate validation')
      await refresh()
    } catch (error) { setMessage(error instanceof Error ? error.message : String(error)); setBusy(false) }
  }

  async function removeContact() {
    if (!account || !contactId) return
    setBusy(true)
    try {
      await deactivateCrmContact(account.id, contactId)
      setMessage('Contact deactivated and audit event recorded')
      setContactId('')
      await refresh()
    } catch (error) { setMessage(error instanceof Error ? error.message : String(error)); setBusy(false) }
  }

  async function addContact() {
    if (!account || !contactName.trim()) return
    setBusy(true)
    try {
      await addCrmContact(account.id, { name: contactName, email: contactEmail, phone: contactPhone, isPrimary: contactPrimary })
      setMessage('New contact added')
      setContactId(''); setContactName(''); setContactEmail(''); setContactPhone(''); setContactPrimary(false)
      await refresh()
    } catch (error) { setMessage(error instanceof Error ? error.message : String(error)); setBusy(false) }
  }

  async function saveOpportunity() {
    if (!opportunity) return
    setBusy(true)
    try {
      await updateCrmOpportunity(opportunity.id, {
        title: oppTitle,
        estimatedValue: oppValue,
        probabilityPercent: oppProbability,
        expectedCloseDate: oppCloseDate || undefined,
        ownerUserId: oppOwner || null,
      })
      setMessage('Opportunity forecast/owner updated and audited')
      await refresh()
    } catch (error) { setMessage(error instanceof Error ? error.message : String(error)); setBusy(false) }
  }

  const openLeads = leads.filter(x => x.status !== 'Converted' && x.status !== 'Unqualified')
  const activeTeam = team.filter(x => x.active)

  return <div className="crm2-app crm-advanced-app">
    <aside className="crm2-sidebar">
      <div className="crm2-brand"><div className="crm2-brand-mark">o</div><div><strong>oRRbit</strong><span>BusinessOS</span></div></div>
      <nav className="crm2-nav">
        <a className="crm-advanced-back" href="/crm"><span>←</span>CRM Home</a>
        <button className={view === 'merge' ? 'active' : ''} onClick={() => setView('merge')}><span>⇉</span>Lead Merge</button>
        <button className={view === 'account' ? 'active' : ''} onClick={() => setView('account')}><span>A</span>Accounts & Contacts</button>
        <button className={view === 'opportunity' ? 'active' : ''} onClick={() => setView('opportunity')}><span>O</span>Opportunity Edit</button>
      </nav>
      <div className="crm2-sidebar-foot"><strong>DATA QUALITY WORKSPACE</strong><small>Merge · Edit · Deactivate · Forecast</small></div>
    </aside>
    <main className="crm2-main">
      <header className="crm2-topbar"><div><span className="crm2-kicker">BUSINESSOS DATA MAINTENANCE</span><h1>{view === 'merge' ? 'Merge duplicate leads' : view === 'account' ? 'Customer account & contacts' : 'Opportunity maintenance'}</h1></div><div className="crm2-top-actions"><button className="crm2-refresh" disabled={busy} onClick={() => void refresh()}>↻ Refresh</button></div></header>
      <section className="crm2-statusbar"><div><span className={busy ? 'pulse busy' : 'pulse'} />{message}</div><span>Server-side validation · Audited changes</span></section>

      {view === 'merge' ? <section className="crm2-table-card"><div className="crm2-section-head"><div><span>DUPLICATE CONTROL</span><h2>Consolidate lead records</h2></div></div><div className="crm-advanced-form stacked"><select value={sourceLeadId} onChange={e => setSourceLeadId(e.target.value)}><option value="">Source duplicate lead</option>{openLeads.map(x => <option key={x.id} value={x.id}>{x.title} · {x.mobileNumber || x.email || x.status}</option>)}</select><select value={targetLeadId} onChange={e => setTargetLeadId(e.target.value)}><option value="">Target lead to keep</option>{openLeads.filter(x => x.id !== sourceLeadId).map(x => <option key={x.id} value={x.id}>{x.title} · {x.mobileNumber || x.email || x.status}</option>)}</select><button className="crm2-primary" onClick={() => void runMerge()}>Merge & Reassign Open Work</button><small>Source lead history remains traceable. Open follow-ups/tasks are recreated on target and source is closed as merged.</small></div></section> : null}

      {view === 'account' ? <section className="crm-advanced-grid"><article className="crm2-table-card"><div className="crm2-section-head"><div><span>CUSTOMER</span><h2>Account profile</h2></div></div><div className="crm-advanced-form stacked"><select value={accountId} onChange={e => setAccountId(e.target.value)}><option value="">Select account</option>{accounts.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</select>{account ? <><input value={accountName} onChange={e => setAccountName(e.target.value)} placeholder="Business name"/><input value={accountLegal} onChange={e => setAccountLegal(e.target.value)} placeholder="Legal name"/><input value={accountGstin} onChange={e => setAccountGstin(e.target.value)} placeholder="GSTIN"/><input value={accountCode} onChange={e => setAccountCode(e.target.value)} placeholder="Display code"/><select value={accountStatus} onChange={e => setAccountStatus(e.target.value)}><option>Active</option><option>Inactive</option><option>Archived</option></select><button className="crm2-primary" onClick={() => void saveAccount()}>Save Account</button></> : null}</div></article><article className="crm2-table-card"><div className="crm2-section-head"><div><span>CONTACTS</span><h2>{account?.contacts.length || 0} contact(s)</h2></div></div>{account ? <div className="crm-advanced-form stacked"><select value={contactId} onChange={e => setContactId(e.target.value)}><option value="">New contact / select existing</option>{account.contacts.map(x => <option key={x.id} value={x.id}>{x.name}{x.isPrimary ? ' · Primary' : ''}</option>)}</select><input value={contactName} onChange={e => setContactName(e.target.value)} placeholder="Contact name"/><input value={contactEmail} onChange={e => setContactEmail(e.target.value)} placeholder="Email"/><input value={contactPhone} onChange={e => setContactPhone(e.target.value)} placeholder="Mobile"/><label><input type="checkbox" checked={contactPrimary} onChange={e => setContactPrimary(e.target.checked)}/> Primary contact</label><div className="crm2-top-actions">{contactId ? <><button className="crm2-primary" onClick={() => void saveContact()}>Update</button><button className="crm2-refresh" onClick={() => void removeContact()}>Deactivate</button></> : <button className="crm2-primary" onClick={() => void addContact()}>Add Contact</button>}</div></div> : <p>Select an account.</p>}</article></section> : null}

      {view === 'opportunity' ? <section className="crm2-table-card"><div className="crm2-section-head"><div><span>DEAL FORECAST</span><h2>Edit open opportunity</h2></div></div><div className="crm-advanced-form stacked"><select value={opportunityId} onChange={e => setOpportunityId(e.target.value)}><option value="">Select opportunity</option>{opportunities.filter(x => x.stage !== 'Won' && x.stage !== 'Lost').map(x => <option key={x.id} value={x.id}>{x.title} · {x.stage}</option>)}</select>{opportunity ? <><input value={oppTitle} onChange={e => setOppTitle(e.target.value)} placeholder="Deal title"/><input type="number" value={oppValue} onChange={e => setOppValue(Number(e.target.value))} placeholder="Estimated value"/><input type="number" min={0} max={100} value={oppProbability} onChange={e => setOppProbability(Number(e.target.value))} placeholder="Probability %"/><input type="date" value={oppCloseDate} onChange={e => setOppCloseDate(e.target.value)}/><select value={oppOwner} onChange={e => setOppOwner(e.target.value)}><option value="">Unassigned</option>{activeTeam.map(x => <option key={x.id} value={x.id}>{x.displayName} · {x.role}</option>)}</select><button className="crm2-primary" onClick={() => void saveOpportunity()}>Save Opportunity</button></> : null}</div></section> : null}
    </main>
  </div>
}
