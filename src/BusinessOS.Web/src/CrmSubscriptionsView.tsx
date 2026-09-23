import { useMemo, useState } from 'react'
import { type CrmSubscription } from './crmApi'
import { exportCrmSpreadsheet } from './crmSpreadsheet'

type Props = {
  subscriptions: CrmSubscription[]
  busy: boolean
  openSales: () => void
}

function fmtDate(value?: string | null) {
  if (!value) return '—'
  const d = new Date(value.length === 10 ? value + 'T00:00:00' : value)
  return Number.isNaN(d.getTime()) ? value : d.toLocaleDateString('en-IN')
}

export function CrmSubscriptionsView({ subscriptions, busy, openSales }: Props) {
  const [query, setQuery] = useState('')
  const [status, setStatus] = useState('All')
  const statuses = useMemo(() => Array.from(new Set(subscriptions.map(x => x.status).filter(Boolean))).sort(), [subscriptions])
  const filtered = useMemo(() => {
    const needle = query.trim().toLowerCase()
    return subscriptions.filter(item =>
      (status === 'All' || item.status === status) &&
      (!needle || [item.accountName, item.productCode, item.subscriptionId, item.licenseId].join(' ').toLowerCase().includes(needle)))
  }, [subscriptions, query, status])
  const countLike = (...names: string[]) => subscriptions.filter(x => names.some(name => x.status.toLowerCase().replace(/\s/g,'') === name.toLowerCase().replace(/\s/g,''))).length

  async function exportRows() {
    await exportCrmSpreadsheet('crm-subscriptions', {
      headers: ['Subscription Name','Customer','Status','Next Billing Cycle','Date Subscribed','License Id','Renewals'],
      rows: filtered.map(x => [x.productCode,x.accountName,x.status,x.validUntil,x.startsOn,x.licenseId,x.renewalCount]),
    }, 'xlsx')
  }

  return <section className="crm2-ref-list-page crm2-subscriptions-reference">
    <div className="crm2-ref-action-row">
      <button className="crm2-ref-primary" onClick={openSales}>+ New Subscription</button>
      <span className="crm2-action-spacer" />
      <button className="crm2-ref-square">▼</button>
    </div>

    <section className="crm2-reference-status-summary crm2-subscription-summary">
      <h2><small>stripe</small> Subscriptions Summary</h2>
      <div>
        <span><b>0</b><em className="blue">Not Subscribed</em></span>
        <span><b>{countLike('Active')}</b><em className="good">Active</em></span>
        <span><b>{countLike('Future')}</b><em className="good">Future</em></span>
        <span><b>{countLike('PastDue','Past Due','Expired')}</b><em className="bad">Past Due</em></span>
        <span><b>{countLike('Unpaid')}</b><em className="bad">Unpaid</em></span>
        <span><b>{countLike('Incomplete')}</b><em className="warn">Incomplete</em></span>
        <span><b>{countLike('Canceled','Cancelled')}</b><em>Canceled</em></span>
        <span><b>{countLike('IncompleteExpired','Incomplete Expired')}</b><em>Incomplete Expired</em></span>
      </div>
    </section>

    <section className="crm2-ref-table-card">
      <div className="crm2-ref-table-tools">
        <select><option>25</option><option>50</option></select>
        <button onClick={() => void exportRows()} disabled={busy || filtered.length === 0}>Export</button>
        <button>↻</button>
        <select value={status} onChange={e => setStatus(e.target.value)}><option>All</option>{statuses.map(x => <option key={x}>{x}</option>)}</select>
        <span /><label><b>⌕</b><input value={query} onChange={e => setQuery(e.target.value)} placeholder="Search..." /></label>
      </div>
      <div className="crm2-subscription-head"><span>#</span><span>Subscription Name</span><span>Customer</span><span>Project</span><span>Status</span><span>Next Billing Cycle</span><span>Date Subscribed</span><span>Last Sent</span></div>
      {filtered.length === 0 ? <p className="crm2-reference-empty">No entries found</p> : filtered.map((item,index) =>
        <div className="crm2-subscription-row" key={item.subscriptionId}>
          <span>{index+1}</span><span><a>{item.productCode}</a><small>{item.subscriptionId.slice(0,8)}…</small></span><span>{item.accountName}</span><span>—</span>
          <span><em className={'crm2-sales-status '+item.status.toLowerCase().replace(/\s/g,'')}>{item.status}</em></span><span>{fmtDate(item.validUntil)}</span><span>{fmtDate(item.startsOn)}</span><span>—</span>
        </div>)}
    </section>
  </section>
}
