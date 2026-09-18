import { useMemo, useState } from 'react'
import {
  addCrmContact,
  changeCrmOpportunityStage,
  createCrmAccount,
  createCrmOpportunity,
  type CrmAccount,
  type CrmOpportunity,
} from './crmApi'

type Props = {
  view: 'accounts' | 'opportunities'
  accounts: CrmAccount[]
  opportunities: CrmOpportunity[]
  busy: boolean
  refresh: () => Promise<void>
  notify: (message: string) => void
  canManageAccounts: boolean
  canManageOpportunities: boolean
}

const stages = ['Discovery', 'SolutionFit', 'Proposal', 'Negotiation', 'Won', 'Lost']

function money(value: number, currency: string) {
  try { return new Intl.NumberFormat('en-IN', { style: 'currency', currency, maximumFractionDigits: 0 }).format(value) }
  catch { return `${currency} ${value.toLocaleString('en-IN')}` }
}

export function CrmSalesView({ view, accounts, opportunities, busy, refresh, notify, canManageAccounts, canManageOpportunities }: Props) {
  const [showAccount, setShowAccount] = useState(false)
  const [showOpportunity, setShowOpportunity] = useState(false)
  const [selectedAccountId, setSelectedAccountId] = useState<string | null>(null)
  const [accountName, setAccountName] = useState('')
  const [contactName, setContactName] = useState('')
  const [phone, setPhone] = useState('')
  const [email, setEmail] = useState('')
  const [contactDraft, setContactDraft] = useState('')
  const [dealAccountId, setDealAccountId] = useState('')
  const [dealTitle, setDealTitle] = useState('')
  const [dealValue, setDealValue] = useState('29999')
  const [dealProbability, setDealProbability] = useState('50')
  const [closeDate, setCloseDate] = useState('')

  const selectedAccount = useMemo(
    () => accounts.find((item) => item.id === selectedAccountId) ?? null,
    [accounts, selectedAccountId],
  )
  const totalPipeline = opportunities
    .filter((item) => !['Won', 'Lost'].includes(item.stage))
    .reduce((sum, item) => sum + item.estimatedValue, 0)

  async function perform(action: () => Promise<unknown>, success: string) {
    try {
      await action()
      notify(success)
      await refresh()
    } catch (error) {
      notify(error instanceof Error ? error.message : String(error))
    }
  }

  async function saveAccount() {
    if (!accountName.trim()) return
    await perform(async () => {
      await createCrmAccount({
        name: accountName.trim(), contactName: contactName.trim() || undefined,
        phone: phone.trim() || undefined, email: email.trim() || undefined,
      })
      setAccountName(''); setContactName(''); setPhone(''); setEmail(''); setShowAccount(false)
    }, 'Customer account created')
  }

  async function saveOpportunity() {
    if (!dealAccountId || !dealTitle.trim()) return
    await perform(async () => {
      await createCrmOpportunity({
        accountId: dealAccountId,
        title: dealTitle.trim(),
        estimatedValue: Number(dealValue || 0),
        probabilityPercent: Number(dealProbability || 0),
        expectedCloseDate: closeDate || undefined,
      })
      setDealTitle(''); setDealValue('29999'); setDealProbability('50'); setCloseDate(''); setShowOpportunity(false)
    }, 'Opportunity created')
  }

  async function addContact() {
    if (!selectedAccount || !contactDraft.trim()) return
    await perform(async () => {
      await addCrmContact(selectedAccount.id, { name: contactDraft.trim(), isPrimary: selectedAccount.contacts.length === 0 })
      setContactDraft('')
    }, 'Contact added')
  }

  if (view === 'accounts') {
    const activeCustomers = accounts.filter((account) => account.status !== 'Inactive').length
    const activeContacts = accounts.reduce((sum, account) => sum + account.contacts.length, 0)
    return (
      <section className="crm2-ref-list-page">
        <div className="crm2-ref-action-row">
          {canManageAccounts ? <button className="crm2-ref-primary" onClick={() => setShowAccount(true)}>+ New Customer</button> : null}
          <button className="crm2-ref-primary" onClick={() => notify('Import customers is scheduled for the next backend block')}>Import Customers</button>
          <button className="crm2-ref-outline" onClick={() => accounts[0] && setSelectedAccountId(accounts[0].id)}>Contacts</button>
          <button className="crm2-filter-button">Filter</button>
        </div>
        <section className="crm2-ref-summary-card">
          <h2>Customers Summary</h2>
          <div className="crm2-ref-summary-line"><strong>{accounts.length}</strong><span>Total Customers</span><strong>{activeCustomers}</strong><span className="good">Active Customers</span><strong>{accounts.length - activeCustomers}</strong><span className="bad">Inactive Customers</span><strong>{activeContacts}</strong><span>Active Contacts</span><strong>0</strong><span>Contacts Logged In...</span></div>
        </section>
        <section className="crm2-ref-table-card">
          <label className="crm2-ref-check"><input type="checkbox" defaultChecked /> Exclude Inactive Customers</label>
          <div className="crm2-ref-table-tools"><select><option>25</option><option>50</option></select><button>Export</button><button>Bulk Actions</button><button onClick={refresh}>Refresh</button><span /><label><b>⌕</b><input placeholder="Search..." /></label></div>
          <div className="crm2-ref-customers-head"><span><input type="checkbox" /></span><span>#</span><span>Company</span><span>Primary Contact</span><span>Primary Email</span><span>Phone</span><span>Active</span><span>Groups</span></div>
          {accounts.length === 0 ? <p className="crm2-reference-empty">No entries found</p> : accounts.map((account, index) => (
            <article className="crm2-ref-customers-row" key={account.id} onClick={() => setSelectedAccountId(account.id)}>
              <span><input type="checkbox" onClick={(e) => e.stopPropagation()} /></span><span>{176 - index}</span><span><a>{account.name}</a></span><span>{account.primaryContact?.name || '-'}</span><span><a>{account.primaryContact?.email || '-'}</a></span><span>{account.primaryContact?.phone || '-'}</span><span><label className="crm2-ref-switch"><input type="checkbox" checked={account.status !== 'Inactive'} readOnly /><i /></label></span><span><em>{account.status === 'Active' ? 'Customer' : account.status}</em></span>
            </article>
          ))}
        </section>

        {showAccount && canManageAccounts ? (
          <div className="crm2-overlay" onMouseDown={() => setShowAccount(false)}>
            <section className="crm2-drawer" onMouseDown={(e) => e.stopPropagation()}>
              <div className="crm2-drawer-head"><div><span className="crm2-kicker">NEW CUSTOMER</span><h2>Create account</h2></div><button onClick={() => setShowAccount(false)}>×</button></div>
              <label>Business / account name<input autoFocus value={accountName} onChange={(e) => setAccountName(e.target.value)} /></label>
              <label>Primary contact<input value={contactName} onChange={(e) => setContactName(e.target.value)} /></label>
              <label>Phone<input value={phone} onChange={(e) => setPhone(e.target.value)} /></label>
              <label>Email<input type="email" value={email} onChange={(e) => setEmail(e.target.value)} /></label>
              <div className="crm2-drawer-actions"><button className="crm2-cancel" onClick={() => setShowAccount(false)}>Cancel</button><button className="crm2-primary" disabled={busy || !accountName.trim()} onClick={() => void saveAccount()}>Create account</button></div>
            </section>
          </div>
        ) : null}
        {selectedAccount ? (
          <div className="crm2-overlay" onMouseDown={() => setSelectedAccountId(null)}>
            <section className="crm2-drawer crm2-wide-drawer" onMouseDown={(e) => e.stopPropagation()}>
              <div className="crm2-drawer-head"><div><span className="crm2-kicker">ACCOUNT</span><h2>{selectedAccount.name}</h2></div><button onClick={() => setSelectedAccountId(null)}>×</button></div>
              <div className="crm2-detail-summary">
                <div><span>Status</span><strong>{selectedAccount.status}</strong></div>
                <div><span>Contacts</span><strong>{selectedAccount.contacts.length}</strong></div>
                <div><span>GSTIN</span><strong>{selectedAccount.gstin || '—'}</strong></div>
                <div><span>Code</span><strong>{selectedAccount.displayCode || '—'}</strong></div>
              </div>
              <div className="crm2-contact-list">
                {selectedAccount.contacts.length === 0 ? <p className="crm2-muted">No contacts yet.</p> : selectedAccount.contacts.map((contact) => (
                  <article key={contact.id}><div><strong>{contact.name}</strong><span>{contact.phone || contact.email || 'No contact details'}</span></div>{contact.isPrimary ? <em>Primary</em> : null}</article>
                ))}
              </div>
              {canManageAccounts ? <div className="crm2-inline-create"><input value={contactDraft} onChange={(e) => setContactDraft(e.target.value)} placeholder="Add contact name" /><button className="crm2-primary" disabled={busy || !contactDraft.trim()} onClick={() => void addContact()}>Add contact</button></div> : null}
            </section>
          </div>
        ) : null}
      </section>
    )
  }

  return (
    <section className="crm2-sales-module">
      <div className="crm2-module-head">
        <div><span className="crm2-kicker">DEAL MANAGEMENT</span><h2>Opportunities</h2><p>Track value, probability, expected close and deal stage.</p></div>
        {canManageOpportunities ? <button className="crm2-primary" disabled={accounts.length === 0} onClick={() => { setDealAccountId(accounts[0]?.id || ''); setShowOpportunity(true) }}>＋ New opportunity</button> : <span className="crm2-readonly-badge">Read only</span>}
      </div>
      <div className="crm2-deal-summary">
        <article><span>Open pipeline</span><strong>{money(totalPipeline, 'INR')}</strong></article>
        <article><span>Open deals</span><strong>{opportunities.filter((x) => !['Won', 'Lost'].includes(x.stage)).length}</strong></article>
        <article><span>Won</span><strong>{opportunities.filter((x) => x.stage === 'Won').length}</strong></article>
        <article><span>Lost</span><strong>{opportunities.filter((x) => x.stage === 'Lost').length}</strong></article>
      </div>
      <div className="crm2-opportunity-table">
        <div className="crm2-opportunity-head"><span>Opportunity</span><span>Account</span><span>Value</span><span>Probability</span><span>Stage</span></div>
        {opportunities.length === 0 ? <div className="crm2-empty-card"><strong>No opportunities yet</strong><span>Convert a lead to automatically create its first deal.</span></div> : opportunities.map((deal) => {
          const account = accounts.find((item) => item.id === deal.accountId)
          return <article key={deal.id}>
            <div><strong>{deal.title}</strong><small>{deal.expectedCloseDate || 'No close date'}</small></div>
            <span>{account?.name || 'Unknown account'}</span>
            <span>{money(deal.estimatedValue, deal.currencyCode)}</span>
            <span>{deal.probabilityPercent}%</span>
            <select value={deal.stage} disabled={busy || !canManageOpportunities || ['Won', 'Lost'].includes(deal.stage)} onChange={(e) => void perform(
              () => changeCrmOpportunityStage(deal.id, e.target.value, e.target.value === 'Lost' ? 'Closed as lost' : undefined),
              `Opportunity moved to ${e.target.value}`,
            )}>{stages.map((stage) => <option key={stage}>{stage}</option>)}</select>
          </article>
        })}
      </div>

      {showOpportunity && canManageOpportunities ? (
        <div className="crm2-overlay" onMouseDown={() => setShowOpportunity(false)}>
          <section className="crm2-drawer" onMouseDown={(e) => e.stopPropagation()}>
            <div className="crm2-drawer-head"><div><span className="crm2-kicker">NEW DEAL</span><h2>Create opportunity</h2></div><button onClick={() => setShowOpportunity(false)}>×</button></div>
            <label>Customer account<select value={dealAccountId} onChange={(e) => setDealAccountId(e.target.value)}>{accounts.map((account) => <option key={account.id} value={account.id}>{account.name}</option>)}</select></label>
            <label>Opportunity title<input value={dealTitle} onChange={(e) => setDealTitle(e.target.value)} placeholder="AI Repair software sale" /></label>
            <label>Estimated value<input type="number" min="0" value={dealValue} onChange={(e) => setDealValue(e.target.value)} /></label>
            <label>Probability %<input type="number" min="0" max="100" value={dealProbability} onChange={(e) => setDealProbability(e.target.value)} /></label>
            <label>Expected close date<input type="date" value={closeDate} onChange={(e) => setCloseDate(e.target.value)} /></label>
            <div className="crm2-drawer-actions"><button className="crm2-cancel" onClick={() => setShowOpportunity(false)}>Cancel</button><button className="crm2-primary" disabled={busy || !dealAccountId || !dealTitle.trim()} onClick={() => void saveOpportunity()}>Create opportunity</button></div>
          </section>
        </div>
      ) : null}
    </section>
  )
}
