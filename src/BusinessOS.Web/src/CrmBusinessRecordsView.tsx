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
import { exportCrmSpreadsheet, pickCrmSpreadsheet, type CrmSpreadsheetFormat } from './crmSpreadsheet'

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

const config: Record<Props['view'], { module: CrmBusinessModule; title: string; primary: string; statuses: string[]; amountLabel: string }> = {
  expenses: { module: 'Expense', title: 'Expenses', primary: '+ Record Expense', statuses: ['Draft', 'Approved', 'Paid', 'Rejected'], amountLabel: 'Amount' },
  contracts: { module: 'Contract', title: 'Contracts', primary: '+ New Contract', statuses: ['Draft', 'Active', 'Completed', 'Expired', 'Cancelled'], amountLabel: 'Contract Value' },
  projects: { module: 'Project', title: 'Projects', primary: '+ New Project', statuses: ['Planned', 'InProgress', 'OnHold', 'Completed', 'Cancelled'], amountLabel: 'Budget' },
  support: { module: 'Ticket', title: 'Support', primary: '+ New Ticket', statuses: ['Open', 'InProgress', 'Resolved', 'Closed', 'Cancelled'], amountLabel: 'Value' },
}

function money(value?: number | null) {
  if (value == null) return '—'
  return new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR', maximumFractionDigits: 2 }).format(value)
}
function today() { return new Date().toISOString().slice(0, 10) }
function fmtDate(value?: string | null) {
  if (!value) return '—'
  const d = new Date(value.length === 10 ? value + 'T00:00:00' : value)
  return Number.isNaN(d.getTime()) ? value : d.toLocaleDateString('en-IN')
}

export function CrmBusinessRecordsView({ view, accounts, records, teamMembers, busy, refresh, notify, canManage }: Props) {
  const cfg = config[view]
  const [query, setQuery] = useState('')
  const [status, setStatus] = useState('All')
  const [creating, setCreating] = useState(false)
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
  const [expenseReceipt, setExpenseReceipt] = useState('')
  const [expenseProject, setExpenseProject] = useState('')
  const [expenseInvoice, setExpenseInvoice] = useState('')
  const [expenseReference, setExpenseReference] = useState('')
  const [expensePaymentMode, setExpensePaymentMode] = useState('')
  const [selectedExpenseIds, setSelectedExpenseIds] = useState<string[]>([])

  const moduleRecords = useMemo(() => records.filter(record => record.module === cfg.module), [records, cfg.module])
  const filtered = useMemo(() => {
    const search = query.trim().toLowerCase()
    return moduleRecords.filter(record => {
      if (status !== 'All' && record.status !== status) return false
      const account = accounts.find(item => item.id === record.accountId)
      const owner = teamMembers.find(item => item.id === record.ownerUserId)
      const haystack = [record.title, record.status, record.category, record.priority, record.description, account?.name, owner?.displayName, ...Object.values(record.metadata || {})].filter(Boolean).join(' ').toLowerCase()
      return !search || haystack.includes(search)
    })
  }, [accounts, moduleRecords, query, status, teamMembers])

  const count = (value: string) => moduleRecords.filter(record => record.status === value).length
  const accountName = (record: CrmBusinessRecord) => accounts.find(item => item.id === record.accountId)?.name || '—'
  const ownerName = (record: CrmBusinessRecord) => teamMembers.find(item => item.id === record.ownerUserId)?.displayName || '—'
  const meta = (record: CrmBusinessRecord, key: string) => record.metadata?.[key] || '—'

  function resetForm() {
    setCreating(false); setEditing(null); setTitle(''); setAccountId(''); setAmount(''); setCategory('')
    setPriority('Normal'); setStartDate(today()); setDueDate(''); setOwnerUserId(''); setDescription('')
    setExpenseReceipt(''); setExpenseProject(''); setExpenseInvoice(''); setExpenseReference(''); setExpensePaymentMode('')
  }
  function openCreate() { resetForm(); setCreating(true) }
  function openEdit(record: CrmBusinessRecord) {
    setCreating(false); setEditing(record); setTitle(record.title); setAccountId(record.accountId || '')
    setAmount(record.amount == null ? '' : String(record.amount)); setCategory(record.category || '')
    setPriority(record.priority || 'Normal'); setStartDate(record.startDate || today()); setDueDate(record.dueDate || '')
    setOwnerUserId(record.ownerUserId || ''); setDescription(record.description || '')
    setExpenseReceipt(record.metadata?.receipt || ''); setExpenseProject(record.metadata?.project || '')
    setExpenseInvoice(record.metadata?.invoice || ''); setExpenseReference(record.metadata?.reference || '')
    setExpensePaymentMode(record.metadata?.paymentMode || '')
  }
  async function save() {
    if (!title.trim()) { notify('Title is required'); return }
    const parsedAmount = amount.trim() ? Number(amount) : null
    if (amount.trim() && (!Number.isFinite(parsedAmount) || (parsedAmount ?? 0) < 0)) { notify('Enter a valid amount'); return }
    if (view === 'expenses') {
      if (!parsedAmount || parsedAmount <= 0) { notify('Expense amount must be greater than zero'); return }
      if (!category.trim()) { notify('Expense category is required'); return }
      if (!startDate) { notify('Expense date is required'); return }
    }
    const metadata: Record<string, string> = { ...(editing?.metadata || {}) }
    if (view === 'expenses') {
      for (const key of ['receipt', 'project', 'invoice', 'reference', 'paymentMode']) delete metadata[key]
      for (const [key, value] of Object.entries({
        receipt: expenseReceipt, project: expenseProject, invoice: expenseInvoice,
        reference: expenseReference, paymentMode: expensePaymentMode,
      })) if (value.trim()) metadata[key] = value.trim()
    }
    const payload = { title: title.trim(), accountId: accountId || null, amount: parsedAmount, category: category.trim() || null, priority: priority.trim() || null, startDate: startDate || null, dueDate: dueDate || null, ownerUserId: ownerUserId || null, description: description.trim() || null, metadata }
    try {
      if (editing) { await updateCrmBusinessRecord(editing.id, payload); notify(cfg.title + ' updated') }
      else { await createCrmBusinessRecord({ ...payload, module: cfg.module }); notify(cfg.title + ' record created') }
      resetForm(); await refresh()
    } catch (error) { notify(error instanceof Error ? error.message : String(error)) }
  }
  async function move(record: CrmBusinessRecord, next: string) {
    try { await changeCrmBusinessRecordStatus(record.id, next); notify(cfg.title + ' status updated'); await refresh() }
    catch (error) { notify(error instanceof Error ? error.message : String(error)) }
  }

  function normalizeHeader(value: string) { return value.trim().toLowerCase().replace(/[^a-z0-9]/g, '') }
  function findColumn(headers: string[], aliases: string[]) {
    const normalized = headers.map(normalizeHeader)
    return aliases.map(normalizeHeader).map(alias => normalized.indexOf(alias)).find(index => index >= 0) ?? -1
  }
  function parseImportAmount(value: string) {
    const parsed = Number(value.replace(/[₹,\s]/g, ''))
    return Number.isFinite(parsed) ? parsed : NaN
  }
  function parseImportDate(value: string) {
    const clean = value.trim()
    if (!clean) return ''
    if (/^\d{4}-\d{2}-\d{2}$/.test(clean)) return clean
    const parsed = new Date(clean)
    return Number.isNaN(parsed.getTime()) ? '' : parsed.toISOString().slice(0, 10)
  }
  async function exportCurrent(format: CrmSpreadsheetFormat) {
    const baseName = `crm-${view}-${today()}`
    const table = view === 'expenses'
      ? {
          headers: ['Category', 'Amount', 'Name', 'Receipt', 'Date', 'Project', 'Customer', 'Invoice', 'Reference #', 'Payment Mode', 'Status'],
          rows: filtered.map(record => [
            record.category || '', record.amount ?? '', record.title, record.metadata?.receipt || '',
            record.startDate || '', record.metadata?.project || '', accountName(record) === '—' ? '' : accountName(record),
            record.metadata?.invoice || '', record.metadata?.reference || '', record.metadata?.paymentMode || '', record.status,
          ]),
        }
      : {
          headers: ['Module', 'Title', 'Customer', 'Amount', 'Category', 'Priority', 'Start Date', 'Due Date', 'Owner', 'Status', 'Description'],
          rows: filtered.map(record => [
            record.module, record.title, accountName(record) === '—' ? '' : accountName(record), record.amount ?? '',
            record.category || '', record.priority || '', record.startDate || '', record.dueDate || '',
            ownerName(record) === '—' ? '' : ownerName(record), record.status, record.description || '',
          ]),
        }
    try {
      await exportCrmSpreadsheet(baseName, table, format)
      notify(`${cfg.title} exported to ${format.toUpperCase()}`)
    } catch (error) { notify(error instanceof Error ? error.message : String(error)) }
  }
  async function importExpenseRows(rows: string[][], fileName: string) {
    if (rows.length < 2) { notify('Expense spreadsheet must include a header and data rows'); return }
    const headers = rows[0]
    const col = {
      category: findColumn(headers, ['Category']),
      amount: findColumn(headers, ['Amount']),
      title: findColumn(headers, ['Name', 'Title', 'Expense']),
      receipt: findColumn(headers, ['Receipt']),
      date: findColumn(headers, ['Date', 'Expense Date']),
      project: findColumn(headers, ['Project']),
      customer: findColumn(headers, ['Customer', 'Client']),
      invoice: findColumn(headers, ['Invoice']),
      reference: findColumn(headers, ['Reference #', 'Reference', 'Ref']),
      paymentMode: findColumn(headers, ['Payment Mode', 'Payment Method']),
      status: findColumn(headers, ['Status']),
    }
    if (col.category < 0 || col.amount < 0 || col.title < 0 || col.date < 0) {
      notify('Import needs Category, Amount, Name and Date columns'); return
    }
    let imported = 0
    const errors: string[] = []
    for (let rowIndex = 1; rowIndex < rows.length; rowIndex += 1) {
      const row = rows[rowIndex]
      const titleValue = row[col.title]?.trim() || ''
      const categoryValue = row[col.category]?.trim() || ''
      const amountValue = parseImportAmount(row[col.amount] || '')
      const dateValue = parseImportDate(row[col.date] || '')
      if (!titleValue || !categoryValue || !Number.isFinite(amountValue) || amountValue <= 0 || !dateValue) {
        errors.push(`row ${rowIndex + 1}`); continue
      }
      const customerName = col.customer >= 0 ? row[col.customer]?.trim() || '' : ''
      const account = customerName
        ? accounts.find(item => item.name.trim().toLowerCase() === customerName.toLowerCase())
        : undefined
      const metadata: Record<string, string> = {}
      for (const [key, index] of Object.entries({
        receipt: col.receipt, project: col.project, invoice: col.invoice,
        reference: col.reference, paymentMode: col.paymentMode,
      })) {
        const value = index >= 0 ? row[index]?.trim() || '' : ''
        if (value) metadata[key] = value
      }
      const statusValue = col.status >= 0 ? row[col.status]?.trim() || '' : ''
      try {
        await createCrmBusinessRecord({
          module: 'Expense', title: titleValue, accountId: account?.id || null, amount: amountValue,
          category: categoryValue, priority: 'Normal', startDate: dateValue, dueDate: null,
          ownerUserId: null, description: null, metadata,
          status: cfg.statuses.includes(statusValue) ? statusValue : null,
        })
        imported += 1
      } catch { errors.push(`row ${rowIndex + 1}`) }
    }
    await refresh()
    notify(`${fileName}: ${imported} expense(s) imported${errors.length ? `; skipped ${errors.length} invalid row(s)` : ''}`)
  }
  function importExpenses() {
    pickCrmSpreadsheet(
      (rows, fileName) => { void importExpenseRows(rows, fileName) },
      message => notify(message),
    )
  }
  function toggleExpenseSelection(id: string) {
    setSelectedExpenseIds(current => current.includes(id) ? current.filter(value => value !== id) : [...current, id])
  }
  function toggleAllFilteredExpenses() {
    const ids = filtered.map(record => record.id)
    const allSelected = ids.length > 0 && ids.every(id => selectedExpenseIds.includes(id))
    setSelectedExpenseIds(allSelected ? selectedExpenseIds.filter(id => !ids.includes(id)) : Array.from(new Set([...selectedExpenseIds, ...ids])))
  }
  async function bulkExpenseStatus(next: string) {
    const ids = selectedExpenseIds.filter(id => filtered.some(record => record.id === id))
    if (ids.length === 0) { notify('Select at least one expense'); return }
    try {
      for (const id of ids) await changeCrmBusinessRecordStatus(id, next)
      setSelectedExpenseIds(current => current.filter(id => !ids.includes(id)))
      await refresh()
      notify(`${ids.length} expense(s) moved to ${next}`)
    } catch (error) { notify(error instanceof Error ? error.message : String(error)) }
  }

  const form = canManage && (creating || editing) ? <section className="crm2-ref-filter-card crm2-reference-edit-form">
    <strong>{editing ? 'Edit' : 'New'} {cfg.title}</strong>
    <div className="crm2-form-grid">
      <label>Title<input value={title} onChange={e => setTitle(e.target.value)} /></label>
      <label>Customer<select value={accountId} onChange={e => setAccountId(e.target.value)}><option value="">No customer</option>{accounts.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}</select></label>
      <label>{cfg.amountLabel}<input type="number" min="0" value={amount} onChange={e => setAmount(e.target.value)} /></label>
      <label>Category<input value={category} onChange={e => setCategory(e.target.value)} /></label>
      <label>Priority<select value={priority} onChange={e => setPriority(e.target.value)}><option>Low</option><option>Normal</option><option>High</option><option>Urgent</option></select></label>
      <label>Owner<select value={ownerUserId} onChange={e => setOwnerUserId(e.target.value)}><option value="">Unassigned</option>{teamMembers.filter(member => member.active).map(member => <option key={member.id} value={member.id}>{member.displayName}</option>)}</select></label>
      <label>{view === 'expenses' ? 'Expense date' : 'Start date'}<input type="date" value={startDate} onChange={e => setStartDate(e.target.value)} /></label>
      <label>Due date<input type="date" value={dueDate} onChange={e => setDueDate(e.target.value)} /></label>
      {view === 'expenses' ? <>
        <label>Receipt / Attachment Ref<input value={expenseReceipt} onChange={e => setExpenseReceipt(e.target.value)} placeholder="Receipt no. or file reference" /></label>
        <label>Project<input value={expenseProject} onChange={e => setExpenseProject(e.target.value)} placeholder="Related project" /></label>
        <label>Invoice<input value={expenseInvoice} onChange={e => setExpenseInvoice(e.target.value)} placeholder="Related invoice" /></label>
        <label>Reference #<input value={expenseReference} onChange={e => setExpenseReference(e.target.value)} placeholder="Transaction / voucher reference" /></label>
        <label>Payment Mode<select value={expensePaymentMode} onChange={e => setExpensePaymentMode(e.target.value)}><option value="">Select</option><option>Cash</option><option>Bank Transfer</option><option>UPI</option><option>Card</option><option>Cheque</option><option>Other</option></select></label>
      </> : null}
    </div>
    <label>Description<textarea value={description} onChange={e => setDescription(e.target.value)} rows={3} /></label>
    <div className="crm2-drawer-actions"><button onClick={resetForm}>Cancel</button><button className="crm2-primary" onClick={() => void save()} disabled={busy}>Save</button></div>
  </section> : null

  const toolbar = <div className="crm2-ref-table-tools">
    <select><option>25</option><option>50</option></select>
    <button onClick={() => void exportCurrent('xlsx')} disabled={busy || filtered.length === 0}>Export XLSX</button>
    <button onClick={() => void exportCurrent('csv')} disabled={busy || filtered.length === 0}>CSV</button>
    {view === 'expenses' && canManage ? <button onClick={toggleAllFilteredExpenses} disabled={filtered.length === 0}>{filtered.length > 0 && filtered.every(record => selectedExpenseIds.includes(record.id)) ? 'Clear Selection' : 'Select All'}</button> : null}
    <button onClick={() => void refresh()} disabled={busy}>↻</button>
    <select value={status} onChange={e => setStatus(e.target.value)}><option>All</option>{cfg.statuses.map(item => <option key={item}>{item}</option>)}</select>
    <span /><label><b>⌕</b><input value={query} onChange={e => setQuery(e.target.value)} placeholder="Search..." /></label>
  </div>

  if (view === 'expenses') return <section className="crm2-ref-list-page crm2-business-records crm2-expenses-reference">
    <div className="crm2-ref-action-row">
      {canManage ? <button className="crm2-ref-primary" onClick={openCreate}>+ Record Expense</button> : null}
      {canManage ? <button onClick={importExpenses} disabled={busy}>↥ Import Expenses</button> : null}
      <span className="crm2-action-spacer" />
      <button onClick={() => void exportCurrent('csv')} disabled={busy || filtered.length === 0}>Export CSV</button>
      <button onClick={() => void exportCurrent('xlsx')} disabled={busy || filtered.length === 0}>Export Excel</button>
    </div>
    {selectedExpenseIds.length > 0 && canManage ? <div className="crm2-ref-action-row">
      <strong>{selectedExpenseIds.length} selected</strong>
      <button onClick={() => void bulkExpenseStatus('Approved')} disabled={busy}>Approve</button>
      <button onClick={() => void bulkExpenseStatus('Paid')} disabled={busy}>Mark Paid</button>
      <button onClick={() => void bulkExpenseStatus('Rejected')} disabled={busy}>Reject</button>
      <button onClick={() => setSelectedExpenseIds([])} disabled={busy}>Clear</button>
    </div> : null}
    {form}
    <section className="crm2-ref-table-card">{toolbar}
      <div className="crm2-expense-head"><span></span><span>Category</span><span>Amount</span><span>Name</span><span>Receipt</span><span>Date</span><span>Project</span><span>Customer</span><span>Invoice</span><span>Reference #</span><span>Payment Mode</span></div>
      {filtered.length === 0 ? <p className="crm2-reference-empty">No entries found</p> : filtered.map(record => <div className="crm2-expense-row" key={record.id} onDoubleClick={() => canManage && openEdit(record)}>
        <span><input type="checkbox" checked={selectedExpenseIds.includes(record.id)} onChange={() => toggleExpenseSelection(record.id)} disabled={!canManage} /></span><span><a>{record.category || '—'}</a></span><span>{money(record.amount)}</span><span><a>{record.title}</a></span><span>{meta(record,'receipt')}</span><span>{fmtDate(record.startDate || record.createdAtUtc)}</span><span>{meta(record,'project')}</span><span>{accountName(record)}</span><span>{meta(record,'invoice')}</span><span>{meta(record,'reference')}</span><span>{meta(record,'paymentMode')}</span>
      </div>)}
    </section>
  </section>

  if (view === 'contracts') {
    const categories = Array.from(new Set(moduleRecords.map(record => record.category || 'Uncategorized')))
    return <section className="crm2-ref-list-page crm2-business-records crm2-contracts-reference">
      <div className="crm2-ref-action-row">{canManage ? <button className="crm2-ref-primary" onClick={openCreate}>+ New Contract</button> : null}<span className="crm2-action-spacer" /><button className="crm2-ref-square">▼</button></div>
      {form}
      <section className="crm2-reference-status-summary">
        <h2>▧ Contract Summary</h2>
        <div><span><b>{count('Active')}</b><em className="good">Active</em></span><span><b>{count('Expired')}</b><em className="bad">Expired</em></span><span><b>{count('Draft')}</b><em className="warn">About to Expire</em></span><span><b>{moduleRecords.filter(r => new Date(r.createdAtUtc).getTime() > Date.now()-30*86400000).length}</b><em className="good">Recently Added</em></span><span><b>{count('Cancelled')}</b><em>Trash</em></span></div>
      </section>
      <section className="crm2-reference-charts">
        <article><h3>Contracts by Type</h3><div className="crm2-bar-chart">{categories.map(cat => { const n=moduleRecords.filter(r => (r.category||'Uncategorized')===cat).length; return <div key={cat}><span>{cat}</span><i><b style={{width: Math.max(3,Math.round(n/Math.max(1,moduleRecords.length)*100))+'%'}} /></i><strong>{n}</strong></div> })}</div></article>
        <article><h3>Contracts Value by Type (INR)</h3><div className="crm2-bar-chart">{categories.map(cat => { const value=moduleRecords.filter(r => (r.category||'Uncategorized')===cat).reduce((sum,r)=>sum+(r.amount||0),0); const max=Math.max(1,...categories.map(c=>moduleRecords.filter(r=>(r.category||'Uncategorized')===c).reduce((sum,r)=>sum+(r.amount||0),0))); return <div key={cat}><span>{cat}</span><i><b style={{width: Math.max(3,Math.round(value/max*100))+'%'}} /></i><strong>{money(value)}</strong></div> })}</div></article>
      </section>
      <section className="crm2-ref-table-card">{toolbar}<div className="crm2-business-head"><span>Contract</span><span>Customer</span><span>Value</span><span>Owner</span><span>Status</span></div>{filtered.length===0?<p className="crm2-reference-empty">No entries found</p>:filtered.map(record=><div className="crm2-business-row" key={record.id} onDoubleClick={()=>canManage&&openEdit(record)}><span><a>{record.title}</a><small>{record.category||'—'}</small></span><span>{accountName(record)}</span><span>{money(record.amount)}</span><span>{ownerName(record)}</span><span>{canManage?<select value={record.status} onChange={e=>void move(record,e.target.value)}>{cfg.statuses.map(item=><option key={item}>{item}</option>)}</select>:record.status}</span></div>)}</section>
    </section>
  }

  if (view === 'projects') return <section className="crm2-ref-list-page crm2-business-records crm2-projects-reference">
    <div className="crm2-ref-action-row">{canManage ? <button className="crm2-ref-primary" onClick={openCreate}>+ New Project</button> : null}<button className="crm2-ref-square">≡</button><span className="crm2-action-spacer" /><button className="crm2-ref-square">▼</button></div>
    {form}
    <section className="crm2-reference-status-summary"><h2>▧ Projects Summary</h2><div><span><b>{count('Planned')}</b><em>Not Started</em></span><span><b>{count('InProgress')}</b><em className="blue">In Progress</em></span><span><b>{count('OnHold')}</b><em className="warn">On Hold</em></span><span><b>{count('Cancelled')}</b><em>Cancelled</em></span><span><b>{count('Completed')}</b><em className="good">Finished</em></span></div></section>
    <section className="crm2-ref-table-card">{toolbar}<div className="crm2-project-head"><span>#</span><span>Project Name</span><span>Customer</span><span>Tags</span><span>Start Date</span><span>Deadline</span><span>Members</span><span>Status</span></div>{filtered.length===0?<p className="crm2-reference-empty">No entries found</p>:filtered.map((record,index)=><div className="crm2-project-row" key={record.id} onDoubleClick={()=>canManage&&openEdit(record)}><span>{index+1}</span><span><a>{record.title}</a></span><span><a>{accountName(record)}</a></span><span>{meta(record,'tags')}</span><span>{fmtDate(record.startDate)}</span><span>{fmtDate(record.dueDate)}</span><span>{ownerName(record)}</span><span>{canManage?<select value={record.status} onChange={e=>void move(record,e.target.value)}>{cfg.statuses.map(item=><option key={item}>{item}</option>)}</select>:record.status}</span></div>)}</section>
  </section>

  return <section className="crm2-ref-list-page crm2-business-records crm2-support-reference">
    <div className="crm2-ref-action-row">{canManage ? <button className="crm2-ref-primary" onClick={openCreate}>+ New Ticket</button> : null}<button className="crm2-ref-square">▤</button><span className="crm2-action-spacer" /><button className="crm2-ref-square">▼</button></div>
    {form}
    <section className="crm2-reference-status-summary"><h2>▧ Tickets Summary</h2><div><span><b>{count('Open')}</b><em className="bad">Open</em></span><span><b>{count('InProgress')}</b><em className="good">In Progress</em></span><span><b>{count('Resolved')}</b><em className="blue">Resolved</em></span><span><b>{count('Cancelled')}</b><em>On Hold</em></span><span><b>{count('Closed')}</b><em className="blue">Closed</em></span></div></section>
    <section className="crm2-ref-table-card">{toolbar}<div className="crm2-ticket-head"><span></span><span>#</span><span>Subject</span><span>Tags</span><span>Department</span><span>Service</span><span>Contact</span><span>Status</span><span>Priority</span><span>Last Reply</span><span>Created</span></div>{filtered.length===0?<p className="crm2-reference-empty">No entries found</p>:filtered.map((record,index)=><div className="crm2-ticket-row" key={record.id} onDoubleClick={()=>canManage&&openEdit(record)}><span><input type="checkbox" /></span><span>{index+1}</span><span><a>{record.title}</a></span><span>{meta(record,'tags')}</span><span>{meta(record,'department')}</span><span>{meta(record,'service')}</span><span>{accountName(record)}</span><span>{canManage?<select value={record.status} onChange={e=>void move(record,e.target.value)}>{cfg.statuses.map(item=><option key={item}>{item}</option>)}</select>:record.status}</span><span>{record.priority||'—'}</span><span>{meta(record,'lastReply')}</span><span>{fmtDate(record.createdAtUtc)}</span></div>)}</section>
  </section>
}
