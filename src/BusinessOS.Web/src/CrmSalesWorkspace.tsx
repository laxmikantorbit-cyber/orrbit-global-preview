import { useState } from 'react'
import { type CrmAccount, type CrmInvoice, type CrmOpportunity, type CrmSalesDocument } from './crmApi'
import { CrmInvoicesView } from './CrmInvoicesView'
import { CrmSalesDocumentsView } from './CrmSalesDocumentsView'

type Props = {
  accounts: CrmAccount[]
  opportunities: CrmOpportunity[]
  documents: CrmSalesDocument[]
  invoices: CrmInvoice[]
  busy: boolean
  refresh: () => Promise<void>
  notify: (message: string) => void
  canManageSales: boolean
}

export function CrmSalesWorkspace(props: Props) {
  const [tab, setTab] = useState<'documents' | 'invoices'>('documents')
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
      </div>
      {tab === 'documents'
        ? <CrmSalesDocumentsView
            accounts={props.accounts}
            opportunities={props.opportunities}
            documents={props.documents}
            busy={props.busy}
            refresh={props.refresh}
            notify={props.notify}
            canManageSales={props.canManageSales}
          />
        : <CrmInvoicesView
            accounts={props.accounts}
            opportunities={props.opportunities}
            documents={props.documents}
            invoices={props.invoices}
            busy={props.busy}
            refresh={props.refresh}
            notify={props.notify}
            canManageSales={props.canManageSales}
          />}
    </>
  )
}
