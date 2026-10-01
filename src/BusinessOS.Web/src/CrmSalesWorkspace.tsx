import { useEffect, useMemo, useState } from 'react'
import {
  type CrmAccount,
  type CrmCreditNote,
  listCrmInvoicePayments,
  type CrmInvoice,
  type CrmInvoicePayment,
  type CrmOpportunity,
  type CrmSalesDocument,
  type CrmSalesItem,
  type CrmSalesItemGroup,
} from './crmApi'
import { CrmCreditNotesView } from './CrmCreditNotesView'
import { CrmInvoicesView } from './CrmInvoicesView'
import { CrmSalesDocumentsView } from './CrmSalesDocumentsView'
import { CrmSalesItemsView } from './CrmSalesItemsView'
import { exportCrmSpreadsheet } from './crmSpreadsheet'

type SalesSection = 'proposals' | 'estimates' | 'invoices' | 'payments' | 'credits' | 'items'

type Props = {
  section: SalesSection
  accounts: CrmAccount[]
  opportunities: CrmOpportunity[]
  documents: CrmSalesDocument[]
  invoices: CrmInvoice[]
  salesItems: CrmSalesItem[]
  salesItemGroups: CrmSalesItemGroup[]
  creditNotes: CrmCreditNote[]
  busy: boolean
  refresh: () => Promise<void>
  notify: (message: string) => void
  canManageSales: boolean
}

function money(value: number) {
  return new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR', maximumFractionDigits: 2 }).format(value || 0)
}

function CrmPaymentsReferenceView({ accounts, invoices, busy, refresh, notify }: Pick<Props, 'accounts' | 'invoices' | 'busy' | 'refresh' | 'notify'>) {
  const [query, setQuery] = useState('')
  const [pageSize, setPageSize] = useState(25)
  const [payments, setPayments] = useState<CrmInvoicePayment[]>([])
  const [paymentBusy, setPaymentBusy] = useState(false)

  async function loadPayments() {
    setPaymentBusy(true)
    try {
      const eligible = invoices.filter(invoice => invoice.amountPaid > 0)
      const batches = await Promise.all(eligible.map(async invoice => {
        const result = await listCrmInvoicePayments(invoice.id)
        return result.payments
      }))
      setPayments(batches.flat().sort((a, b) => b.receivedAtUtc.localeCompare(a.receivedAtUtc)))
    } catch (error) {
      notify(error instanceof Error ? error.message : String(error))
    } finally {
      setPaymentBusy(false)
    }
  }

  useEffect(() => { void loadPayments() }, [invoices])

  const rows = useMemo(() => {
    const needle = query.trim().toLowerCase()
    return payments.filter(payment => {
      const invoice = invoices.find(x => x.id === payment.invoiceId)
      const account = accounts.find(x => x.id === invoice?.accountId)
      const haystack = [payment.paymentNumber, invoice?.invoiceNumber, payment.method, payment.reference, account?.name]
        .filter(Boolean).join(' ').toLowerCase()
      return !needle || haystack.includes(needle)
    })
  }, [accounts, invoices, payments, query])
  const visibleRows = rows.slice(0, pageSize)

  async function reload() {
    await refresh()
    await loadPayments()
  }

  async function exportPayments() {
    await exportCrmSpreadsheet('crm-payments', {
      headers: ['Payment #', 'Invoice #', 'Payment Mode', 'Transaction ID', 'Customer', 'Amount', 'Received At', 'Notes'],
      rows: rows.map(payment => {
        const invoice = invoices.find(x => x.id === payment.invoiceId)
        const account = accounts.find(x => x.id === invoice?.accountId)
        return [payment.paymentNumber, invoice?.invoiceNumber || '', payment.method, payment.reference || '', account?.name || '', payment.amount, payment.receivedAtUtc, payment.notes || '']
      }),
    }, 'xlsx')
    notify(`Exported ${rows.length} payment(s)`)
  }

  return <section className="crm2-ref-list-page crm2-reference-payments">
    <section className="crm2-ref-table-card">
      <div className="crm2-ref-table-tools">
        <select aria-label="Rows" value={pageSize} onChange={(e) => setPageSize(Number(e.target.value))}><option value={25}>25</option><option value={50}>50</option><option value={100}>100</option></select>
        <button type="button" onClick={() => void exportPayments()} disabled={busy || paymentBusy || rows.length === 0}>Export</button><button type="button" onClick={() => void reload()} disabled={busy || paymentBusy}>↻</button><span />
        <label><b>⌕</b><input value={query} onChange={e => setQuery(e.target.value)} placeholder="Search..." /></label>
      </div>
      <div className="crm2-payments-head"><span>Payment #</span><span>Invoice #</span><span>Payment Mode</span><span>Transaction ID</span><span>Customer</span><span>Amount</span><span>Date</span></div>
      {paymentBusy && payments.length === 0 ? <p className="crm2-reference-empty">Loading payments...</p> : rows.length === 0 ? <p className="crm2-reference-empty">No entries found</p> : visibleRows.map(payment => {
        const invoice = invoices.find(x => x.id === payment.invoiceId)
        const account = accounts.find(x => x.id === invoice?.accountId)
        return <div className="crm2-payments-row" key={payment.id}>
          <span>{payment.paymentNumber}</span><span><strong>{invoice?.invoiceNumber || '—'}</strong></span><span>{payment.method}</span><span>{payment.reference || '—'}</span>
          <span><strong>{account?.name || 'Unknown customer'}</strong></span><span>{money(payment.amount)}</span><span>{new Date(payment.receivedAtUtc).toLocaleDateString('en-IN')}</span>
        </div>
      })}
    </section>
  </section>
}

export function CrmSalesWorkspace(props: Props) {
  if (props.section === 'proposals' || props.section === 'estimates') {
    return <CrmSalesDocumentsView
      key={props.section}
      initialKind={props.section === 'proposals' ? 'Proposal' : 'Estimate'}
      accounts={props.accounts} opportunities={props.opportunities}
      documents={props.documents} salesItems={props.salesItems}
      busy={props.busy} refresh={props.refresh} notify={props.notify}
      canManageSales={props.canManageSales}
    />
  }

  if (props.section === 'invoices') {
    return <CrmInvoicesView
      accounts={props.accounts} opportunities={props.opportunities}
      documents={props.documents} invoices={props.invoices} salesItems={props.salesItems}
      busy={props.busy} refresh={props.refresh} notify={props.notify}
      canManageSales={props.canManageSales}
    />
  }

  if (props.section === 'payments') return <CrmPaymentsReferenceView accounts={props.accounts} invoices={props.invoices} busy={props.busy} refresh={props.refresh} notify={props.notify} />

  if (props.section === 'credits') {
    return <CrmCreditNotesView
      accounts={props.accounts} invoices={props.invoices} creditNotes={props.creditNotes}
      busy={props.busy} refresh={props.refresh} notify={props.notify}
      canManageSales={props.canManageSales}
    />
  }

  return <CrmSalesItemsView
    items={props.salesItems} groups={props.salesItemGroups} busy={props.busy} refresh={props.refresh}
    notify={props.notify} canManageSales={props.canManageSales}
  />
}
