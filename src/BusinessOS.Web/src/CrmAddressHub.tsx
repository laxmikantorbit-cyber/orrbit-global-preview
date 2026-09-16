import { useEffect, useMemo, useState } from 'react'
import './CrmDemo.css'
import './CrmAdvancedHub.css'
import { listCrmAccounts, type CrmAccount } from './crmApi'
import { addCrmAddress, deactivateCrmAddress, listCrmAddresses, updateCrmAddress, type CrmAddress } from './crmAddressApi'

export function CrmAddressHub() {
  const [accounts, setAccounts] = useState<CrmAccount[]>([])
  const [accountId, setAccountId] = useState('')
  const [addresses, setAddresses] = useState<CrmAddress[]>([])
  const [addressId, setAddressId] = useState('')
  const [line1, setLine1] = useState('')
  const [line2, setLine2] = useState('')
  const [city, setCity] = useState('')
  const [state, setState] = useState('')
  const [postalCode, setPostalCode] = useState('')
  const [countryCode, setCountryCode] = useState('IN')
  const [isPrimary, setIsPrimary] = useState(false)
  const [message, setMessage] = useState('Customer address workspace ready')
  const [busy, setBusy] = useState(false)

  const account = useMemo(() => accounts.find(x => x.id === accountId) || null, [accounts, accountId])

  async function refreshAccounts() {
    try {
      const result = await listCrmAccounts()
      setAccounts(result.accounts)
      if (!accountId && result.accounts[0]) setAccountId(result.accounts[0].id)
    } catch (error) { setMessage(error instanceof Error ? error.message : String(error)) }
  }

  async function refreshAddresses(id = accountId) {
    if (!id) { setAddresses([]); return }
    setBusy(true)
    try {
      const result = await listCrmAddresses(id)
      setAddresses(result.addresses)
      setMessage(`${result.addresses.length} address(es) loaded for customer`)
    } catch (error) { setMessage(error instanceof Error ? error.message : String(error)) }
    finally { setBusy(false) }
  }

  useEffect(() => { void refreshAccounts() }, [])
  useEffect(() => { if (accountId) void refreshAddresses(accountId) }, [accountId])
  useEffect(() => {
    const item = addresses.find(x => x.id === addressId)
    if (!item) { setLine1(''); setLine2(''); setCity(''); setState(''); setPostalCode(''); setCountryCode('IN'); setIsPrimary(false); return }
    setLine1(item.line1); setLine2(item.line2 || ''); setCity(item.city); setState(item.state); setPostalCode(item.postalCode); setCountryCode(item.countryCode); setIsPrimary(item.isPrimary)
  }, [addressId, addresses])

  const input = () => ({ line1, line2, city, state, postalCode, countryCode, isPrimary })

  async function save() {
    if (!accountId || !line1.trim() || !city.trim() || !state.trim() || !postalCode.trim()) return
    setBusy(true)
    try {
      if (addressId) await updateCrmAddress(accountId, addressId, input())
      else await addCrmAddress(accountId, input())
      setMessage(addressId ? 'Customer address updated and audited' : 'Customer address added and audited')
      setAddressId('')
      await refreshAddresses(accountId)
    } catch (error) { setMessage(error instanceof Error ? error.message : String(error)); setBusy(false) }
  }

  async function remove() {
    if (!accountId || !addressId) return
    setBusy(true)
    try {
      await deactivateCrmAddress(accountId, addressId)
      setMessage('Address removed and audit history preserved')
      setAddressId('')
      await refreshAddresses(accountId)
    } catch (error) { setMessage(error instanceof Error ? error.message : String(error)); setBusy(false) }
  }

  return <div className="crm2-app crm-advanced-app">
    <aside className="crm2-sidebar">
      <div className="crm2-brand"><div className="crm2-brand-mark">o</div><div><strong>oRRbit</strong><span>BusinessOS</span></div></div>
      <nav className="crm2-nav">
        <a className="crm-advanced-back" href="/crm"><span>←</span>CRM Home</a>
        <a className="crm-advanced-back" href="/crm/maintenance"><span>A</span>Accounts & Contacts</a>
        <a className="crm-advanced-back" href="/crm/manage"><span>≡</span>Audit & Settings</a>
      </nav>
      <div className="crm2-sidebar-foot"><strong>CUSTOMER 360</strong><small>Multiple addresses · Primary location · Audited changes</small></div>
    </aside>
    <main className="crm2-main">
      <header className="crm2-topbar"><div><span className="crm2-kicker">CUSTOMER ADDRESS MANAGEMENT</span><h1>Account locations & addresses</h1></div><div className="crm2-top-actions"><button className="crm2-refresh" disabled={busy} onClick={() => void refreshAddresses()}>↻ Refresh</button></div></header>
      <section className="crm2-statusbar"><div><span className={busy ? 'pulse busy' : 'pulse'} />{message}</div><span>{account?.name || 'Select customer'} · Persistent Postgres</span></section>
      <section className="crm-advanced-grid">
        <article className="crm2-table-card"><div className="crm2-section-head"><div><span>CUSTOMER</span><h2>Select account</h2></div></div><div className="crm-advanced-form stacked"><select value={accountId} onChange={e => { setAccountId(e.target.value); setAddressId('') }}><option value="">Select account</option>{accounts.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</select><select value={addressId} onChange={e => setAddressId(e.target.value)}><option value="">New address</option>{addresses.map(x => <option key={x.id} value={x.id}>{x.city}, {x.state}{x.isPrimary ? ' · Primary' : ''}</option>)}</select><small>{addresses.length} saved address(es)</small></div><div className="crm-advanced-list">{addresses.map(x => <article key={x.id}><div><b>{x.isPrimary ? 'Primary' : 'Address'}</b><strong>{x.line1}{x.line2 ? `, ${x.line2}` : ''}</strong><small>{x.city}, {x.state} {x.postalCode} · {x.countryCode}</small></div><button onClick={() => setAddressId(x.id)}>Edit</button></article>)}</div></article>
        <article className="crm2-table-card"><div className="crm2-section-head"><div><span>{addressId ? 'EDIT' : 'NEW'}</span><h2>{addressId ? 'Update address' : 'Add address'}</h2></div></div><div className="crm-advanced-form stacked"><input value={line1} onChange={e => setLine1(e.target.value)} placeholder="Address line 1"/><input value={line2} onChange={e => setLine2(e.target.value)} placeholder="Address line 2"/><input value={city} onChange={e => setCity(e.target.value)} placeholder="City"/><input value={state} onChange={e => setState(e.target.value)} placeholder="State"/><input value={postalCode} onChange={e => setPostalCode(e.target.value)} placeholder="PIN / Postal code"/><input value={countryCode} maxLength={2} onChange={e => setCountryCode(e.target.value.toUpperCase())} placeholder="Country code"/><label><input type="checkbox" checked={isPrimary} onChange={e => setIsPrimary(e.target.checked)}/> Primary address</label><div className="crm2-top-actions"><button className="crm2-primary" disabled={busy || !accountId || !line1.trim() || !city.trim() || !state.trim() || !postalCode.trim()} onClick={() => void save()}>{addressId ? 'Update Address' : 'Add Address'}</button>{addressId ? <button className="crm2-refresh" disabled={busy} onClick={() => void remove()}>Remove</button> : null}</div></div></article>
      </section>
    </main>
  </div>
}
