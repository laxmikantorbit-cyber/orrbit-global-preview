import { useState } from 'react'
import {
  cancelAtPeriodEnd, getAutoPayStatus, getEntitlement, getSubscription, listSubscriptions, setupAutoPay,
  type AutoPayStatus, type EntitlementResponse, type SubscriptionListItem,
} from './businessosApi'
import { getBillingReceipt, listBillingHistory, type BillingInvoice } from './billingApi'
import { OrganisationProfileCard } from './OrganisationProfileCard'
import { SoftwareDeliveryCard } from './SoftwareDeliveryCard'
import './BusinessOSHub.css'

export function BusinessOSCustomerPortal() {
  const [token, setToken] = useState('')
  const [subscriptionId, setSubscriptionId] = useState('')
  const [subscriptions, setSubscriptions] = useState<SubscriptionListItem[]>([])
  const [subscription, setSubscription] = useState<Awaited<ReturnType<typeof getSubscription>> | null>(null)
  const [entitlement, setEntitlement] = useState<EntitlementResponse | null>(null)
  const [autopay, setAutopay] = useState<AutoPayStatus | null>(null)
  const [billing, setBilling] = useState<BillingInvoice[]>([])
  const [receiptText, setReceiptText] = useState('')
  const [message, setMessage] = useState('')

  const discover = async () => {
    if (!token.trim()) {
      setMessage('Enter the staging access token first.')
      return
    }
    setMessage('Finding your subscriptions…')
    try {
      const items = await listSubscriptions(token.trim())
      setSubscriptions(items)
      setSubscription(null); setEntitlement(null); setAutopay(null); setBilling([])
      if (items.length === 0) {
        setSubscriptionId('')
        setMessage('No subscriptions were found for this account.')
        return
      }
      const selected = items.some(x => x.subscriptionId === subscriptionId)
        ? subscriptionId
        : items[0].subscriptionId
      setSubscriptionId(selected)
      setMessage(`${items.length} subscription${items.length === 1 ? '' : 's'} found. Select one and load it.`)
    } catch (error) { setMessage(error instanceof Error ? error.message : String(error)) }
  }

  const load = async () => {
    if (!subscriptionId) {
      setMessage('Select a subscription first.')
      return
    }
    setMessage('Loading…')
    try {
      const [sub, ent] = await Promise.all([
        getSubscription(token, subscriptionId),
        getEntitlement(token, subscriptionId),
      ])
      setSubscription(sub); setEntitlement(ent)
      setBilling(await listBillingHistory(token, { subscriptionId }))
      try { setAutopay(await getAutoPayStatus(token, subscriptionId)) } catch { setAutopay(null) }
      setMessage('Subscription loaded.')
    } catch (error) { setMessage(error instanceof Error ? error.message : String(error)) }
  }

  const cancelRenewal = async () => {
    try {
      setEntitlement(await cancelAtPeriodEnd(token, subscriptionId))
      setMessage('Cancellation scheduled for period end. Access remains valid until expiry.')
    } catch (error) { setMessage(error instanceof Error ? error.message : String(error)) }
  }
  const enableAutoPay = async () => {
    try {
      const setup = await setupAutoPay(token, subscriptionId)
      setMessage(setup.authorizationUrl ? 'AutoPay setup created. Complete authorization using the provider link.' : 'AutoPay setup created.')
      try { setAutopay(await getAutoPayStatus(token, subscriptionId)) } catch { setAutopay(null) }
    } catch (error) { setMessage(error instanceof Error ? error.message : String(error)) }
  }

  const showReceipt = async (invoiceId: string) => {
    try {
      setReceiptText(JSON.stringify(await getBillingReceipt(token, invoiceId), null, 2))
      setMessage('Receipt loaded.')
    } catch (error) { setMessage(error instanceof Error ? error.message : String(error)) }
  }

  return <main className="bos-shell">
    <header><p className="eyebrow">oRRbit.BusinessOS</p><h1>Customer Self-Service Portal</h1>
      <p>Subscription, entitlement, licence, AutoPay and renewal control in one place.</p></header>
    <section className="bos-card bos-form">
      <label>Staging access token<input type="password" autoComplete="off" value={token}
        onChange={e => setToken(e.target.value)} placeholder="FreeTesting access token" /></label>
      <label>Subscription<select value={subscriptionId} disabled={subscriptions.length === 0}
        onChange={e => setSubscriptionId(e.target.value)}>
        {subscriptions.length === 0 && <option value="">Find subscriptions first</option>}
        {subscriptions.map(item => <option key={item.subscriptionId} value={item.subscriptionId}>
          {item.productCode} · {item.status} · valid to {item.validUntil}
        </option>)}
      </select></label>
      <div className="bos-actions"><button onClick={discover}>Find subscriptions</button>
        <button onClick={load} disabled={!subscriptionId}>Load selected</button></div>
      <span className="bos-message">{message}</span>
    </section>

    {subscription && <section className="bos-metrics">
      <article><span>Product</span><strong>{subscription.productCode}</strong></article>
      <article><span>Valid until</span><strong>{subscription.validUntil}</strong></article>
      <article><span>Renewal</span><strong>{entitlement?.renewalStatus || '—'}</strong></article>
      <article><span>AutoPay</span><strong>{autopay?.status || entitlement?.autoPayProviderStatus || 'Not configured'}</strong></article>
    </section>}

    {subscription && <OrganisationProfileCard token={token} subscriptionId={subscriptionId} />}
    {subscription && <SoftwareDeliveryCard token={token} subscriptionId={subscriptionId} />}
    <div className="bos-grid">
      {subscription && <section className="bos-card"><h2>Subscription</h2><pre>{JSON.stringify(subscription, null, 2)}</pre></section>}
      {entitlement && <section className="bos-card"><h2>Entitlement & renewal</h2>
        <dl className="bos-detail-list"><div><dt>Status</dt><dd>{entitlement.status}</dd></div><div><dt>Renewal</dt><dd>{entitlement.renewalStatus}</dd></div>
          <div><dt>Auto-renew</dt><dd>{entitlement.autoRenewEnabled ? 'Enabled' : 'Disabled'}</dd></div><div><dt>Valid until</dt><dd>{entitlement.validUntil}</dd></div></dl>
        <button className="danger" onClick={cancelRenewal} disabled={entitlement.cancelAtPeriodEnd}>{entitlement.cancelAtPeriodEnd ? 'Cancellation already scheduled' : 'Cancel at period end'}</button>
      </section>}
      <section className="bos-card"><h2>AutoPay</h2>{autopay ? <dl className="bos-detail-list">
        <div><dt>Status</dt><dd>{autopay.status}</dd></div><div><dt>Auto-renew</dt><dd>{autopay.autoRenewEnabled ? 'Enabled' : 'Disabled'}</dd></div>
        <div><dt>Cancel at period end</dt><dd>{autopay.cancelAtPeriodEnd ? 'Yes' : 'No'}</dd></div></dl> : <p>AutoPay is not configured for this subscription.</p>}
        {!entitlement?.cancelAtPeriodEnd && <button onClick={enableAutoPay}>Set up / refresh AutoPay</button>}</section>
    </div>
    <section className="bos-card"><h2>Billing history</h2>
      <div className="bos-table-wrap"><table className="bos-table"><thead><tr><th>Invoice</th><th>Type</th><th>Gross</th><th>GST</th><th>Paid</th><th>Action</th></tr></thead>
        <tbody>{billing.map(invoice => <tr key={invoice.id}><td>{invoice.invoiceNumber}</td><td>{invoice.invoiceType}</td>
          <td>{invoice.currencyCode} {invoice.tax.grossAmount.toFixed(2)}</td><td>{invoice.tax.totalTax.toFixed(2)}</td><td>{invoice.paidAtUtc}</td>
          <td><button onClick={() => showReceipt(invoice.id)}>View receipt</button></td></tr>)}</tbody></table></div>
      {billing.length === 0 && <p>No billing documents found for this subscription.</p>}
      {receiptText && <pre>{receiptText}</pre>}
    </section>
    <nav><a href="/businessos/admin">Admin Control Centre →</a><a href="/crm">CRM →</a></nav>
  </main>
}
