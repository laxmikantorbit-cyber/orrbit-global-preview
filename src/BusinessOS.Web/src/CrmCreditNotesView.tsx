import { useMemo, useState } from 'react'
import {
  createCrmCreditNote,
  issueCrmCreditNote,
  updateCrmCreditNote,
  voidCrmCreditNote,
  type CrmAccount,
  type CrmCreditNote,
  type CrmInvoice,
} from './crmApi'

type Props = {
  accounts: CrmAccount[]
  invoices: CrmInvoice[]
  creditNotes: CrmCreditNote[]
  busy: boolean
  refresh: () => Promise<void>
  notify: (message: string) => void
  canManageSales: boolean
}

const today = () => new Date().toISOString().slice(0, 10)
function money(value: number) {
  return new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR', maximumFractionDigits: 2 }).format(value)
}

export function CrmCreditNotesView({
  accounts, invoices, creditNotes, busy, refresh, notify, canManageSales,
}: Props) {
  const [status, setStatus] = useState<'All' | 'Draft' | 'Issued' | 'Void'>('All')
  const [query, setQuery] = useState('')
  const [selectedId, setSelectedId] = useState<string | null>(null)
  const [open, setOpen] = useState(false)
  const [editingId, setEditingId] = useState<string | null>(null)
  const [invoiceId, setInvoiceId] = useState('')
  const [issueDate, setIssueDate] = useState(today())
  const [amount, setAmount] = useState('0')
  const [reason, setReason] = useState('')
  const [notes, setNotes] = useState('')

  const selected = creditNotes.find((x) => x.id === selectedId) ?? null
  const eligibleInvoices = useMemo(
    () => invoices.filter((x) => !['Draft', 'Void'].includes(x.status)),
    [invoices],
  )
  const filtered = useMemo(() => {
    const search = query.trim().toLowerCase()
    return creditNotes.filter((note) => {
      const invoice = invoices.find((x) => x.id === note.invoiceId)
      const account = accounts.find((x) => x.id === note.accountId)
      const haystack = [note.creditNoteNumber, note.reason, invoice?.invoiceNumber, account?.name]
        .filter(Boolean).join(' ').toLowerCase()
      return (status === 'All' || note.status === status) && (!search || haystack.includes(search))
    })
  }, [accounts, creditNotes, invoices, query, status])

  function beginNew() {
    const invoice = eligibleInvoices.find((x) => x.balance > 0) || eligibleInvoices[0]
    setEditingId(null); setInvoiceId(invoice?.id || ''); setIssueDate(today())
    setAmount(invoice ? String(Math.max(0, invoice.balance)) : '0')
    setReason(''); setNotes(''); setSelectedId(null); setOpen(true)
  }

  function beginEdit(note: CrmCreditNote) {
    if (note.status !== 'Draft') { notify('Only draft credit notes can be edited'); return }
    setEditingId(note.id); setInvoiceId(note.invoiceId); setIssueDate(note.issueDate)
    setAmount(String(note.amount)); setReason(note.reason); setNotes(note.notes || '')
    setOpen(true)
  }

  async function save() {
    const value = Number(amount) || 0
    if (!invoiceId || value <= 0 || !reason.trim()) {
      notify('Select invoice, enter amount and reason'); return
    }
    try {
      if (editingId) {
        await updateCrmCreditNote(editingId, {
          issueDate, amount: value, reason: reason.trim(), notes: notes.trim() || undefined,
        })
        notify('Credit note updated')
      } else {
        await createCrmCreditNote({
          invoiceId, issueDate, amount: value, reason: reason.trim(), notes: notes.trim() || undefined,
        })
        notify('Credit note created')
      }
      setOpen(false); await refresh()
    } catch (error) {
      notify(error instanceof Error ? error.message : String(error))
    }
  }

  async function issue(note: CrmCreditNote) {
    try {
      const result = await issueCrmCreditNote(note.id, today())
      notify(`${result.creditNote.creditNoteNumber} issued; invoice balance ${money(result.invoice.balance)}`)
      await refresh(); setSelectedId(result.creditNote.id)
    } catch (error) { notify(error instanceof Error ? error.message : String(error)) }
  }

  async function voidNote(note: CrmCreditNote) {
    try {
      const result = await voidCrmCreditNote(note.id, today())
      notify(`${result.creditNote.creditNoteNumber} voided; invoice balance ${money(result.invoice.balance)}`)
      await refresh(); setSelectedId(result.creditNote.id)
    } catch (error) { notify(error instanceof Error ? error.message : String(error)) }
  }

  return <section className="crm2-ref-list-page">
    <div className="crm2-ref-action-row">
      {canManageSales ? <button className="crm2-ref-primary" onClick={beginNew}>+ New Credit Note</button> : null}
    </div>
    <section className="crm2-ref-filter-card">
      <strong>Credit notes</strong>
      <div className="crm2-ref-filter-grid">
        <select value={status} onChange={(e) => setStatus(e.target.value as typeof status)}>
          <option>All</option><option>Draft</option><option>Issued</option><option>Void</option>
        </select>
        <input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Search credit note, invoice, customer..." />
        <button onClick={() => void refresh()} disabled={busy}>Refresh</button>
      </div>
    </section>
    <section className="crm2-ref-table-card">
      <div className="crm2-credit-head">
        <span>Credit Note</span><span>Invoice</span><span>Customer</span><span>Reason</span><span>Amount</span><span>Status</span>
      </div>
      {filtered.length === 0 ? <p className="crm2-reference-empty">No credit notes found</p> :
        filtered.map((note) => {
          const invoice = invoices.find((x) => x.id === note.invoiceId)
          const account = accounts.find((x) => x.id === note.accountId)
          return <button className="crm2-credit-row" key={note.id} onClick={() => setSelectedId(note.id)}>
            <span><strong>{note.creditNoteNumber}</strong><small>{note.issueDate}</small></span>
            <span>{invoice?.invoiceNumber || '-'}</span><span>{account?.name || '-'}</span>
            <span>{note.reason}</span><span>{money(note.amount)}</span>
            <span><em className={`crm2-sales-status ${note.status.toLowerCase()}`}>{note.status}</em></span>
          </button>
        })}
    </section>

    {selected ? <div className="crm2-overlay" onMouseDown={() => setSelectedId(null)}>
      <section className="crm2-drawer" onMouseDown={(e) => e.stopPropagation()}>
        <div className="crm2-drawer-head">
          <div><span className="crm2-kicker">CREDIT NOTE</span><h2>{selected.creditNoteNumber}</h2><p>{selected.reason}</p></div>
          <button onClick={() => setSelectedId(null)}>×</button>
        </div>
        <p><strong>Invoice:</strong> {invoices.find((x) => x.id === selected.invoiceId)?.invoiceNumber || '-'}</p>
        <p><strong>Amount:</strong> {money(selected.amount)}</p>
        <p><strong>Status:</strong> {selected.status}</p>
        {selected.notes ? <p><strong>Notes:</strong> {selected.notes}</p> : null}
        <div className="crm2-drawer-actions">
          {canManageSales && selected.status === 'Draft' ? <button onClick={() => beginEdit(selected)}>Edit</button> : null}
          {canManageSales && selected.status === 'Draft' ? <button className="crm2-primary" onClick={() => void issue(selected)}>Issue</button> : null}
          {canManageSales && selected.status === 'Issued' ? <button onClick={() => void voidNote(selected)}>Void</button> : null}
        </div>
      </section>
    </div> : null}

    {open ? <div className="crm2-overlay" onMouseDown={() => setOpen(false)}>
      <section className="crm2-drawer" onMouseDown={(e) => e.stopPropagation()}>
        <div className="crm2-drawer-head">
          <div><span className="crm2-kicker">{editingId ? 'EDIT' : 'NEW'} CREDIT NOTE</span><h2>Invoice Adjustment</h2></div>
          <button onClick={() => setOpen(false)}>×</button>
        </div>
        <label>Invoice<select value={invoiceId} disabled={!!editingId} onChange={(e) => {
          setInvoiceId(e.target.value)
          const invoice = invoices.find((x) => x.id === e.target.value)
          if (invoice) setAmount(String(Math.max(0, invoice.balance)))
        }}>
          <option value="">Select invoice</option>
          {eligibleInvoices.map((x) => <option key={x.id} value={x.id}>{x.invoiceNumber} — Balance {money(x.balance)}</option>)}
        </select></label>
        <label>Issue date<input type="date" value={issueDate} onChange={(e) => setIssueDate(e.target.value)} /></label>
        <label>Amount<input type="number" min="0.01" step="0.01" value={amount} onChange={(e) => setAmount(e.target.value)} /></label>
        <label>Reason<input value={reason} onChange={(e) => setReason(e.target.value)} placeholder="e.g. Service adjustment / return" /></label>
        <label>Notes<textarea rows={3} value={notes} onChange={(e) => setNotes(e.target.value)} /></label>
        <div className="crm2-drawer-actions">
          <button onClick={() => setOpen(false)}>Cancel</button>
          <button className="crm2-primary" disabled={busy} onClick={() => void save()}>Save Draft</button>
        </div>
      </section>
    </div> : null}
  </section>
}
