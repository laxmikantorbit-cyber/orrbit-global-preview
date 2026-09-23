import { useMemo, useState } from 'react'
import {
  type CrmAccount,
  type CrmCreditNote,
  type CrmInvoice,
  type CrmOpportunity,
  type CrmSalesDocument,
  type CrmSalesItem,
} from './crmApi'
import { CrmCreditNotesView } from './CrmCreditNotesView'
import { CrmInvoicesView } from './CrmInvoicesView'
import { CrmSalesDocumentsView } from './CrmSalesDocumentsView'
import { CrmSalesItemsView } from './CrmSalesItemsView'

type SalesSection = 'proposals' | 'estimates' | 'invoices' | 'payments' | 'credits' | 'items'

type Props = {
  section: SalesSection
  accounts: CrmAccount[]
  opportunities: CrmOpportunity[]
  documents: CrmSalesDocument[]
  invoices: CrmInvoice[]
  salesItems: CrmSalesItem[]
  creditNotes: CrmCreditNote[]
  busy: boolean
  refresh: () => Promise<void>
  notify: (message: string) => void
  canManageSales: boolean
}

function money(value: number) {
  return new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR', maximumFractionDigits: 2 }).format(value || 0)
}

function CrmPaymentsReferenceView({ accounts, invoices }: Pick<Props, 'accounts' | 'invoices'>) {
  const [query, setQuery] = useState('')
  const rows = useMemo(() => {
    const needle = query.trim().toLowerCase()
    return invoices.filter(invoice => invoice.amountPaid > 0).filter(invoice => {
      const account = accounts.find(x => x.id === invoice.accountId)
      return !needle || [invoice.invoiceNumber, account?.name, invoice.status].filter(Boolean).join(' ').toLowerCase().includes(needle)
    })
  }, [accounts, invoices, query])

  return <section className="crm2-ref-list-page crm2-reference-payments">
    <section className="crm2-ref-table-card">
      <div className="crm2-ref-table-tools">
        <select aria-label="Rows"><option>25</option><option>50</option></select>
        <button type="button">Export</button><button type="button">↻</button><span />
        <label><b>⌕</b><input value={query} onChange={e => setQuery(e.target.value)} placeholder="Search..." /></label>
      </div>
      <div className="crm2-payments-head"><span>Payment #</span><span>Invoice #</span><span>Payment Mode</span><span>Transaction ID</span><span>Customer</span><span>Amount</span><span>Date</span></div>
      {rows.length === 0 ? <p className="crm2-reference-empty">No entries found</p> : rows.map((invoice, index) => {
        const account = accounts.find(x => x.id === invoice.accountId)
        return <div className="crm2-payments-row" key={invoice.id}>
          <span>{index + 1}</span><span><strong>{invoice.invoiceNumber}</strong></span><span>—</span><span>—</span>
          <span><strong>{account?.name || 'Unknown customer'}</strong></span><span>{money(invoice.amountPaid)}</span><span>{invoice.issueDate}</span>
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

  if (props.section === 'payments') return <CrmPaymentsReferenceView accounts={props.accounts} invoices={props.invoices} />

  if (props.section === 'credits') {
    return <CrmCreditNotesView
      accounts={props.accounts} invoices={props.invoices} creditNotes={props.creditNotes}
      busy={props.busy} refresh={props.refresh} notify={props.notify}
      canManageSales={props.canManageSales}
    />
  }

  return <CrmSalesItemsView
    items={props.salesItems} busy={props.busy} refresh={props.refresh}
    notify={props.notify} canManageSales={props.canManageSales}
  />
}
