import { useEffect, useMemo, useState } from 'react'
import './CrmDemo.css'
import './CrmAdvancedHub.css'
import {
  createCrmOpportunity,
  getCrmSession,
  listCrmAccounts,
  listCrmTeam,
  type CrmAccount,
  type CrmSession,
  type CrmTeamMember,
} from './crmApi'
import {
  listDetailedCrmOpportunities,
  updateCrmOpportunityProductService,
  type CrmDetailedOpportunity,
} from './crmOpportunityProductApi'

function money(value: number, currency = 'INR') {
  try { return new Intl.NumberFormat('en-IN', { style: 'currency', currency, maximumFractionDigits: 0 }).format(value || 0) }
  catch { return `${currency} ${value || 0}` }
}

export function CrmOpportunityProductHub() {
  const [session, setSession] = useState<CrmSession | null>(null)
  const [accounts, setAccounts] = useState<CrmAccount[]>([])
  const [team, setTeam] = useState<CrmTeamMember[]>([])
  const [items, setItems] = useState<CrmDetailedOpportunity[]>([])
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState('Opportunity product workspace ready')
  const [editingId, setEditingId] = useState<string | null>(null)
  const [editingProduct, setEditingProduct] = useState('')
  const [accountId, setAccountId] = useState('')
  const [title, setTitle] = useState('')
  const [productService, setProductService] = useState('')
  const [estimatedValue, setEstimatedValue] = useState(0)
  const [probability, setProbability] = useState(50)
  const [closeDate, setCloseDate] = useState('')
  const [ownerUserId, setOwnerUserId] = useState('')

  const accountName = (id: string) => accounts.find(x => x.id === id)?.name || id
  const ownerName = (id?: string | null) => team.find(x => x.id === id)?.displayName || 'Unassigned'
  const activeTeam = useMemo(() => team.filter(x => x.active), [team])

  async function refresh() {
    setBusy(true)
    try {
      const [sessionResult, accountResult, teamResult, opportunityResult] = await Promise.all([
        getCrmSession(), listCrmAccounts(), listCrmTeam(), listDetailedCrmOpportunities(),
      ])
      setSession(sessionResult)
      setAccounts(accountResult.accounts)
      setTeam(teamResult.members)
      setItems(opportunityResult.opportunities)
      if (!accountId && accountResult.accounts[0]) setAccountId(accountResult.accounts[0].id)
      if (!ownerUserId && !sessionResult.canViewAllOwnedRecords) setOwnerUserId(sessionResult.member.id)
      setMessage(`${opportunityResult.opportunities.length} opportunity/deal(s) loaded`)
    } catch (error) {
      setMessage(error instanceof Error ? error.message : String(error))
    } finally { setBusy(false) }
  }

  useEffect(() => { void refresh() }, [])

  async function createDeal() {
    if (!accountId || !title.trim()) { setMessage('Account and deal title are required'); return }
    setBusy(true)
    try {
      const created = await createCrmOpportunity({
        accountId,
        title: title.trim(),
        estimatedValue,
        currencyCode: 'INR',
        probabilityPercent: probability,
        expectedCloseDate: closeDate || undefined,
        ownerUserId: ownerUserId || undefined,
      } as Parameters<typeof createCrmOpportunity>[0] & { ownerUserId?: string })
      if (productService.trim())
        await updateCrmOpportunityProductService(created.id, productService.trim())
      setTitle(''); setProductService(''); setEstimatedValue(0); setProbability(50); setCloseDate('')
      setMessage('Opportunity created with Product/Service')
      await refresh()
    } catch (error) {
      setMessage(error instanceof Error ? error.message : String(error)); setBusy(false)
    }
  }

  async function saveProduct(id: string) {
    setBusy(true)
    try {
      await updateCrmOpportunityProductService(id, editingProduct.trim() || null)
      setEditingId(null); setEditingProduct('')
      setMessage('Opportunity Product/Service updated')
      await refresh()
    } catch (error) {
      setMessage(error instanceof Error ? error.message : String(error)); setBusy(false)
    }
  }

  return <div className="crm2-app crm-advanced-app">
    <aside className="crm2-sidebar">
      <div className="crm2-brand"><div className="crm2-brand-mark">o</div><div><strong>oRRbit</strong><span>BusinessOS</span></div></div>
      <nav className="crm2-nav">
        <a className="crm-advanced-back" href="/crm"><span>←</span>CRM Home</a>
        <a className="crm-advanced-back" href="/crm/maintenance"><span>◎</span>Data Maintenance</a>
        <a className="crm-advanced-back" href="/crm/analytics"><span>↗</span>Analytics</a>
      </nav>
      <div className="crm2-sidebar-foot"><strong>DEAL PRODUCT CONTROL</strong><small>Product/Service · Value · Owner · Forecast</small></div>
    </aside>
    <main className="crm2-main">
      <header className="crm2-topbar"><div><span className="crm2-kicker">OPPORTUNITIES / DEALS</span><h1>Product & service opportunity workspace</h1></div><div className="crm2-top-actions"><button className="crm2-refresh" disabled={busy} onClick={() => void refresh()}>↻ Refresh</button></div></header>
      <section className="crm2-statusbar"><div><span className={busy ? 'pulse busy' : 'pulse'} />{message}</div><span>{session ? `${session.member.displayName} · ${session.canViewAllOwnedRecords ? 'Team scope' : 'My scope'}` : 'CRM'}</span></section>

      <section className="crm2-table-card" style={{ marginBottom: 18 }}>
        <div className="crm2-section-head"><div><span>NEW DEAL</span><h2>Create product/service opportunity</h2></div></div>
        <div className="crm2-form-grid">
          <label>Customer account<select value={accountId} onChange={e => setAccountId(e.target.value)}><option value="">Select account</option>{accounts.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
          <label>Deal title<input value={title} onChange={e => setTitle(e.target.value)} placeholder="e.g. AI Repair Hybrid rollout" /></label>
          <label>Product / Service<input value={productService} onChange={e => setProductService(e.target.value)} placeholder="Product or service" /></label>
          <label>Deal value<input type="number" min={0} value={estimatedValue} onChange={e => setEstimatedValue(Number(e.target.value))} /></label>
          <label>Probability %<input type="number" min={0} max={100} value={probability} onChange={e => setProbability(Number(e.target.value))} /></label>
          <label>Expected close date<input type="date" value={closeDate} onChange={e => setCloseDate(e.target.value)} /></label>
          <label>Deal owner<select value={ownerUserId} onChange={e => setOwnerUserId(e.target.value)} disabled={!session?.canViewAllOwnedRecords}><option value="">Unassigned</option>{activeTeam.map(x => <option key={x.id} value={x.id}>{x.displayName} · {x.role}</option>)}</select></label>
        </div>
        <div className="crm2-drawer-actions"><button className="crm2-primary" disabled={busy || !accountId || !title.trim()} onClick={() => void createDeal()}>Create Opportunity</button></div>
      </section>

      <section className="crm2-table-card">
        <div className="crm2-section-head"><div><span>DEAL REGISTER</span><h2>{items.length} opportunity/deal(s)</h2></div></div>
        <div className="crm-advanced-list">
          {items.map(item => <article key={item.id}>
            <div style={{ flex: 1 }}>
              <b>{accountName(item.accountId)} · {item.stage}</b>
              <strong>{item.title}</strong>
              {editingId === item.id ? <div className="crm-advanced-form" style={{ marginTop: 8 }}><input autoFocus value={editingProduct} onChange={e => setEditingProduct(e.target.value)} placeholder="Product / Service"/><button className="crm2-primary" disabled={busy} onClick={() => void saveProduct(item.id)}>Save</button><button onClick={() => setEditingId(null)}>Cancel</button></div> : <small>Product/Service: {item.productService || 'Not set'} · Owner: {ownerName(item.ownerUserId)} · {item.probabilityPercent}% · Close {item.expectedCloseDate || '—'}</small>}
            </div>
            <div style={{ textAlign: 'right' }}><strong>{money(item.estimatedValue, item.currencyCode)}</strong><button style={{ display: 'block', marginTop: 8 }} onClick={() => { setEditingId(item.id); setEditingProduct(item.productService || '') }}>Edit Product</button></div>
          </article>)}
          {!items.length ? <p>No opportunities yet.</p> : null}
        </div>
      </section>
    </main>
  </div>
}
