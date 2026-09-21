import { useMemo, useState } from 'react'
import {
  changeCrmSalesDocumentStatus,
  createCrmSalesDocument,
  updateCrmSalesDocument,
  type CrmAccount,
  type CrmOpportunity,
  type CrmSalesDocument,
  type CrmSalesItem,
} from './crmApi'
import { exportCrmSpreadsheet, type CrmSpreadsheetFormat } from './crmSpreadsheet'

type Props = {
  accounts: CrmAccount[]
  opportunities: CrmOpportunity[]
  documents: CrmSalesDocument[]
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

const statuses = ['All', 'Draft', 'Sent', 'Accepted', 'Rejected', 'Expired'] as const
const emptyLine = (): DraftLine => ({ itemId: '', description: '', quantity: '1', unitPrice: '0', taxPercent: '18' })
const today = () => new Date().toISOString().slice(0, 10)

function money(value: number, currency = 'INR') {
  try { return new Intl.NumberFormat('en-IN', { style: 'currency', currency, maximumFractionDigits: 2 }).format(value) }
  catch { return `${currency} ${value.toLocaleString('en-IN')}` }
}

function lineSubtotal(line: DraftLine) {
  return Math.max(0, Number(line.quantity) || 0) * Math.max(0, Number(line.unitPrice) || 0)
}

export function CrmSalesDocumentsView({
  accounts, opportunities, documents, salesItems, busy, refresh, notify, canManageSales,
}: Props) {
  const [kindFilter, setKindFilter] = useState<'All' | 'Proposal' | 'Estimate'>('All')
  const [statusFilter, setStatusFilter] = useState<(typeof statuses)[number]>('All')
  const [query, setQuery] = useState('')
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [editing, setEditing] = useState(false)
  const [draftKind, setDraftKind] = useState<'Proposal' | 'Estimate'>('Proposal')
  const [accountId, setAccountId] = useState('')
  const [opportunityId, setOpportunityId] = useState('')
  const [subject, setSubject] = useState('')
  const [issueDate, setIssueDate] = useState(today())
  const [expiryDate, setExpiryDate] = useState('')
  const [discountPercent, setDiscountPercent] = useState('0')
  const [notes, setNotes] = useState('')
  const [terms, setTerms] = useState('')
  const [lines, setLines] = useState<DraftLine[]>([emptyLine()])

  const selected = useMemo(
    () => documents.find((document) => document.id === selectedId) ?? null,
    [documents, selectedId],
  )
  const filtered = useMemo(() => {
    const search = query.trim().toLowerCase()
    return documents.filter((document) => {
      const account = accounts.find((item) => item.id === document.accountId)
      const matchesKind = kindFilter === 'All' || document.kind === kindFilter
      const matchesStatus = statusFilter === 'All' || document.status === statusFilter
      const haystack = [
        document.documentNumber, document.subject, document.status, document.kind,
        account?.name, account?.primaryContact?.name, account?.primaryContact?.phone,
      ].filter(Boolean).join(' ').toLowerCase()
      return matchesKind && matchesStatus && (!search || haystack.includes(search))
    })
  }, [accounts, documents, kindFilter, query, statusFilter])

  const eligibleOpportunities = useMemo(
    () => opportunities.filter((opportunity) => !accountId || opportunity.accountId === accountId),
    [accountId, opportunities],
  )

  const draftTotals = useMemo(() => {
    const subtotal = lines.reduce((sum, line) => sum + lineSubtotal(line), 0)
    const discount = subtotal * Math.min(100, Math.max(0, Number(discountPercent) || 0)) / 100
    const factor = 1 - Math.min(100, Math.max(0, Number(discountPercent) || 0)) / 100
    const tax = lines.reduce((sum, line) =>
      sum + lineSubtotal(line) * factor * Math.min(100, Math.max(0, Number(line.taxPercent) || 0)) / 100, 0)
    return { subtotal, discount, tax, total: subtotal - discount + tax }
  }, [discountPercent, lines])

  function resetDraft(kind: 'Proposal' | 'Estimate') {
    setDraftKind(kind)
    setAccountId(accounts.find((item) => item.status === 'Active')?.id || accounts[0]?.id || '')
    setOpportunityId('')
    setSubject('')
    setIssueDate(today())
    setExpiryDate('')
    setDiscountPercent('0')
    setNotes('')
    setTerms('')
    setLines([emptyLine()])
    setSelectedId(null)
    setEditing(true)
  }

  function editDocument(document: CrmSalesDocument) {
    if (document.status !== 'Draft') {
      notify('Only draft proposals and estimates can be edited')
      return
    }
    setSelectedId(document.id)
    setDraftKind(document.kind)
    setAccountId(document.accountId)
    setOpportunityId(document.opportunityId || '')
    setSubject(document.subject)
    setIssueDate(document.issueDate)
    setExpiryDate(document.expiryDate || '')
    setDiscountPercent(String(document.discountPercent))
    setNotes(document.notes || '')
    setTerms(document.terms || '')
    setLines(document.lines.map((line) => ({
      id: line.id,
      itemId: line.itemId || '',
      description: line.description,
      quantity: String(line.quantity),
      unitPrice: String(line.unitPrice),
      taxPercent: String(line.taxPercent),
    })))
    setEditing(true)
  }

  function updateLine(index: number, patch: Partial<DraftLine>) {
    setLines((items) => items.map((line, lineIndex) => lineIndex === index ? { ...line, ...patch } : line))
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

  async function saveDraft() {
    if (!accountId) { notify('Select a customer'); return }
    if (!subject.trim()) { notify('Enter proposal / estimate subject'); return }
    const cleanLines = lines.filter((line) => line.description.trim())
    if (!cleanLines.length) { notify('Add at least one line item'); return }
    const payload = {
      accountId,
      opportunityId: opportunityId || null,
      subject: subject.trim(),
      currencyCode: 'INR',
      issueDate,
      expiryDate: expiryDate || null,
      discountPercent: Math.max(0, Number(discountPercent) || 0),
      notes: notes.trim() || undefined,
      terms: terms.trim() || undefined,
      lines: cleanLines.map((line) => ({
        id: line.id,
        itemId: line.itemId || null,
        description: line.description.trim(),
        quantity: Math.max(0, Number(line.quantity) || 0),
        unitPrice: Math.max(0, Number(line.unitPrice) || 0),
        taxPercent: Math.max(0, Number(line.taxPercent) || 0),
      })),
    }
    try {
      if (selected?.status === 'Draft') {
        await updateCrmSalesDocument(selected.id, payload)
        notify(`${draftKind} updated`)
      } else {
        await createCrmSalesDocument({ ...payload, kind: draftKind })
        notify(`${draftKind} created`)
      }
      setEditing(false)
      setSelectedId(null)
      await refresh()
    } catch (error) {
      notify(error instanceof Error ? error.message : String(error))
    }
  }

  async function moveStatus(document: CrmSalesDocument, status: CrmSalesDocument['status']) {
    try {
      const updated = await changeCrmSalesDocumentStatus(document.id, status)
      notify(`${updated.documentNumber} moved to ${updated.status}`)
      await refresh()
      setSelectedId(updated.id)
    } catch (error) {
      notify(error instanceof Error ? error.message : String(error))
    }
  }

  async function exportDocuments(format: CrmSpreadsheetFormat) {
    await exportCrmSpreadsheet('crm-sales-documents', {
      headers: ['Type', 'Document', 'Customer', 'Subject', 'Status', 'Issue Date', 'Expiry Date', 'Subtotal', 'Discount', 'Tax', 'Total'],
      rows: filtered.map((document) => {
        const account = accounts.find((item) => item.id === document.accountId)
        return [
          document.kind, document.documentNumber, account?.name || '', document.subject, document.status,
          document.issueDate, document.expiryDate || '', document.subtotal, document.discountAmount,
          document.taxAmount, document.total,
        ]
      }),
    }, format)
    notify(`Exported ${filtered.length} sales document(s)`)
  }

  const statusActions = selected ? (
    selected.status === 'Draft' ? ['Sent', 'Expired'] :
    selected.status === 'Sent' ? ['Accepted', 'Rejected', 'Expired', 'Draft'] :
    selected.status === 'Rejected' ? ['Draft'] : []
  ) as CrmSalesDocument['status'][] : []

  return (
    <section className="crm2-ref-list-page crm2-sales-documents">
      <div className="crm2-ref-action-row">
        {canManageSales ? <button className="crm2-ref-primary" onClick={() => resetDraft('Proposal')}>+ New Proposal</button> : null}
        {canManageSales ? <button className="crm2-ref-primary" onClick={() => resetDraft('Estimate')}>+ New Estimate</button> : null}
        <button onClick={() => void exportDocuments('csv')}>Export CSV</button>
        <button onClick={() => void exportDocuments('xlsx')}>Export Excel</button>
      </div>

      <section className="crm2-ref-filter-card">
        <strong>Sales documents</strong>
        <div className="crm2-ref-filter-grid">
          <select value={kindFilter} onChange={(e) => setKindFilter(e.target.value as typeof kindFilter)}>
            <option>All</option><option>Proposal</option><option>Estimate</option>
          </select>
          <select value={statusFilter} onChange={(e) => setStatusFilter(e.target.value as typeof statusFilter)}>
            {statuses.map((status) => <option key={status}>{status}</option>)}
          </select>
          <input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Search document, customer, subject..." />
          <button onClick={() => void refresh()} disabled={busy}>Refresh</button>
        </div>
      </section>

      <section className="crm2-ref-table-card">
        <div className="crm2-sales-head">
          <span>Document</span><span>Customer</span><span>Subject</span><span>Amount</span><span>Status</span><span>Issue Date</span>
        </div>
        {filtered.length === 0 ? <p className="crm2-reference-empty">No proposals or estimates found</p> : filtered.map((document) => {
          const account = accounts.find((item) => item.id === document.accountId)
          return <button className="crm2-sales-row" key={document.id} onClick={() => setSelectedId(document.id)}>
            <span><strong>{document.documentNumber}</strong><small>{document.kind}</small></span>
            <span>{account?.name || 'Unknown customer'}</span>
            <span>{document.subject}</span>
            <span>{money(document.total, document.currencyCode)}</span>
            <span><em className={`crm2-sales-status ${document.status.toLowerCase()}`}>{document.status}</em></span>
            <span>{document.issueDate}</span>
          </button>
        })}
      </section>

      {selected && !editing ? <div className="crm2-overlay" onMouseDown={() => setSelectedId(null)}>
        <section className="crm2-drawer crm2-wide-drawer" onMouseDown={(e) => e.stopPropagation()}>
          <div className="crm2-drawer-head">
            <div><span className="crm2-kicker">{selected.kind.toUpperCase()}</span><h2>{selected.documentNumber}</h2><p>{selected.subject}</p></div>
            <button onClick={() => setSelectedId(null)}>×</button>
          </div>
          <div className="crm2-sales-detail-grid">
            <span><small>Customer</small><strong>{accounts.find((item) => item.id === selected.accountId)?.name || '-'}</strong></span>
            <span><small>Status</small><strong>{selected.status}</strong></span>
            <span><small>Issue date</small><strong>{selected.issueDate}</strong></span>
            <span><small>Valid until</small><strong>{selected.expiryDate || '-'}</strong></span>
          </div>
          <div className="crm2-sales-lines">
            <div><b>Description</b><b>Qty</b><b>Rate</b><b>Tax</b><b>Amount</b></div>
            {selected.lines.map((line) => <div key={line.id}>
              <span>{line.description}</span><span>{line.quantity}</span><span>{money(line.unitPrice)}</span>
              <span>{line.taxPercent}%</span><span>{money(line.subtotal)}</span>
            </div>)}
          </div>
          <div className="crm2-sales-total-box">
            <span>Subtotal <b>{money(selected.subtotal)}</b></span>
            <span>Discount <b>{money(selected.discountAmount)}</b></span>
            <span>GST / Tax <b>{money(selected.taxAmount)}</b></span>
            <strong>Total <b>{money(selected.total)}</b></strong>
          </div>
          {selected.notes ? <p><strong>Notes:</strong> {selected.notes}</p> : null}
          {selected.terms ? <p><strong>Terms:</strong> {selected.terms}</p> : null}
          <div className="crm2-drawer-actions">
            {canManageSales && selected.status === 'Draft' ? <button onClick={() => editDocument(selected)}>Edit</button> : null}
            {canManageSales ? statusActions.map((status) => <button key={status} disabled={busy} onClick={() => void moveStatus(selected, status)}>{status === 'Sent' ? 'Mark Sent' : status}</button>) : null}
          </div>
        </section>
      </div> : null}

      {editing ? <div className="crm2-overlay" onMouseDown={() => setEditing(false)}>
        <section className="crm2-drawer crm2-wide-drawer" onMouseDown={(e) => e.stopPropagation()}>
          <div className="crm2-drawer-head">
            <div><span className="crm2-kicker">{selected ? 'EDIT' : 'NEW'} {draftKind.toUpperCase()}</span><h2>{selected?.documentNumber || `Create ${draftKind}`}</h2><p>Add customer, items, tax and validity.</p></div>
            <button onClick={() => setEditing(false)}>×</button>
          </div>
          <div className="crm2-form-grid">
            <label>Customer<select value={accountId} onChange={(e) => { setAccountId(e.target.value); setOpportunityId('') }}>
              <option value="">Select customer</option>{accounts.filter((item) => item.status !== 'Archived').map((item) => <option key={item.id} value={item.id}>{item.name}</option>)}
            </select></label>
            <label>Opportunity (optional)<select value={opportunityId} onChange={(e) => setOpportunityId(e.target.value)}>
              <option value="">No linked opportunity</option>{eligibleOpportunities.map((item) => <option key={item.id} value={item.id}>{item.title}</option>)}
            </select></label>
            <label>Issue date<input type="date" value={issueDate} onChange={(e) => setIssueDate(e.target.value)} /></label>
            <label>Valid until<input type="date" min={issueDate} value={expiryDate} onChange={(e) => setExpiryDate(e.target.value)} /></label>
            <label>Discount %<input type="number" min="0" max="100" step="0.01" value={discountPercent} onChange={(e) => setDiscountPercent(e.target.value)} /></label>
          </div>
          <label>Subject<input value={subject} onChange={(e) => setSubject(e.target.value)} placeholder="e.g. AI Repair Software proposal" /></label>
          <div className="crm2-sales-edit-lines">
            <div className="crm2-sales-edit-head"><strong>Line items</strong><button onClick={() => setLines((items) => [...items, emptyLine()])}>+ Add line</button></div>
            {lines.map((line, index) => <div className="crm2-sales-edit-line crm2-sales-edit-line-with-item" key={line.id || index}>
              <select value={line.itemId || ''} onChange={(e) => selectItem(index, e.target.value)}>
                <option value="">Custom line</option>
                {salesItems.filter((x) => x.status === 'Active').map((x) => <option key={x.id} value={x.id}>{x.code} — {x.name}</option>)}
              </select>
              <input value={line.description} onChange={(e) => updateLine(index, { description: e.target.value })} placeholder="Product / service" />
              <input type="number" min="0.01" step="0.01" value={line.quantity} onChange={(e) => updateLine(index, { quantity: e.target.value })} placeholder="Qty" />
              <input type="number" min="0" step="0.01" value={line.unitPrice} onChange={(e) => updateLine(index, { unitPrice: e.target.value })} placeholder="Rate" />
              <input type="number" min="0" max="100" step="0.01" value={line.taxPercent} onChange={(e) => updateLine(index, { taxPercent: e.target.value })} placeholder="Tax %" />
              <strong>{money(lineSubtotal(line))}</strong>
              <button disabled={lines.length === 1} onClick={() => setLines((items) => items.filter((_, i) => i !== index))}>×</button>
            </div>)}
          </div>
          <div className="crm2-sales-total-box">
            <span>Subtotal <b>{money(draftTotals.subtotal)}</b></span>
            <span>Discount <b>{money(draftTotals.discount)}</b></span>
            <span>GST / Tax <b>{money(draftTotals.tax)}</b></span>
            <strong>Total <b>{money(draftTotals.total)}</b></strong>
          </div>
          <label>Notes<textarea rows={3} value={notes} onChange={(e) => setNotes(e.target.value)} placeholder="Internal/customer note" /></label>
          <label>Terms<textarea rows={3} value={terms} onChange={(e) => setTerms(e.target.value)} placeholder="Payment / validity terms" /></label>
          <div className="crm2-drawer-actions"><button onClick={() => setEditing(false)}>Cancel</button><button className="crm2-primary" disabled={busy} onClick={() => void saveDraft()}>{busy ? 'Saving...' : 'Save Draft'}</button></div>
        </section>
      </div> : null}
    </section>
  )
}
