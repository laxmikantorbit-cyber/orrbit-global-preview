import { useMemo, useState } from 'react'
import { type CrmSubscription } from './crmApi'
import { exportCrmSpreadsheet, type CrmSpreadsheetFormat } from './crmSpreadsheet'

type Props = {
  subscriptions: CrmSubscription[]
  busy: boolean
  openSales: () => void
}

function money(value: number, currency: string) {
  try {
    return new Intl.NumberFormat('en-IN', { style: 'currency', currency: currency || 'INR', maximumFractionDigits: 0 }).format(value || 0)
  } catch {
    return `${currency || 'INR'} ${value || 0}`
  }
}

export function CrmSubscriptionsView({ subscriptions, busy, openSales }: Props) {
  const [query, setQuery] = useState('')
  const [status, setStatus] = useState('All')

  const filtered = useMemo(() => {
    const needle = query.trim().toLowerCase()
    return subscriptions.filter(item =>
      (status === 'All' || item.status === status) &&
      (!needle || [item.accountName, item.productCode, item.subscriptionId, item.licenseId]
        .join(' ').toLowerCase().includes(needle)))
  }, [subscriptions, query, status])

  const active = subscriptions.filter(x => x.status === 'Active').length
  const expired = subscriptions.filter(x => x.status === 'Expired').length
  const renewals = subscriptions.reduce((sum, x) => sum + x.renewalCount, 0)

  async function exportRows(format: CrmSpreadsheetFormat) {
    await exportCrmSpreadsheet('crm-subscriptions', {
      headers: ['Customer', 'Product', 'Subscription Id', 'License Id', 'Start', 'Valid Until', 'Status', 'Renewals', 'Latest Order Amount', 'Currency', 'Last Paid'],
      rows: filtered.map(x => [x.accountName, x.productCode, x.subscriptionId, x.licenseId, x.startsOn, x.validUntil, x.status, x.renewalCount, x.latestOrderAmount, x.currencyCode, x.lastPaidAtUtc]),
    }, format)
  }

  return <section className="crm2-ref-list-page">
    <div className="crm2-reference-module-head">
      <div><span className="crm2-kicker">BUSINESS MODULE</span><h2>Subscriptions</h2><p>Live subscriptions, licences, validity and renewals from the BusinessOS commerce ledger.</p></div>
      <button className="crm2-filter-button" onClick={openSales}>Open Sales / Checkout</button>
    </div>

    <section className="crm2-metrics">
      <article><span>Subscriptions</span><strong>{subscriptions.length}</strong><small>All activated subscriptions</small></article>
      <article className="accent"><span>Active</span><strong>{active}</strong><small>Currently entitled</small></article>
      <article><span>Expired</span><strong>{expired}</strong><small>Validity ended</small></article>
      <article><span>Renewals</span><strong>{renewals}</strong><small>Recorded renewal events</small></article>
    </section>

    <section className="crm2-ref-filter-card">
      <strong>Filter subscriptions</strong>
      <div className="crm2-ref-filter-grid">
        <input value={query} onChange={e => setQuery(e.target.value)} placeholder="Customer, product, subscription or licence..." />
        <select value={status} onChange={e => setStatus(e.target.value)}><option>All</option><option>Active</option><option>Expired</option></select>
        <button onClick={() => void exportRows('csv')} disabled={busy || filtered.length === 0}>Export CSV</button>
        <button onClick={() => void exportRows('xlsx')} disabled={busy || filtered.length === 0}>Export Excel</button>
      </div>
    </section>

    <section className="crm2-ref-table-card">
      <div className="crm2-reference-head"><span>Customer / Product</span><span>Validity</span><span>Renewals</span><span>Latest payment</span><span>Status</span></div>
      {filtered.length === 0 ? <p className="crm2-reference-empty">No subscriptions found</p> : filtered.map(item =>
        <div className="crm2-reference-row" key={item.subscriptionId}>
          <span><strong>{item.accountName}</strong><small>{item.productCode} · {item.subscriptionId.slice(0, 8)}…</small></span>
          <span><strong>{item.validUntil}</strong><small>Started {item.startsOn}</small></span>
          <span><strong>{item.renewalCount}</strong><small>License {item.licenseId.slice(0, 8)}…</small></span>
          <span><strong>{money(item.latestOrderAmount, item.currencyCode)}</strong><small>{item.lastPaidAtUtc ? new Date(item.lastPaidAtUtc).toLocaleDateString('en-IN') : 'No paid timestamp'}</small></span>
          <span><b>{item.status}</b></span>
        </div>)}
    </section>
  </section>
}
