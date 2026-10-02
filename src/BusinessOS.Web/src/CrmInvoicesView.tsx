import { useEffect, useMemo, useState } from 'react'
import {
  changeCrmInvoiceStatus,
  changeCrmRecurringInvoiceStatus,
  convertCrmSalesDocumentToInvoice,
  createCrmInvoice,
  createCrmRecurringInvoiceFromInvoice,
  getCrmInvoice,
  listCrmRecurringInvoices,
  recordCrmInvoicePayment,
  runDueCrmRecurringInvoices,
  updateCrmInvoice,
  type CrmAccount,
  type CrmInvoice,
  type CrmInvoicePayment,
  type CrmRecurringInvoiceTemplate,
  type CrmOpportunity,
  type CrmSalesDocument,
  type CrmSalesItem,
} from './crmApi'
import { exportCrmSpreadsheet, type CrmSpreadsheetFormat } from './crmSpreadsheet'

type Props = {
  accounts: CrmAccount[]
  opportunities: CrmOpportunity[]
  documents: CrmSalesDocument[]
  invoices: CrmInvoice[]
  salesItems: CrmSalesItem[]
  busy: boolean
  refresh: () => Promise<void>
  notify: (message: string) => void
  canManageSales: boolean
  quickCreateToken?: number
}

type DraftLine = {
  id?: string
  itemId?: string
  description: string
  quantity: string
  unitPrice: string
  taxPercent: string
}

const statuses = ['All', 'Draft', 'Sent', 'PartiallyPaid', 'Paid', 'Overdue', 'Void'] as const
const methods = ['Cash', 'UPI', 'Bank Transfer', 'Card', 'Cheque', 'Other']
const today = () => new Date().toISOString().slice(0, 10)
const addDays = (date: string, days: number) => {
  const value = new Date(date + 'T00:00:00')
  value.setDate(value.getDate() + days)
  return value.toISOString().slice(0, 10)
}
const emptyLine = (): DraftLine => ({ itemId: '', description: '', quantity: '1', unitPrice: '0', taxPercent: '18' })

function money(value: number, currency = 'INR') {
  try { return new Intl.NumberFormat('en-IN', { style: 'currency', currency, maximumFractionDigits: 2 }).format(value) }
  catch { return `${currency} ${value.toLocaleString('en-IN')}` }
}

function lineSubtotal(line: DraftLine) {
  return Math.max(0, Number(line.quantity) || 0) * Math.max(0, Number(line.unitPrice) || 0)
}

export function CrmInvoicesView({
  accounts, opportunities, documents, invoices, salesItems, busy, refresh, notify, canManageSales, quickCreateToken,
}: Props) {
  const [query, setQuery] = useState('')
  const [statusFilter, setStatusFilter] = useState<(typeof statuses)[number]>('All')
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [payments, setPayments] = useState<CrmInvoicePayment[]>([])
  const [editing, setEditing] = useState(false)
  const [converting, setConverting] = useState(false)
  const [paymentOpen, setPaymentOpen] = useState(false)
  const [accountId, setAccountId] = useState('')
  const [opportunityId, setOpportunityId] = useState('')
  const [subject, setSubject] = useState('')
  const [issueDate, setIssueDate] = useState(today())
  const [dueDate, setDueDate] = useState(addDays(today(), 7))
  const [discountPercent, setDiscountPercent] = useState('0')
  const [notes, setNotes] = useState('')
  const [terms, setTerms] = useState('')
  const [lines, setLines] = useState<DraftLine[]>([emptyLine()])
  const [convertDocumentId, setConvertDocumentId] = useState('')
  const [convertDueDate, setConvertDueDate] = useState(addDays(today(), 7))
  const [paymentAmount, setPaymentAmount] = useState('')
  const [paymentMethod, setPaymentMethod] = useState('UPI')
  const [paymentReference, setPaymentReference] = useState('')
  const [paymentNotes, setPaymentNotes] = useState('')
  const [pageSize, setPageSize] = useState(25)
  const [customerFilter, setCustomerFilter] = useState('')
  const [outstandingOnly, setOutstandingOnly] = useState(false)
  const [filtersOpen, setFiltersOpen] = useState(false)
  const [selectedInvoiceIds, setSelectedInvoiceIds] = useState<string[]>([])
  const [batchOpen, setBatchOpen] = useState(false)
  const [batchMethod, setBatchMethod] = useState('UPI')
  const [batchReference, setBatchReference] = useState('')
  const [batchNotes, setBatchNotes] = useState('')
  const [recurringOpen, setRecurringOpen] = useState(false)
  const [recurringTemplates, setRecurringTemplates] = useState<CrmRecurringInvoiceTemplate[]>([])
  const [recurringSourceId, setRecurringSourceId] = useState('')
  const [recurringName, setRecurringName] = useState('')
  const [recurringFrequency, setRecurringFrequency] = useState<CrmRecurringInvoiceTemplate['frequency']>('Monthly')
  const [recurringNextDate, setRecurringNextDate] = useState(addDays(today(), 30))
  const [recurringDueDays, setRecurringDueDays] = useState('7')
  const [localBusy, setLocalBusy] = useState(false)

  const working = busy || localBusy
  const selected = useMemo(() => invoices.find((invoice) => invoice.id === selectedId) ?? null, [invoices, selectedId])
  const filtered = useMemo(() => {
    const search = query.trim().toLowerCase()
    return invoices.filter((invoice) => {
      const account = accounts.find((item) => item.id === invoice.accountId)
      const matchesStatus = statusFilter === 'All' || invoice.status === statusFilter
      const matchesCustomer = !customerFilter || invoice.accountId === customerFilter
      const matchesOutstanding = !outstandingOnly || invoice.balance > 0
      const haystack = [invoice.invoiceNumber, invoice.subject, invoice.status, account?.name]
        .filter(Boolean).join(' ').toLowerCase()
      return matchesStatus && matchesCustomer && matchesOutstanding && (!search || haystack.includes(search))
    })
  }, [accounts, customerFilter, invoices, outstandingOnly, query, statusFilter])
  const visible = filtered.slice(0, pageSize)

  const availableDocuments = useMemo(() => {
    const used = new Set(invoices.map((invoice) => invoice.sourceDocumentId).filter(Boolean))
    return documents.filter((document) => document.status === 'Accepted' && !used.has(document.id))
  }, [documents, invoices])

  const eligibleOpportunities = useMemo(
    () => opportunities.filter((opportunity) => !accountId || opportunity.accountId === accountId),
    [accountId, opportunities],
  )

  const draftTotals = useMemo(() => {
    const subtotal = lines.reduce((sum, line) => sum + lineSubtotal(line), 0)
    const discountRate = Math.min(100, Math.max(0, Number(discountPercent) || 0))
    const discount = subtotal * discountRate / 100
    const factor = 1 - discountRate / 100
    const tax = lines.reduce((sum, line) =>
      sum + lineSubtotal(line) * factor * Math.min(100, Math.max(0, Number(line.taxPercent) || 0)) / 100, 0)
    return { subtotal, discount, tax, total: subtotal - discount + tax }
  }, [discountPercent, lines])

  async function openDetail(invoiceId: string) {
    setSelectedId(invoiceId)
    try {
      const detail = await getCrmInvoice(invoiceId)
      setPayments(detail.payments)
    } catch (error) {
      notify(error instanceof Error ? error.message : String(error))
    }
  }

  function resetDraft() {
    const issue = today()
    setSelectedId(null)
    setAccountId(accounts.find((item) => item.status === 'Active')?.id || accounts[0]?.id || '')
    setOpportunityId('')
    setSubject('')
    setIssueDate(issue)
    setDueDate(addDays(issue, 7))
    setDiscountPercent('0')
    setNotes('')
    setTerms('')
    setLines([emptyLine()])
    setEditing(true)
  }

  useEffect(() => {
    if (quickCreateToken && canManageSales) resetDraft()
  }, [quickCreateToken, canManageSales])

  function editInvoice(invoice: CrmInvoice) {
    if (invoice.status !== 'Draft') { notify('Only draft invoices can be edited'); return }
    setSelectedId(invoice.id)
    setAccountId(invoice.accountId)
    setOpportunityId(invoice.opportunityId || '')
    setSubject(invoice.subject)
    setIssueDate(invoice.issueDate)
    setDueDate(invoice.dueDate)
    setDiscountPercent(String(invoice.discountPercent))
    setNotes(invoice.notes || '')
    setTerms(invoice.terms || '')
    setLines(invoice.lines.map((line) => ({
      id: line.id, itemId: line.itemId || '', description: line.description, quantity: String(line.quantity),
      unitPrice: String(line.unitPrice), taxPercent: String(line.taxPercent),
    })))
    setEditing(true)
  }

  function updateLine(index: number, patch: Partial<DraftLine>) {
    setLines((items) => items.map((line, i) => i === index ? { ...line, ...patch } : line))
  }

  function selectItem(index: number, itemId: string) {
    const item = salesItems.find((x) => x.id === itemId)
    updateLine(index, item ? {
      itemId: item.id,
      description: item.description || item.name,
      unitPrice: String(item.defaultRate),
      taxPercent: String(item.defaultTaxPercent),
    } : { itemId: '' })
  }

  async function saveInvoice() {
    if (!accountId || !subject.trim()) { notify('Select customer and enter invoice subject'); return }
    const cleanLines = lines.filter((line) => line.description.trim())
    if (!cleanLines.length) { notify('Add at least one invoice line'); return }
    const payload = {
      accountId, opportunityId: opportunityId || null, subject: subject.trim(), currencyCode: 'INR',
      issueDate, dueDate, discountPercent: Math.max(0, Number(discountPercent) || 0),
      notes: notes.trim() || undefined, terms: terms.trim() || undefined,
      lines: cleanLines.map((line) => ({
        id: line.id, itemId: line.itemId || null, description: line.description.trim(), quantity: Number(line.quantity) || 0,
        unitPrice: Number(line.unitPrice) || 0, taxPercent: Number(line.taxPercent) || 0,
      })),
    }
    try {
      if (selected?.status === 'Draft') {
        await updateCrmInvoice(selected.id, payload)
        notify('Invoice updated')
      } else {
        await createCrmInvoice(payload)
        notify('Invoice created')
      }
      setEditing(false)
      setSelectedId(null)
      await refresh()
    } catch (error) {
      notify(error instanceof Error ? error.message : String(error))
    }
  }

  async function convertDocument() {
    if (!convertDocumentId) { notify('Select an accepted proposal or estimate'); return }
    try {
      const invoice = await convertCrmSalesDocumentToInvoice(convertDocumentId, { issueDate: today(), dueDate: convertDueDate })
      notify(`${invoice.invoiceNumber} created from accepted sales document`)
      setConverting(false)
      setConvertDocumentId('')
      await refresh()
      await openDetail(invoice.id)
    } catch (error) {
      notify(error instanceof Error ? error.message : String(error))
    }
  }

  async function moveStatus(status: CrmInvoice['status']) {
    if (!selected) return
    try {
      const updated = await changeCrmInvoiceStatus(selected.id, status, today())
      notify(`${updated.invoiceNumber} moved to ${updated.status}`)
      await refresh()
      await openDetail(updated.id)
    } catch (error) {
      notify(error instanceof Error ? error.message : String(error))
    }
  }

  function beginPayment() {
    if (!selected || selected.balance <= 0) return
    setPaymentAmount(String(selected.balance))
    setPaymentMethod('UPI')
    setPaymentReference('')
    setPaymentNotes('')
    setPaymentOpen(true)
  }

  async function savePayment() {
    if (!selected) return
    try {
      const result = await recordCrmInvoicePayment(selected.id, {
        amount: Number(paymentAmount) || 0,
        method: paymentMethod,
        reference: paymentReference.trim() || undefined,
        notes: paymentNotes.trim() || undefined,
      })
      notify(`${result.payment.paymentNumber} recorded; balance ${money(result.invoice.balance)}`)
      setPaymentOpen(false)
      await refresh()
      await openDetail(selected.id)
    } catch (error) {
      notify(error instanceof Error ? error.message : String(error))
    }
  }

  function toggleInvoiceSelection(id: string) {
    setSelectedInvoiceIds(current => current.includes(id) ? current.filter(value => value !== id) : [...current, id])
  }

  function toggleVisibleInvoiceSelection() {
    const ids = visible.map(invoice => invoice.id)
    const allSelected = ids.length > 0 && ids.every(id => selectedInvoiceIds.includes(id))
    setSelectedInvoiceIds(allSelected
      ? selectedInvoiceIds.filter(id => !ids.includes(id))
      : Array.from(new Set([...selectedInvoiceIds, ...ids])))
  }

  async function exportInvoices(format: CrmSpreadsheetFormat) {
    try {
      await exportCrmSpreadsheet(`crm-invoices-${today()}`, {
        headers: ['Invoice', 'Customer', 'Subject', 'Issue Date', 'Due Date', 'Total', 'Paid', 'Balance', 'Status'],
        rows: filtered.map(invoice => [
          invoice.invoiceNumber,
          accounts.find(account => account.id === invoice.accountId)?.name || '',
          invoice.subject,
          invoice.issueDate,
          invoice.dueDate,
          invoice.total,
          invoice.amountPaid,
          invoice.balance,
          invoice.status,
        ]),
      }, format)
      notify(`Invoices exported to ${format.toUpperCase()}`)
    } catch (error) {
      notify(error instanceof Error ? error.message : String(error))
    }
  }

  function openBatchPayments() {
    const payable = invoices.filter(invoice =>
      selectedInvoiceIds.includes(invoice.id) &&
      invoice.balance > 0 &&
      !['Draft', 'Void', 'Paid'].includes(invoice.status))
    if (payable.length === 0) {
      notify('Select at least one payable invoice')
      return
    }
    setBatchMethod('UPI')
    setBatchReference('')
    setBatchNotes('')
    setBatchOpen(true)
  }

  async function saveBatchPayments() {
    const payable = invoices.filter(invoice =>
      selectedInvoiceIds.includes(invoice.id) &&
      invoice.balance > 0 &&
      !['Draft', 'Void', 'Paid'].includes(invoice.status))
    if (payable.length === 0) { notify('No payable invoices selected'); return }
    setLocalBusy(true)
    let completed = 0
    try {
      for (const invoice of payable) {
        await recordCrmInvoicePayment(invoice.id, {
          amount: invoice.balance,
          method: batchMethod,
          reference: batchReference.trim() || undefined,
          notes: batchNotes.trim() || undefined,
        })
        completed += 1
      }
      setSelectedInvoiceIds([])
      setBatchOpen(false)
      await refresh()
      notify(`${completed} invoice payment(s) recorded`)
    } catch (error) {
      await refresh()
      notify(`Batch payments stopped after ${completed}: ${error instanceof Error ? error.message : String(error)}`)
    } finally {
      setLocalBusy(false)
    }
  }

  async function loadRecurringTemplates() {
    const result = await listCrmRecurringInvoices()
    setRecurringTemplates(result.templates)
  }

  async function openRecurringManager() {
    setLocalBusy(true)
    try {
      await loadRecurringTemplates()
      const source = (selected && selected.status !== 'Void' ? selected : invoices.find(invoice => invoice.status !== 'Void')) || null
      setRecurringSourceId(source?.id || '')
      setRecurringName(source ? `${source.subject} recurring` : '')
      setRecurringFrequency('Monthly')
      setRecurringNextDate(addDays(today(), 30))
      setRecurringDueDays('7')
      setRecurringOpen(true)
    } catch (error) {
      notify(error instanceof Error ? error.message : String(error))
    } finally {
      setLocalBusy(false)
    }
  }

  async function saveRecurringTemplate() {
    const dueDays = Number(recurringDueDays)
    if (!recurringSourceId) { notify('Select a source invoice'); return }
    if (!recurringName.trim()) { notify('Recurring template name is required'); return }
    if (!Number.isInteger(dueDays) || dueDays < 0 || dueDays > 365) { notify('Due days must be between 0 and 365'); return }
    setLocalBusy(true)
    try {
      await createCrmRecurringInvoiceFromInvoice({
        sourceInvoiceId: recurringSourceId,
        name: recurringName.trim(),
        frequency: recurringFrequency,
        nextIssueDate: recurringNextDate,
        dueDays,
      })
      await loadRecurringTemplates()
      notify('Recurring invoice template created')
    } catch (error) {
      notify(error instanceof Error ? error.message : String(error))
    } finally {
      setLocalBusy(false)
    }
  }

  async function toggleRecurringTemplate(template: CrmRecurringInvoiceTemplate) {
    setLocalBusy(true)
    try {
      await changeCrmRecurringInvoiceStatus(template.id, !template.active)
      await loadRecurringTemplates()
      notify(`Recurring template ${template.active ? 'paused' : 'activated'}`)
    } catch (error) {
      notify(error instanceof Error ? error.message : String(error))
    } finally {
      setLocalBusy(false)
    }
  }

  async function runDueRecurring() {
    setLocalBusy(true)
    try {
      const result = await runDueCrmRecurringInvoices(today())
      await Promise.all([refresh(), loadRecurringTemplates()])
      notify(`${result.generatedCount} recurring invoice(s) generated`)
    } catch (error) {
      notify(error instanceof Error ? error.message : String(error))
    } finally {
      setLocalBusy(false)
    }
  }

  const actions = selected ? (
    selected.status === 'Draft' ? ['Sent', 'Void'] :
    selected.status === 'Sent' ? ['Draft', 'Overdue', 'Void'] :
    selected.status === 'Overdue' && selected.amountPaid === 0 ? ['Void'] : []
  ) as CrmInvoice['status'][] : []

  return (
    <section className="crm2-ref-list-page crm2-invoices">
      <div className="crm2-ref-action-row">
        {canManageSales ? <button className="crm2-ref-primary" onClick={resetDraft}>+ New Invoice</button> : null}
        {canManageSales ? <button onClick={openBatchPayments} disabled={working || selectedInvoiceIds.length === 0}>Batch Payments</button> : null}
        {canManageSales ? <button onClick={() => void openRecurringManager()} disabled={working}>Recurring Invoices</button> : null}
        {canManageSales ? <button onClick={() => { setConvertDocumentId(availableDocuments[0]?.id || ''); setConvertDueDate(addDays(today(), 7)); setConverting(true) }}>Convert Accepted Document</button> : null}
        <button className={filtersOpen ? 'crm2-ref-square active' : 'crm2-ref-square'} title="Filter" onClick={() => setFiltersOpen(value => !value)}>▼</button>
      </div>
      {filtersOpen ? <section className="crm2-ref-filter-card">
        <div className="crm2-form-grid">
          <label>Customer<select value={customerFilter} onChange={(e) => setCustomerFilter(e.target.value)}><option value="">All customers</option>{accounts.map(account => <option key={account.id} value={account.id}>{account.name}</option>)}</select></label>
          <label>Status<select value={statusFilter} onChange={(e) => setStatusFilter(e.target.value as typeof statusFilter)}>{statuses.map((status) => <option key={status}>{status}</option>)}</select></label>
          <label><span>Outstanding only</span><input type="checkbox" checked={outstandingOnly} onChange={(e) => setOutstandingOnly(e.target.checked)} /></label>
        </div>
        <button onClick={() => { setCustomerFilter(''); setStatusFilter('All'); setOutstandingOnly(false); setQuery('') }}>Clear Filters</button>
      </section> : null}
      {selectedInvoiceIds.length > 0 ? <div className="crm2-ref-action-row">
        <strong>{selectedInvoiceIds.length} selected</strong>
        {canManageSales ? <button onClick={openBatchPayments} disabled={working}>Record Full Balance Payments</button> : null}
        <button onClick={() => setSelectedInvoiceIds([])} disabled={working}>Clear</button>
      </div> : null}
      <section className="crm2-ref-table-card">
        <div className="crm2-ref-table-tools">
          <select value={pageSize} onChange={(e) => setPageSize(Number(e.target.value))}><option value={25}>25</option><option value={50}>50</option></select>
          <button onClick={() => void exportInvoices('xlsx')} disabled={working || filtered.length === 0}>Export XLSX</button>
          <button onClick={() => void exportInvoices('csv')} disabled={working || filtered.length === 0}>CSV</button>
          {canManageSales ? <button onClick={toggleVisibleInvoiceSelection} disabled={working || visible.length === 0}>{visible.length > 0 && visible.every(invoice => selectedInvoiceIds.includes(invoice.id)) ? 'Clear Selection' : 'Select Visible'}</button> : null}
          <button onClick={() => void refresh()} disabled={working}>↻</button>
          <select value={statusFilter} onChange={(e) => setStatusFilter(e.target.value as typeof statusFilter)}>{statuses.map((status) => <option key={status}>{status}</option>)}</select>
          <span />
          <label><b>⌕</b><input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Search..." /></label>
        </div>
        <div className="crm2-invoice-head">
          <span></span><span>Invoice</span><span>Customer</span><span>Subject</span><span>Total</span><span>Paid</span><span>Balance</span><span>Status</span><span>Due</span>
        </div>
        {filtered.length === 0 ? <p className="crm2-reference-empty">No invoices found</p> : visible.map((invoice) => {
          const account = accounts.find((item) => item.id === invoice.accountId)
          return <div className="crm2-invoice-row" key={invoice.id} onDoubleClick={() => void openDetail(invoice.id)}>
            <span><input type="checkbox" checked={selectedInvoiceIds.includes(invoice.id)} onChange={() => toggleInvoiceSelection(invoice.id)} disabled={!canManageSales} /></span>
            <span><button className="crm2-link-button" onClick={() => void openDetail(invoice.id)}><strong>{invoice.invoiceNumber}</strong></button></span>
            <span>{account?.name || 'Unknown customer'}</span>
            <span>{invoice.subject}</span><span>{money(invoice.total)}</span><span>{money(invoice.amountPaid)}</span>
            <span>{money(invoice.balance)}</span><span><em className={`crm2-sales-status ${invoice.status.toLowerCase()}`}>{invoice.status}</em></span>
            <span>{invoice.dueDate}</span>
          </div>
        })}
      </section>

      {batchOpen ? <div className="crm2-overlay" onMouseDown={() => setBatchOpen(false)}>
        <section className="crm2-drawer" onMouseDown={(e) => e.stopPropagation()}>
          <div className="crm2-drawer-head">
            <div><span className="crm2-kicker">BATCH PAYMENTS</span><h2>Record Full Balances</h2><p>{invoices.filter(invoice => selectedInvoiceIds.includes(invoice.id) && invoice.balance > 0 && !['Draft','Void','Paid'].includes(invoice.status)).length} payable invoice(s)</p></div>
            <button onClick={() => setBatchOpen(false)}>×</button>
          </div>
          <div className="crm2-sales-total-box">
            <strong>Total to record <b>{money(invoices.filter(invoice => selectedInvoiceIds.includes(invoice.id) && invoice.balance > 0 && !['Draft','Void','Paid'].includes(invoice.status)).reduce((sum, invoice) => sum + invoice.balance, 0))}</b></strong>
          </div>
          <label>Method<select value={batchMethod} onChange={(e) => setBatchMethod(e.target.value)}>{methods.map(method => <option key={method}>{method}</option>)}</select></label>
          <label>Reference<input value={batchReference} onChange={(e) => setBatchReference(e.target.value)} placeholder="Common bank / UPI / cheque reference" /></label>
          <label>Notes<textarea rows={2} value={batchNotes} onChange={(e) => setBatchNotes(e.target.value)} /></label>
          <div className="crm2-drawer-actions">
            <button onClick={() => setBatchOpen(false)}>Cancel</button>
            <button className="crm2-primary" disabled={working} onClick={() => void saveBatchPayments()}>Record Batch Payments</button>
          </div>
        </section>
      </div> : null}

      {recurringOpen ? <div className="crm2-overlay" onMouseDown={() => setRecurringOpen(false)}>
        <section className="crm2-drawer crm2-wide-drawer" onMouseDown={(e) => e.stopPropagation()}>
          <div className="crm2-drawer-head">
            <div><span className="crm2-kicker">RECURRING INVOICES</span><h2>Recurring Templates</h2><p>Generate draft invoices from saved billing schedules.</p></div>
            <button onClick={() => setRecurringOpen(false)}>×</button>
          </div>
          <div className="crm2-form-grid">
            <label>Source invoice<select value={recurringSourceId} onChange={(e) => { const id=e.target.value; setRecurringSourceId(id); const src=invoices.find(invoice=>invoice.id===id); if(src) setRecurringName(`${src.subject} recurring`) }}><option value="">Select invoice</option>{invoices.filter(invoice => invoice.status !== 'Void').map(invoice => <option key={invoice.id} value={invoice.id}>{invoice.invoiceNumber} — {invoice.subject}</option>)}</select></label>
            <label>Template name<input value={recurringName} onChange={(e) => setRecurringName(e.target.value)} /></label>
            <label>Frequency<select value={recurringFrequency} onChange={(e) => setRecurringFrequency(e.target.value as CrmRecurringInvoiceTemplate['frequency'])}><option>Monthly</option><option>Quarterly</option><option>Yearly</option></select></label>
            <label>Next issue date<input type="date" value={recurringNextDate} onChange={(e) => setRecurringNextDate(e.target.value)} /></label>
            <label>Due after days<input type="number" min="0" max="365" value={recurringDueDays} onChange={(e) => setRecurringDueDays(e.target.value)} /></label>
          </div>
          <div className="crm2-ref-action-row">
            <button className="crm2-primary" disabled={working || !recurringSourceId} onClick={() => void saveRecurringTemplate()}>+ Create Template</button>
            <button disabled={working} onClick={() => void runDueRecurring()}>Run Due Now</button>
          </div>
          <section className="crm2-payment-history">
            <div className="crm2-sales-edit-head"><strong>Schedules</strong><span>{recurringTemplates.length} template(s)</span></div>
            {recurringTemplates.length === 0 ? <p>No recurring templates yet.</p> : recurringTemplates.map(template => <div key={template.id}>
              <strong>{template.name}</strong>
              <span>{template.frequency}</span>
              <span>Next: {template.nextIssueDate}</span>
              <span>{accounts.find(account => account.id === template.accountId)?.name || 'Customer'}</span>
              <button disabled={working} onClick={() => void toggleRecurringTemplate(template)}>{template.active ? 'Pause' : 'Activate'}</button>
            </div>)}
          </section>
        </section>
      </div> : null}

      {selected && !editing ? <div className="crm2-overlay" onMouseDown={() => setSelectedId(null)}>
        <section className="crm2-drawer crm2-wide-drawer" onMouseDown={(e) => e.stopPropagation()}>
          <div className="crm2-drawer-head">
            <div><span className="crm2-kicker">INVOICE</span><h2>{selected.invoiceNumber}</h2><p>{selected.subject}</p></div>
            <button onClick={() => setSelectedId(null)}>×</button>
          </div>
          <div className="crm2-sales-detail-grid">
            <span><small>Customer</small><strong>{accounts.find((item) => item.id === selected.accountId)?.name || '-'}</strong></span>
            <span><small>Status</small><strong>{selected.status}</strong></span>
            <span><small>Issue date</small><strong>{selected.issueDate}</strong></span>
            <span><small>Due date</small><strong>{selected.dueDate}</strong></span>
          </div>
          <div className="crm2-sales-lines">
            <div><b>Description</b><b>Qty</b><b>Rate</b><b>Tax</b><b>Amount</b></div>
            {selected.lines.map((line) => <div key={line.id}>
              <span>{line.description}</span><span>{line.quantity}</span><span>{money(line.unitPrice)}</span>
              <span>{line.taxPercent}%</span><span>{money(line.subtotal)}</span>
            </div>)}
          </div>
          <div className="crm2-sales-total-box">
            <span>Subtotal <b>{money(selected.subtotal)}</b></span><span>Discount <b>{money(selected.discountAmount)}</b></span>
            <span>GST / Tax <b>{money(selected.taxAmount)}</b></span><span>Paid <b>{money(selected.amountPaid)}</b></span>
            <span>Credited <b>{money(selected.amountCredited)}</b></span><span>Net total <b>{money(selected.netTotal)}</b></span>
            {selected.overpaidAmount > 0 ? <span>Customer credit <b>{money(selected.overpaidAmount)}</b></span> : null}
            <strong>Balance <b>{money(selected.balance)}</b></strong>
          </div>
          <section className="crm2-payment-history">
            <div className="crm2-sales-edit-head"><strong>Payments</strong>{canManageSales && selected.balance > 0 && !['Draft', 'Void'].includes(selected.status) ? <button onClick={beginPayment}>+ Record Payment</button> : null}</div>
            {payments.length === 0 ? <p>No payments recorded</p> : payments.map((payment) => <div key={payment.id}>
              <strong>{payment.paymentNumber}</strong><span>{payment.method}</span><span>{payment.reference || '-'}</span><span>{new Date(payment.receivedAtUtc).toLocaleDateString('en-IN')}</span><b>{money(payment.amount)}</b>
            </div>)}
          </section>
          <div className="crm2-drawer-actions">
            {canManageSales && selected.status === 'Draft' ? <button onClick={() => editInvoice(selected)}>Edit</button> : null}
            {canManageSales ? actions.map((status) => <button key={status} onClick={() => void moveStatus(status)}>{status === 'Sent' ? 'Mark Sent' : status}</button>) : null}
          </div>
        </section>
      </div> : null}

      {editing ? <div className="crm2-overlay" onMouseDown={() => setEditing(false)}>
        <section className="crm2-drawer crm2-wide-drawer" onMouseDown={(e) => e.stopPropagation()}>
          <div className="crm2-drawer-head"><div><span className="crm2-kicker">{selected ? 'EDIT' : 'NEW'} INVOICE</span><h2>{selected?.invoiceNumber || 'Create Invoice'}</h2></div><button onClick={() => setEditing(false)}>×</button></div>
          <div className="crm2-form-grid">
            <label>Customer<select value={accountId} onChange={(e) => { setAccountId(e.target.value); setOpportunityId('') }}><option value="">Select customer</option>{accounts.filter((x) => x.status !== 'Archived').map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
            <label>Opportunity<select value={opportunityId} onChange={(e) => setOpportunityId(e.target.value)}><option value="">No linked opportunity</option>{eligibleOpportunities.map((x) => <option key={x.id} value={x.id}>{x.title}</option>)}</select></label>
            <label>Issue date<input type="date" value={issueDate} onChange={(e) => { setIssueDate(e.target.value); if (dueDate < e.target.value) setDueDate(addDays(e.target.value, 7)) }} /></label>
            <label>Due date<input type="date" min={issueDate} value={dueDate} onChange={(e) => setDueDate(e.target.value)} /></label>
            <label>Discount %<input type="number" min="0" max="100" step="0.01" value={discountPercent} onChange={(e) => setDiscountPercent(e.target.value)} /></label>
          </div>
          <label>Subject<input value={subject} onChange={(e) => setSubject(e.target.value)} placeholder="Invoice subject" /></label>
          <div className="crm2-sales-edit-lines">
            <div className="crm2-sales-edit-head"><strong>Line items</strong><button onClick={() => setLines((items) => [...items, emptyLine()])}>+ Add line</button></div>
            {lines.map((line, index) => <div className="crm2-sales-edit-line crm2-sales-edit-line-with-item" key={line.id || index}>
              <select value={line.itemId || ''} onChange={(e) => selectItem(index, e.target.value)}>
                <option value="">Custom line</option>
                {salesItems.filter((x) => x.status === 'Active').map((x) => <option key={x.id} value={x.id}>{x.code} — {x.name}</option>)}
              </select>
              <input value={line.description} onChange={(e) => updateLine(index, { description: e.target.value })} placeholder="Product / service" />
              <input type="number" min="0.01" step="0.01" value={line.quantity} onChange={(e) => updateLine(index, { quantity: e.target.value })} />
              <input type="number" min="0" step="0.01" value={line.unitPrice} onChange={(e) => updateLine(index, { unitPrice: e.target.value })} />
              <input type="number" min="0" max="100" step="0.01" value={line.taxPercent} onChange={(e) => updateLine(index, { taxPercent: e.target.value })} />
              <strong>{money(lineSubtotal(line))}</strong><button disabled={lines.length === 1} onClick={() => setLines((items) => items.filter((_, i) => i !== index))}>×</button>
            </div>)}
          </div>
          <div className="crm2-sales-total-box"><span>Subtotal <b>{money(draftTotals.subtotal)}</b></span><span>Discount <b>{money(draftTotals.discount)}</b></span><span>GST / Tax <b>{money(draftTotals.tax)}</b></span><strong>Total <b>{money(draftTotals.total)}</b></strong></div>
          <label>Notes<textarea rows={2} value={notes} onChange={(e) => setNotes(e.target.value)} /></label>
          <label>Terms<textarea rows={2} value={terms} onChange={(e) => setTerms(e.target.value)} /></label>
          <div className="crm2-drawer-actions"><button onClick={() => setEditing(false)}>Cancel</button><button className="crm2-primary" onClick={() => void saveInvoice()} disabled={busy}>Save Draft</button></div>
        </section>
      </div> : null}

      {converting ? <div className="crm2-overlay" onMouseDown={() => setConverting(false)}>
        <section className="crm2-drawer" onMouseDown={(e) => e.stopPropagation()}>
          <div className="crm2-drawer-head"><div><span className="crm2-kicker">CONVERT</span><h2>Create Invoice</h2><p>Use an accepted proposal or estimate.</p></div><button onClick={() => setConverting(false)}>×</button></div>
          <label>Accepted document<select value={convertDocumentId} onChange={(e) => setConvertDocumentId(e.target.value)}><option value="">Select document</option>{availableDocuments.map((x) => <option key={x.id} value={x.id}>{x.documentNumber} — {x.subject} — {money(x.total)}</option>)}</select></label>
          <label>Invoice due date<input type="date" min={today()} value={convertDueDate} onChange={(e) => setConvertDueDate(e.target.value)} /></label>
          {availableDocuments.length === 0 ? <p className="crm2-reference-empty">No accepted unbilled proposal/estimate is available.</p> : null}
          <div className="crm2-drawer-actions"><button onClick={() => setConverting(false)}>Cancel</button><button className="crm2-primary" disabled={!convertDocumentId || busy} onClick={() => void convertDocument()}>Create Invoice</button></div>
        </section>
      </div> : null}

      {paymentOpen && selected ? <div className="crm2-overlay" onMouseDown={() => setPaymentOpen(false)}>
        <section className="crm2-drawer" onMouseDown={(e) => e.stopPropagation()}>
          <div className="crm2-drawer-head"><div><span className="crm2-kicker">PAYMENT</span><h2>Record Receipt</h2><p>{selected.invoiceNumber} · Balance {money(selected.balance)}</p></div><button onClick={() => setPaymentOpen(false)}>×</button></div>
          <label>Amount<input type="number" min="0.01" max={selected.balance} step="0.01" value={paymentAmount} onChange={(e) => setPaymentAmount(e.target.value)} /></label>
          <label>Method<select value={paymentMethod} onChange={(e) => setPaymentMethod(e.target.value)}>{methods.map((x) => <option key={x}>{x}</option>)}</select></label>
          <label>Reference<input value={paymentReference} onChange={(e) => setPaymentReference(e.target.value)} placeholder="UPI / cheque / bank reference" /></label>
          <label>Notes<textarea rows={2} value={paymentNotes} onChange={(e) => setPaymentNotes(e.target.value)} /></label>
          <div className="crm2-drawer-actions"><button onClick={() => setPaymentOpen(false)}>Cancel</button><button className="crm2-primary" disabled={busy || Number(paymentAmount) <= 0} onClick={() => void savePayment()}>Record Payment</button></div>
        </section>
      </div> : null}
    </section>
  )
}
