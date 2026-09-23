import { useMemo, useState } from 'react'
import {
  changeCrmInvoiceStatus,
  convertCrmSalesDocumentToInvoice,
  createCrmInvoice,
  getCrmInvoice,
  recordCrmInvoicePayment,
  updateCrmInvoice,
  type CrmAccount,
  type CrmInvoice,
  type CrmInvoicePayment,
  type CrmOpportunity,
  type CrmSalesDocument,
  type CrmSalesItem,
} from './crmApi'

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
  accounts, opportunities, documents, invoices, salesItems, busy, refresh, notify, canManageSales,
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

  const selected = useMemo(() => invoices.find((invoice) => invoice.id === selectedId) ?? null, [invoices, selectedId])
  const filtered = useMemo(() => {
    const search = query.trim().toLowerCase()
    return invoices.filter((invoice) => {
      const account = accounts.find((item) => item.id === invoice.accountId)
      const matchesStatus = statusFilter === 'All' || invoice.status === statusFilter
      const haystack = [invoice.invoiceNumber, invoice.subject, invoice.status, account?.name]
        .filter(Boolean).join(' ').toLowerCase()
      return matchesStatus && (!search || haystack.includes(search))
    })
  }, [accounts, invoices, query, statusFilter])

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

  const actions = selected ? (
    selected.status === 'Draft' ? ['Sent', 'Void'] :
    selected.status === 'Sent' ? ['Draft', 'Overdue', 'Void'] :
    selected.status === 'Overdue' && selected.amountPaid === 0 ? ['Void'] : []
  ) as CrmInvoice['status'][] : []

  return (
    <section className="crm2-ref-list-page crm2-invoices">
      <div className="crm2-ref-action-row">
        {canManageSales ? <button className="crm2-ref-primary" onClick={resetDraft}>+ New Invoice</button> : null}
        <button>Batch Payments</button>
        <button>Recurring Invoices</button>
        {canManageSales ? <button onClick={() => { setConvertDocumentId(availableDocuments[0]?.id || ''); setConvertDueDate(addDays(today(), 7)); setConverting(true) }}>Convert Accepted Document</button> : null}
        <button className="crm2-ref-square" title="Filter">▼</button>
      </div>
      <section className="crm2-ref-table-card">
        <div className="crm2-ref-table-tools">
          <select><option>25</option><option>50</option></select>
          <button>Export</button><button onClick={() => void refresh()} disabled={busy}>↻</button>
          <select value={statusFilter} onChange={(e) => setStatusFilter(e.target.value as typeof statusFilter)}>{statuses.map((status) => <option key={status}>{status}</option>)}</select>
          <span />
          <label><b>⌕</b><input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Search..." /></label>
        </div>
        <div className="crm2-invoice-head">
          <span>Invoice</span><span>Customer</span><span>Subject</span><span>Total</span><span>Paid</span><span>Balance</span><span>Status</span><span>Due</span>
        </div>
        {filtered.length === 0 ? <p className="crm2-reference-empty">No invoices found</p> : filtered.map((invoice) => {
          const account = accounts.find((item) => item.id === invoice.accountId)
          return <button className="crm2-invoice-row" key={invoice.id} onClick={() => void openDetail(invoice.id)}>
            <span><strong>{invoice.invoiceNumber}</strong></span>
            <span>{account?.name || 'Unknown customer'}</span>
            <span>{invoice.subject}</span><span>{money(invoice.total)}</span><span>{money(invoice.amountPaid)}</span>
            <span>{money(invoice.balance)}</span><span><em className={`crm2-sales-status ${invoice.status.toLowerCase()}`}>{invoice.status}</em></span>
            <span>{invoice.dueDate}</span>
          </button>
        })}
      </section>

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
