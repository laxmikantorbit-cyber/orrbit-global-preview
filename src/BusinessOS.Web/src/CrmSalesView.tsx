import { useMemo, useState } from 'react'
import {
  addCrmContact,
  bulkUpdateCrmAccounts,
  changeCrmOpportunityStage,
  createCrmAccount,
  createCrmOpportunity,
  updateCrmAccountProfile,
  type CrmAccount,
  type CrmOpportunity,
} from './crmApi'
import { exportCrmSpreadsheet, pickCrmSpreadsheet, type CrmSpreadsheetFormat } from './crmSpreadsheet'

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

function parseGroups(value: string) {
  return Array.from(new Set(value.split(/[;,|]/).map((group) => group.trim()).filter(Boolean)))
}

export function CrmSalesView({ view, accounts, opportunities, busy, refresh, notify, canManageAccounts, canManageOpportunities }: Props) {
  const [showAccount, setShowAccount] = useState(false)
  const [showOpportunity, setShowOpportunity] = useState(false)
  const [selectedAccountId, setSelectedAccountId] = useState<string | null>(null)
  const [accountName, setAccountName] = useState('')
  const [contactName, setContactName] = useState('')
  const [phone, setPhone] = useState('')
  const [email, setEmail] = useState('')
  const [accountGroups, setAccountGroups] = useState('')
  const [contactDraft, setContactDraft] = useState('')
  const [selectedAccountIds, setSelectedAccountIds] = useState<string[]>([])
  const [customerQuery, setCustomerQuery] = useState('')
  const [customerStatusFilter, setCustomerStatusFilter] = useState('All')
  const [excludeInactive, setExcludeInactive] = useState(true)
  const [customerGroupFilter, setCustomerGroupFilter] = useState('All')
  const [bulkCustomerStatus, setBulkCustomerStatus] = useState('')
  const [bulkCustomerGroup, setBulkCustomerGroup] = useState('')
  const [bulkCustomerGroupMode, setBulkCustomerGroupMode] = useState<'add' | 'remove'>('add')
  const [showBulkCustomerActions, setShowBulkCustomerActions] = useState(false)
  const [detailName, setDetailName] = useState('')
  const [detailLegalName, setDetailLegalName] = useState('')
  const [detailGstin, setDetailGstin] = useState('')
  const [detailCode, setDetailCode] = useState('')
  const [detailStatus, setDetailStatus] = useState('Active')
  const [detailGroups, setDetailGroups] = useState('')
  const [dealAccountId, setDealAccountId] = useState('')
  const [dealTitle, setDealTitle] = useState('')
  const [dealValue, setDealValue] = useState('29999')
  const [dealProbability, setDealProbability] = useState('50')
  const [closeDate, setCloseDate] = useState('')

  const selectedAccount = useMemo(
    () => accounts.find((item) => item.id === selectedAccountId) ?? null,
    [accounts, selectedAccountId],
  )
  const customerGroups = useMemo(
    () => Array.from(new Set(accounts.flatMap((account) => account.groups || []))).sort((a, b) => a.localeCompare(b)),
    [accounts],
  )
  const filteredAccounts = useMemo(() => {
    const search = customerQuery.trim().toLowerCase()
    return accounts.filter((account) => {
      const groups = account.groups || []
      const matchesStatus = (customerStatusFilter === 'All' || account.status === customerStatusFilter) && (!excludeInactive || account.status !== 'Inactive')
      const matchesGroup = customerGroupFilter === 'All' || groups.some((group) => group.toLowerCase() === customerGroupFilter.toLowerCase())
      const haystack = [
        account.name, account.legalName, account.gstin, account.displayCode,
        account.primaryContact?.name, account.primaryContact?.email, account.primaryContact?.phone,
        ...groups,
      ].filter(Boolean).join(' ').toLowerCase()
      return matchesStatus && matchesGroup && (!search || haystack.includes(search))
    })
  }, [accounts, customerGroupFilter, customerQuery, customerStatusFilter, excludeInactive])
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
        groups: parseGroups(accountGroups),
      })
      setAccountName(''); setContactName(''); setPhone(''); setEmail(''); setAccountGroups(''); setShowAccount(false)
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


  function openAccount(account: CrmAccount) {
    setSelectedAccountId(account.id)
    setDetailName(account.name)
    setDetailLegalName(account.legalName || '')
    setDetailGstin(account.gstin || '')
    setDetailCode(account.displayCode || '')
    setDetailStatus(account.status)
    setDetailGroups((account.groups || []).join(', '))
  }

  function toggleSelectedAccount(id: string, checked: boolean) {
    setSelectedAccountIds((ids) => checked ? Array.from(new Set([...ids, id])) : ids.filter((item) => item !== id))
  }

  async function saveSelectedAccount() {
    if (!selectedAccount || !detailName.trim()) return
    await perform(
      () => updateCrmAccountProfile(selectedAccount.id, {
        name: detailName.trim(),
        legalName: detailLegalName.trim() || undefined,
        gstin: detailGstin.trim() || undefined,
        displayCode: detailCode.trim() || undefined,
        status: detailStatus,
        groups: parseGroups(detailGroups),
      }),
      'Customer profile updated',
    )
  }

  async function toggleAccountStatus(account: CrmAccount) {
    await perform(
      () => updateCrmAccountProfile(account.id, {
        name: account.name,
        legalName: account.legalName || undefined,
        gstin: account.gstin || undefined,
        displayCode: account.displayCode || undefined,
        status: account.status === 'Active' ? 'Inactive' : 'Active',
        groups: account.groups || [],
      }),
      `Customer ${account.status === 'Active' ? 'deactivated' : 'activated'}`,
    )
  }

  async function applyCustomerBulkAction() {
    if (selectedAccountIds.length === 0) { notify('Select at least one customer first'); return }
    if (!bulkCustomerStatus && !bulkCustomerGroup.trim()) { notify('Choose a bulk status or group action'); return }
    let updated = 0
    let failed = 0
    await perform(async () => {
      const result = await bulkUpdateCrmAccounts({
        accountIds: selectedAccountIds,
        status: bulkCustomerStatus || undefined,
        addGroup: bulkCustomerGroupMode === 'add' ? bulkCustomerGroup.trim() || undefined : undefined,
        removeGroup: bulkCustomerGroupMode === 'remove' ? bulkCustomerGroup.trim() || undefined : undefined,
      })
      updated = result.updated.length
      failed = result.failed.length
      setSelectedAccountIds([])
      setBulkCustomerStatus('')
      setBulkCustomerGroup('')
    }, 'Customer bulk action completed')
    if (failed) notify(`Bulk updated ${updated} customer(s); ${failed} failed`)
  }

  async function exportCustomers(format: CrmSpreadsheetFormat) {
    await exportCrmSpreadsheet('crm-customers', {
      headers: ['Company', 'Legal Name', 'Primary Contact', 'Email', 'Phone', 'GSTIN', 'Code', 'Status', 'Groups', 'Contacts'],
      rows: filteredAccounts.map((account) => [
        account.name, account.legalName || '', account.primaryContact?.name || '',
        account.primaryContact?.email || '', account.primaryContact?.phone || '',
        account.gstin || '', account.displayCode || '', account.status,
        (account.groups || []).join('; '), account.contacts.length,
      ]),
    }, format)
    notify(`Exported ${filteredAccounts.length} customer(s) to ${format === 'xlsx' ? 'Excel' : 'CSV'}`)
  }

  function importCustomers() {
    pickCrmSpreadsheet((sheetRows, fileName) => {
      const [header, ...rows] = sheetRows
      if (!header || rows.length === 0) { notify('Spreadsheet has no customer rows'); return }
      const keys = header.map((cell) => cell.toLowerCase().replace(/[^a-z0-9]/g, ''))
      const value = (row: string[], names: string[]) => {
        const index = names.map((name) => keys.indexOf(name)).find((item) => item >= 0) ?? -1
        return index >= 0 ? row[index] : ''
      }
      void (async () => {
        let created = 0
        let failed = 0
        for (const row of rows) {
          const company = value(row, ['company', 'business', 'name', 'customer'])
          if (!company) continue
          try {
            const account = await createCrmAccount({
              name: company,
              legalName: value(row, ['legalname']) || undefined,
              contactName: value(row, ['contact', 'contactperson', 'primarycontact', 'person']) || undefined,
              phone: value(row, ['phone', 'mobile', 'mobilenumber']) || undefined,
              email: value(row, ['email', 'mail']) || undefined,
              gstin: value(row, ['gstin']) || undefined,
              displayCode: value(row, ['code', 'displaycode', 'customercode']) || undefined,
              groups: parseGroups(value(row, ['groups', 'group', 'customergroup'])),
            })
            const importedStatus = value(row, ['status', 'customerstatus'])
            if (['Active', 'Inactive', 'Archived'].includes(importedStatus) && importedStatus !== 'Active') {
              await updateCrmAccountProfile(account.id, {
                name: account.name,
                legalName: account.legalName || undefined,
                gstin: account.gstin || undefined,
                displayCode: account.displayCode || undefined,
                status: importedStatus,
                groups: account.groups || [],
              })
            }
            created += 1
          } catch {
            failed += 1
          }
        }
        notify(`Imported ${created} customer(s) from ${fileName}${failed ? `; ${failed} failed` : ''}`)
        await refresh()
      })().catch((error) => notify(error instanceof Error ? error.message : String(error)))
    }, notify)
  }

  if (view === 'accounts') {
    const activeCustomers = accounts.filter((account) => account.status === 'Active').length
    const inactiveCustomers = accounts.filter((account) => account.status === 'Inactive').length
    const activeContacts = accounts.reduce((sum, account) => sum + account.contacts.length, 0)
    return (
      <section className="crm2-ref-list-page">
        <div className="crm2-ref-action-row">
          {canManageAccounts ? <button className="crm2-ref-primary" onClick={() => setShowAccount(true)}>+ New Customer</button> : null}
          <button className="crm2-ref-primary" onClick={importCustomers}>Import CSV / Excel</button>
          <button className="crm2-ref-outline" onClick={() => accounts[0] && openAccount(accounts[0])}>Contacts</button>
        </div>
        <section className="crm2-ref-summary-card">
          <h2>Customers Summary</h2>
          <div className="crm2-ref-summary-line">
            <div><strong>{accounts.length}</strong><span>Total Customers</span></div>
            <div><strong>{activeCustomers}</strong><span className="good">Active Customers</span></div>
            <div><strong>{inactiveCustomers}</strong><span className="bad">Inactive Customers</span></div>
            <div><strong>{activeContacts}</strong><span>Active Contacts</span></div>
            <div><strong>{customerGroups.length}</strong><span>Customer Groups</span></div>
          </div>
        </section>
        <div className="crm2-ref-inline-checks">
          <label><input type="checkbox" checked={excludeInactive} onChange={(e) => setExcludeInactive(e.target.checked)} /> Exclude Inactive</label>
        </div>
        <section className="crm2-ref-table-card">
          <div className="crm2-ref-table-tools">
            <select><option>25</option><option>50</option></select>
            <button onClick={() => void exportCustomers('xlsx')}>Export</button>
            {canManageAccounts ? <button disabled={busy || selectedAccountIds.length === 0} onClick={() => setShowBulkCustomerActions(value => !value)}>Bulk Actions</button> : null}
            <button onClick={refresh}>↻</button>
            <select value={customerStatusFilter} onChange={(e) => setCustomerStatusFilter(e.target.value)}><option value="All">All statuses</option><option>Active</option><option>Inactive</option><option>Archived</option></select>
            <select value={customerGroupFilter} onChange={(e) => setCustomerGroupFilter(e.target.value)}><option value="All">All groups</option>{customerGroups.map((group) => <option key={group}>{group}</option>)}</select>
            <span />
            <label><b>⌕</b><input value={customerQuery} onChange={(e) => setCustomerQuery(e.target.value)} placeholder="Search..." /></label>
          </div>
          {canManageAccounts && showBulkCustomerActions ? <div className="crm2-ref-bulk-panel">
            <select value={bulkCustomerStatus} onChange={(e) => setBulkCustomerStatus(e.target.value)}><option value="">Change status...</option><option>Active</option><option>Inactive</option><option>Archived</option></select>
            <select value={bulkCustomerGroupMode} onChange={(e) => setBulkCustomerGroupMode(e.target.value as 'add' | 'remove')}><option value="add">Add Group</option><option value="remove">Remove Group</option></select>
            <input value={bulkCustomerGroup} onChange={(e) => setBulkCustomerGroup(e.target.value)} placeholder="Group name" />
            <button disabled={busy} onClick={() => void applyCustomerBulkAction()}>Apply</button>
            <button onClick={() => setShowBulkCustomerActions(false)}>Close</button>
          </div> : null}
          <div className="crm2-ref-customers-head"><span><input type="checkbox" checked={filteredAccounts.length > 0 && filteredAccounts.every((account) => selectedAccountIds.includes(account.id))} onChange={(e) => setSelectedAccountIds((ids) => e.target.checked ? Array.from(new Set([...ids, ...filteredAccounts.map((account) => account.id)])) : ids.filter((id) => !filteredAccounts.some((account) => account.id === id)))} /></span><span>#</span><span>Company</span><span>Primary Contact</span><span>Primary Email</span><span>Phone</span><span>Active</span><span>Groups</span></div>
          {filteredAccounts.length === 0 ? <p className="crm2-reference-empty">No entries found</p> : filteredAccounts.map((account, index) => (
            <article className="crm2-ref-customers-row" key={account.id} onClick={() => openAccount(account)}>
              <span><input type="checkbox" checked={selectedAccountIds.includes(account.id)} onClick={(e) => e.stopPropagation()} onChange={(e) => toggleSelectedAccount(account.id, e.target.checked)} /></span><span>{index + 1}</span><span><a>{account.name}</a></span><span>{account.primaryContact?.name || '-'}</span><span><a>{account.primaryContact?.email || '-'}</a></span><span>{account.primaryContact?.phone || '-'}</span><span><label className="crm2-ref-switch" onClick={(e) => e.stopPropagation()}><input type="checkbox" checked={account.status === 'Active'} disabled={busy || !canManageAccounts} onChange={() => void toggleAccountStatus(account)} /><i /></label></span><span>{(account.groups || []).length ? (account.groups || []).map((group) => <em key={group}>{group}</em>) : <small>—</small>}</span>
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
              <label>Customer groups<input value={accountGroups} onChange={(e) => setAccountGroups(e.target.value)} placeholder="VIP, Dealer, Partner" /></label>
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
              {canManageAccounts ? <div className="crm2-form-section">
                <div className="crm2-form-grid">
                  <label>Business name<input value={detailName} onChange={(e) => setDetailName(e.target.value)} /></label>
                  <label>Legal name<input value={detailLegalName} onChange={(e) => setDetailLegalName(e.target.value)} /></label>
                  <label>GSTIN<input value={detailGstin} onChange={(e) => setDetailGstin(e.target.value)} /></label>
                  <label>Customer code<input value={detailCode} onChange={(e) => setDetailCode(e.target.value)} /></label>
                  <label>Status<select value={detailStatus} onChange={(e) => setDetailStatus(e.target.value)}><option>Active</option><option>Inactive</option><option>Archived</option></select></label>
                  <label>Groups<input value={detailGroups} onChange={(e) => setDetailGroups(e.target.value)} placeholder="VIP, Dealer, Partner" /></label>
                </div>
                <div className="crm2-drawer-actions"><button className="crm2-primary" disabled={busy || !detailName.trim()} onClick={() => void saveSelectedAccount()}>Save customer</button></div>
              </div> : null}
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
