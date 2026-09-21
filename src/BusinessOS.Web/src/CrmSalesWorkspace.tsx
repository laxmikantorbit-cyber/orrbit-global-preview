import { useState } from 'react'
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

type Props = {
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

export function CrmSalesWorkspace(props: Props) {
  const [tab, setTab] = useState<'documents' | 'invoices' | 'credits' | 'items'>('documents')
  const outstanding = props.invoices.filter((invoice) => invoice.balance > 0 && invoice.status !== 'Void').length

  return (
    <>
      <div className="crm2-sales-tabs">
        <button className={tab === 'documents' ? 'active' : ''} onClick={() => setTab('documents')}>
          Proposals & Estimates <b>{props.documents.length}</b>
        </button>
        <button className={tab === 'invoices' ? 'active' : ''} onClick={() => setTab('invoices')}>
          Invoices & Payments <b>{props.invoices.length}</b>
          {outstanding > 0 ? <em>{outstanding} due</em> : null}
        </button>
        <button className={tab === 'credits' ? 'active' : ''} onClick={() => setTab('credits')}>
          Credit Notes <b>{props.creditNotes.length}</b>
        </button>
        <button className={tab === 'items' ? 'active' : ''} onClick={() => setTab('items')}>
          Items <b>{props.salesItems.length}</b>
        </button>
      </div>

      {tab === 'documents' ? <CrmSalesDocumentsView
        accounts={props.accounts} opportunities={props.opportunities}
        documents={props.documents} salesItems={props.salesItems}
        busy={props.busy} refresh={props.refresh} notify={props.notify}
        canManageSales={props.canManageSales}
      /> : null}

      {tab === 'invoices' ? <CrmInvoicesView
        accounts={props.accounts} opportunities={props.opportunities}
        documents={props.documents} invoices={props.invoices} salesItems={props.salesItems}
        busy={props.busy} refresh={props.refresh} notify={props.notify}
        canManageSales={props.canManageSales}
      /> : null}

      {tab === 'credits' ? <CrmCreditNotesView
        accounts={props.accounts} invoices={props.invoices} creditNotes={props.creditNotes}
        busy={props.busy} refresh={props.refresh} notify={props.notify}
        canManageSales={props.canManageSales}
      /> : null}

      {tab === 'items' ? <CrmSalesItemsView
        items={props.salesItems} busy={props.busy} refresh={props.refresh}
        notify={props.notify} canManageSales={props.canManageSales}
      /> : null}
    </>
  )
}
