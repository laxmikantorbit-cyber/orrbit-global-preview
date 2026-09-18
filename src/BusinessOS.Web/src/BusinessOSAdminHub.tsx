import { useMemo, useState } from 'react'
import { getAutoPayStatus, getEntitlement, getSubscription } from './businessosApi'
import { createInvoice, getBillingReceipt, listBillingHistory, type BillingInvoice } from './billingApi'
import {
  getActivationCode, getAdminStatus, getDevices, reconcileOrder,
  replaceDevice, revokeDevice, type CommerceAdminStatus, type DeviceInventory,
} from './commerceAdminApi'
import { OrganisationProfileCard } from './OrganisationProfileCard'
import { SoftwareReleaseAdminCard } from './SoftwareReleaseAdminCard'
import './BusinessOSHub.css'

export function BusinessOSAdminHub() {
  const [token, setToken] = useState('')
  const [subscriptionId, setSubscriptionId] = useState('')
  const [status, setStatus] = useState<CommerceAdminStatus | null>(null)
  const [devices, setDevices] = useState<DeviceInventory | null>(null)
  const [subscription, setSubscription] = useState<Record<string, unknown> | null>(null)
  const [entitlement, setEntitlement] = useState<Record<string, unknown> | null>(null)
  const [autopay, setAutopay] = useState<Record<string, unknown> | null>(null)
  const [activationCode, setActivationCode] = useState('')
  const [billing, setBilling] = useState<BillingInvoice[]>([])
  const [billingOrderId, setBillingOrderId] = useState('')
  const [receiptText, setReceiptText] = useState('')
  const [replacementFingerprint, setReplacementFingerprint] = useState('')
  const [replacementName, setReplacementName] = useState('')
  const [replacementVersion, setReplacementVersion] = useState('')
  const [message, setMessage] = useState('')

  const selectedOrder = useMemo(() => status?.orders.find(x =>
    x.order.subscriptionId === subscriptionId) ?? null, [status, subscriptionId])

  const run = async (action: () => Promise<void>, success = 'Updated successfully.') => {
    setMessage('Working…')
    try { await action(); setMessage(success) }
    catch (error) { setMessage(error instanceof Error ? error.message : String(error)) }
  }
  const loadSubscription = async () => {
    if (!subscriptionId) return
    const [sub, ent, deviceList, code] = await Promise.all([
      getSubscription(token, subscriptionId),
      getEntitlement(token, subscriptionId),
      getDevices(token, subscriptionId),
      getActivationCode(token, subscriptionId),
    ])
    setSubscription(sub as unknown as Record<string, unknown>)
    setEntitlement(ent as unknown as Record<string, unknown>)
    setDevices(deviceList)
    setActivationCode(code.activationCode)
    try { setAutopay(await getAutoPayStatus(token, subscriptionId) as unknown as Record<string, unknown>) }
    catch { setAutopay(null) }
  }

  const loadAll = () => run(async () => {
    setStatus(await getAdminStatus(token))
    await loadSubscription()
    setBilling(await listBillingHistory(token, subscriptionId ? { subscriptionId } : { take: 50 }))
  }, 'Control centre refreshed.')

  const revoke = (fingerprint: string) => run(async () => {
    await revokeDevice(token, subscriptionId, fingerprint)
    setDevices(await getDevices(token, subscriptionId))
  }, 'Device revoked.')

  const replace = (oldDeviceFingerprint: string) => run(async () => {
    if (!replacementFingerprint.trim()) throw new Error('New device fingerprint is required.')
    await replaceDevice(token, subscriptionId, {
      oldDeviceFingerprint,
      newDeviceFingerprint: replacementFingerprint.trim(),
      deviceName: replacementName.trim() || undefined,
      appVersion: replacementVersion.trim() || undefined,
    })
    setReplacementFingerprint(''); setReplacementName(''); setReplacementVersion('')
    setDevices(await getDevices(token, subscriptionId))
  }, 'Device replaced.')
  const reconcile = (providerOrderId: string) => run(async () => {
    await reconcileOrder(token, providerOrderId)
    setStatus(await getAdminStatus(token))
    await loadSubscription()
  }, 'Reconciliation completed.')

  const generateInvoice = () => run(async () => {
    if (!billingOrderId.trim()) throw new Error('Commerce order ID is required.')
    await createInvoice(token, billingOrderId.trim())
    setBilling(await listBillingHistory(token, subscriptionId ? { subscriptionId } : { take: 50 }))
  }, 'Invoice generated / already available.')

  const showReceipt = (invoiceId: string) => run(async () => {
    setReceiptText(JSON.stringify(await getBillingReceipt(token, invoiceId), null, 2))
  }, 'Receipt loaded.')

  return <main className="bos-shell">
    <header><p className="eyebrow">oRRbit.BusinessOS</p><h1>Commerce & Licensing Admin</h1>
      <p>Subscriptions, payments, renewals, entitlement, activation, AutoPay and desktop-device control.</p></header>
    <section className="bos-card bos-form">
      <label>Bearer token<input type="password" autoComplete="off" value={token} onChange={e => setToken(e.target.value)} placeholder="Commerce Admin token" /></label>
      <label>Subscription ID<input value={subscriptionId} onChange={e => setSubscriptionId(e.target.value)} placeholder="GUID" /></label>
      <button onClick={loadAll}>Refresh control centre</button><span className="bos-message">{message}</span>
    </section>

    <SoftwareReleaseAdminCard token={token} />

    {status && <section className="bos-metrics">
      <article><span>Pending orders</span><strong>{status.counts.pendingOrders}</strong></article>
      <article><span>Captured payments</span><strong>{status.counts.capturedPayments}</strong></article>
      <article><span>Active subscriptions</span><strong>{status.counts.activeSubscriptions}</strong></article>
      <article><span>Renewals</span><strong>{status.counts.renewals}</strong></article>
      <article><span>Needs reconciliation</span><strong>{status.counts.needsReconciliation}</strong></article>
      <article><span>Failed payments</span><strong>{status.counts.failedPayments}</strong></article>
    </section>}

    {subscription && <OrganisationProfileCard token={token} subscriptionId={subscriptionId} title="Customer organisation & GST profile" />}

    {(subscription || entitlement || autopay) && <div className="bos-grid">
      {subscription && <section className="bos-card"><h2>Subscription</h2><pre>{JSON.stringify(subscription, null, 2)}</pre></section>}
      {entitlement && <section className="bos-card"><h2>Entitlement & renewal</h2><pre>{JSON.stringify(entitlement, null, 2)}</pre></section>}
      <section className="bos-card"><h2>AutoPay</h2>{autopay ? <pre>{JSON.stringify(autopay, null, 2)}</pre> : <p>Not configured.</p>}</section>
    </div>}
    {activationCode && <section className="bos-card"><h2>Activation code</h2><strong className="code">{activationCode}</strong></section>}

    {devices && <section className="bos-card"><h2>Licensing devices</h2>
      <p>{devices.activeDesktopDevices} active / {devices.desktopDeviceLimit} allowed</p>
      <div className="bos-replace-grid">
        <input value={replacementFingerprint} onChange={e => setReplacementFingerprint(e.target.value)} placeholder="New device fingerprint" />
        <input value={replacementName} onChange={e => setReplacementName(e.target.value)} placeholder="New device name" />
        <input value={replacementVersion} onChange={e => setReplacementVersion(e.target.value)} placeholder="App version" />
      </div>
      <div className="bos-grid">{devices.devices.map(device => <article key={device.id} className="device-card">
        <strong>{device.deviceName || device.deviceFingerprint}</strong><span className={device.active ? 'pill ok' : 'pill muted'}>{device.active ? 'Active' : 'Inactive'}</span>
        <small>{device.deviceFingerprint}</small><small>{device.appVersion || 'App version unavailable'}</small>
        <small>Last validation: {device.lastValidatedAtUtc || 'Never'}</small>
        {device.active && <div className="bos-actions"><button onClick={() => replace(device.deviceFingerprint)}>Replace</button>
          <button className="danger" onClick={() => revoke(device.deviceFingerprint)}>Revoke</button></div>}
      </article>)}</div>
    </section>}

    {selectedOrder && <section className="bos-card"><h2>Selected order</h2><pre>{JSON.stringify(selectedOrder, null, 2)}</pre></section>}
    {status && <section className="bos-card"><h2>Payment & reconciliation queue</h2>
      <div className="bos-table-wrap"><table className="bos-table"><thead><tr><th>Order</th><th>Payment</th><th>Reconciliation</th><th>Subscription</th><th>Action</th></tr></thead>
      <tbody>{status.orders.map(item => { const providerId = item.order.providerOrderId || item.order.razorpayOrderId || ''
        return <tr key={item.order.commerceOrderId}><td>{providerId || item.order.commerceOrderId}</td><td>{item.paymentStatus || item.order.orderStatus}</td>
          <td>{item.reconciliationStatus}</td><td>{item.order.subscriptionId || 'Initial purchase'}</td><td>{providerId && <button onClick={() => reconcile(providerId)}>Reconcile</button>}</td></tr> })}</tbody></table></div>
    </section>}
    <section className="bos-card"><h2>Billing & GST invoices</h2>
      <div className="bos-replace-grid"><input value={billingOrderId} onChange={e => setBillingOrderId(e.target.value)} placeholder="Paid commerce order ID" />
        <button onClick={generateInvoice}>Generate / recover invoice</button></div>
      <div className="bos-table-wrap"><table className="bos-table"><thead><tr><th>Invoice</th><th>Type</th><th>Gross</th><th>GST</th><th>Supply</th><th>Action</th></tr></thead>
        <tbody>{billing.map(invoice => <tr key={invoice.id}><td>{invoice.invoiceNumber}</td><td>{invoice.invoiceType}</td>
          <td>{invoice.currencyCode} {invoice.tax.grossAmount.toFixed(2)}</td><td>{invoice.tax.totalTax.toFixed(2)}</td><td>{invoice.tax.supplyType}</td>
          <td><button onClick={() => showReceipt(invoice.id)}>Receipt</button></td></tr>)}</tbody></table></div>
      {receiptText && <pre>{receiptText}</pre>}
    </section>
    <nav><a href="/businessos/portal">Customer Portal →</a><a href="/crm">CRM →</a></nav>
  </main>
}
