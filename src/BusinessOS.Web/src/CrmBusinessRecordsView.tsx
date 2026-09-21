import { useMemo, useState } from 'react'
import {
  changeCrmBusinessRecordStatus,
  createCrmBusinessRecord,
  updateCrmBusinessRecord,
  type CrmAccount,
  type CrmBusinessModule,
  type CrmBusinessRecord,
  type CrmTeamMember,
} from './crmApi'

type Props = {
  view: 'expenses' | 'contracts' | 'projects' | 'support'
  accounts: CrmAccount[]
  records: CrmBusinessRecord[]
  teamMembers: CrmTeamMember[]
  busy: boolean
  refresh: () => Promise<void>
  notify: (message: string) => void
  canManage: boolean
}

const config: Record<Props['view'], { module: CrmBusinessModule; title: string; subtitle: string; primary: string; statuses: string[]; amountLabel: string }> = {
  expenses: { module: 'Expense', title: 'Expenses', subtitle: 'Track office, sales and staff expenses with approval/payment status.', primary: '+ Add Expense', statuses: ['Draft', 'Approved', 'Paid', 'Rejected'], amountLabel: 'Amount' },
  contracts: { module: 'Contract', title: 'Contracts', subtitle: 'Manage AMC, service and customer contracts with renewal dates.', primary: '+ New Contract', statuses: ['Draft', 'Active', 'Completed', 'Expired', 'Cancelled'], amountLabel: 'Contract Value' },
  projects: { module: 'Project', title: 'Projects', subtitle: 'Track onboarding, implementation and custom delivery work.', primary: '+ New Project', statuses: ['Planned', 'InProgress', 'OnHold', 'Completed', 'Cancelled'], amountLabel: 'Budget' },
  support: { module: 'Ticket', title: 'Support Tickets', subtitle: 'Handle support requests, complaints and customer help work.', primary: '+ New Ticket', statuses: ['Open', 'InProgress', 'Resolved', 'Closed', 'Cancelled'], amountLabel: 'Value' },
}

function money(value?: number | null) {
  if (value == null) return '—'
  return new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR', maximumFractionDigits: 0 }).format(value)
}

function today() { return new Date().toISOString().slice(0, 10) }

export function CrmBusinessRecordsView({ view, accounts, records, teamMembers, busy, refresh, notify, canManage }: Props) {
  const cfg = config[view]
  const [query, setQuery] = useState('')
  const [status, setStatus] = useState('All')
  const [editing, setEditing] = useState<CrmBusinessRecord | null>(null)
  const [title, setTitle] = useState('')
  const [accountId, setAccountId] = useState('')
  const [amount, setAmount] = useState('')
  const [category, setCategory] = useState('')
  const [priority, setPriority] = useState('Normal')
  const [startDate, setStartDate] = useState(today())
  const [dueDate, setDueDate] = useState('')
  const [ownerUserId, setOwnerUserId] = useState('')
  const [description, setDescription] = useState('')

  const filtered = useMemo(() => {
    const search = query.trim().toLowerCase()
    return records.filter((record) => {
      if (record.module !== cfg.module) return false
      if (status !== 'All' && record.status !== status) return false
      const account = accounts.find((item) => item.id === record.accountId)
      const owner = teamMembers.find((item) => item.id === record.ownerUserId)
      const haystack = [record.title, record.status, record.category, record.priority, record.description, account?.name, owner?.displayName].filter(Boolean).join(' ').toLowerCase()
      return !search || haystack.includes(search)
    })
  }, [accounts, cfg.module, query, records, status, teamMembers])

  function openCreate() {
    setEditing(null); setTitle(''); setAccountId(''); setAmount(''); setCategory(''); setPriority('Normal'); setStartDate(today()); setDueDate(''); setOwnerUserId(''); setDescription('')
  }

  function openEdit(record: CrmBusinessRecord) {
    setEditing(record); setTitle(record.title); setAccountId(record.accountId || ''); setAmount(record.amount == null ? '' : String(record.amount)); setCategory(record.category || ''); setPriority(record.priority || 'Normal'); setStartDate(record.startDate || today()); setDueDate(record.dueDate || ''); setOwnerUserId(record.ownerUserId || ''); setDescription(record.description || '')
  }

  async function save() {
    if (!title.trim()) { notify('Title is required'); return }
    const payload = { title: title.trim(), accountId: accountId || null, amount: amount.trim() ? Number(amount) : null, category: category.trim() || null, priority: priority.trim() || null, startDate: startDate || null, dueDate: dueDate || null, ownerUserId: ownerUserId || null, description: description.trim() || null, metadata: {} }
    try {
      if (editing) { await updateCrmBusinessRecord(editing.id, payload); notify(`${cfg.title} updated`) }
      else { await createCrmBusinessRecord({ ...payload, module: cfg.module }); notify(`${cfg.title} record created`) }
      setTitle(''); setEditing(null); await refresh()
    } catch (error) { notify(error instanceof Error ? error.message : String(error)) }
  }

  async function move(record: CrmBusinessRecord, next: string) {
    try { await changeCrmBusinessRecordStatus(record.id, next); notify(`${cfg.title} status updated`); await refresh() }
    catch (error) { notify(error instanceof Error ? error.message : String(error)) }
  }

  const showForm = canManage && (editing || title !== '')

  return <section className="crm2-ref-list-page crm2-business-records">
    <div className="crm2-reference-module-head"><div><span className="crm2-kicker">BUSINESS MODULE</span><h2>{cfg.title}</h2><p>{cfg.subtitle}</p></div><button className="crm2-filter-button" onClick={openCreate}>{cfg.primary}</button></div>
    <section className="crm2-ref-filter-card"><strong>Filter by</strong><div className="crm2-ref-filter-grid"><select value={status} onChange={(e) => setStatus(e.target.value)}><option>All</option>{cfg.statuses.map((item) => <option key={item}>{item}</option>)}</select><input value={query} onChange={(e) => setQuery(e.target.value)} placeholder={`Search ${cfg.title.toLowerCase()}...`} /><button onClick={() => void refresh()} disabled={busy}>Refresh</button></div></section>
    {showForm ? <section className="crm2-ref-filter-card"><strong>{editing ? 'Edit' : 'New'} {cfg.title}</strong><div className="crm2-form-grid"><label>Title<input value={title} onChange={(e) => setTitle(e.target.value)} /></label><label>Customer<select value={accountId} onChange={(e) => setAccountId(e.target.value)}><option value="">No customer</option>{accounts.map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label><label>{cfg.amountLabel}<input type="number" min="0" value={amount} onChange={(e) => setAmount(e.target.value)} /></label><label>Category<input value={category} onChange={(e) => setCategory(e.target.value)} /></label><label>Priority<select value={priority} onChange={(e) => setPriority(e.target.value)}><option>Low</option><option>Normal</option><option>High</option><option>Urgent</option></select></label><label>Owner<select value={ownerUserId} onChange={(e) => setOwnerUserId(e.target.value)}><option value="">Unassigned</option>{teamMembers.filter((member) => member.active).map((member) => <option key={member.id} value={member.id}>{member.displayName}</option>)}</select></label><label>Start date<input type="date" value={startDate} onChange={(e) => setStartDate(e.target.value)} /></label><label>Due date<input type="date" value={dueDate} onChange={(e) => setDueDate(e.target.value)} /></label></div><label>Description<textarea value={description} onChange={(e) => setDescription(e.target.value)} rows={3} /></label><div className="crm2-drawer-actions"><button onClick={() => { setEditing(null); setTitle('') }}>Cancel</button><button className="crm2-primary" onClick={() => void save()} disabled={busy}>Save</button></div></section> : null}
    <section className="crm2-ref-table-card"><div className="crm2-reference-head"><span>Title</span><span>Customer</span><span>{cfg.amountLabel}</span><span>Owner</span><span>Status</span></div>{filtered.length === 0 ? <p className="crm2-reference-empty">No entries found</p> : filtered.map((record) => { const account = accounts.find((item) => item.id === record.accountId); const owner = teamMembers.find((item) => item.id === record.ownerUserId); return <div className="crm2-reference-row" key={record.id} onDoubleClick={() => canManage ? openEdit(record) : undefined}><span><strong>{record.title}</strong><small>{record.category || record.priority || '—'}</small></span><span>{account?.name || '—'}</span><span>{money(record.amount)}</span><span>{owner?.displayName || 'Unassigned'}</span><span>{canManage ? <select value={record.status} onChange={(e) => void move(record, e.target.value)}>{cfg.statuses.map((item) => <option key={item}>{item}</option>)}</select> : record.status}</span></div> })}</section>
  </section>
}
