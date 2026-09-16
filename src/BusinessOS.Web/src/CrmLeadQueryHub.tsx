import { useEffect, useMemo, useState } from 'react'
import './CrmDemo.css'
import './CrmAdvancedHub.css'
import { getCrmSession, listCrmTeam, type CrmSession, type CrmTeamMember } from './crmApi'
import { listCrmSavedViews, saveCrmSavedView, type CrmSavedView } from './crmManagementApi'
import { queryCrmLeads, type CrmLeadQueryFilters, type CrmLeadQueryItem } from './crmLeadQueryApi'

type LeadFilters = {
  q: string
  status: string
  priority: string
  leadSource: string
  product: string
  tag: string
  ownerUserId: string
  createdFrom: string
  createdTo: string
  followUpFrom: string
  followUpTo: string
}

const emptyFilters: LeadFilters = {
  q: '', status: '', priority: '', leadSource: '', product: '', tag: '', ownerUserId: '',
  createdFrom: '', createdTo: '', followUpFrom: '', followUpTo: '',
}

function startUtc(value: string) { return value ? `${value}T00:00:00Z` : undefined }
function endUtc(value: string) { return value ? `${value}T23:59:59Z` : undefined }
function when(value?: string | null) {
  if (!value) return '—'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : new Intl.DateTimeFormat('en-IN', { dateStyle: 'medium', timeStyle: 'short' }).format(date)
}

function toApiFilters(filters: LeadFilters, page: number, pageSize: number): CrmLeadQueryFilters {
  return {
    q: filters.q || undefined,
    status: filters.status || undefined,
    priority: filters.priority || undefined,
    leadSource: filters.leadSource || undefined,
    product: filters.product || undefined,
    tag: filters.tag || undefined,
    ownerUserId: filters.ownerUserId || undefined,
    createdFromUtc: startUtc(filters.createdFrom),
    createdToUtc: endUtc(filters.createdTo),
    followUpFromUtc: startUtc(filters.followUpFrom),
    followUpToUtc: endUtc(filters.followUpTo),
    page,
    pageSize,
  }
}

function normalizeSaved(value: unknown): LeadFilters {
  if (!value || typeof value !== 'object') return emptyFilters
  const x = value as Record<string, unknown>
  return {
    q: String(x.q ?? ''),
    status: String(x.status ?? ''),
    priority: String(x.priority ?? ''),
    leadSource: String(x.leadSource ?? ''),
    product: String(x.product ?? ''),
    tag: String(x.tag ?? ''),
    ownerUserId: String(x.ownerUserId ?? ''),
    createdFrom: String(x.createdFrom ?? ''),
    createdTo: String(x.createdTo ?? ''),
    followUpFrom: String(x.followUpFrom ?? ''),
    followUpTo: String(x.followUpTo ?? ''),
  }
}

export function CrmLeadQueryHub() {
  const [session, setSession] = useState<CrmSession | null>(null)
  const [team, setTeam] = useState<CrmTeamMember[]>([])
  const [savedViews, setSavedViews] = useState<CrmSavedView[]>([])
  const [filters, setFilters] = useState<LeadFilters>(emptyFilters)
  const [rows, setRows] = useState<CrmLeadQueryItem[]>([])
  const [total, setTotal] = useState(0)
  const [page, setPage] = useState(1)
  const [pageSize, setPageSize] = useState(25)
  const [message, setMessage] = useState('Advanced lead query ready')
  const [busy, setBusy] = useState(false)
  const [saveName, setSaveName] = useState('')
  const [saveDefault, setSaveDefault] = useState(false)

  const leadViews = useMemo(() => savedViews.filter(x => x.module.toLowerCase() === 'leads'), [savedViews])
  const totalPages = Math.max(1, Math.ceil(total / pageSize))
  const activeTeam = useMemo(() => team.filter(x => x.active), [team])

  async function run(targetPage = page, current = filters, size = pageSize) {
    setBusy(true)
    try {
      const result = await queryCrmLeads(toApiFilters(current, targetPage, size))
      setRows(result.leads)
      setTotal(result.total)
      setPage(result.page)
      setPageSize(result.pageSize)
      setMessage(`${result.total} matching lead(s) · page ${result.page}`)
    } catch (error) {
      setMessage(error instanceof Error ? error.message : String(error))
    } finally { setBusy(false) }
  }

  async function bootstrap() {
    setBusy(true)
    try {
      const [sessionResult, teamResult, viewsResult] = await Promise.all([
        getCrmSession(), listCrmTeam(), listCrmSavedViews(),
      ])
      setSession(sessionResult)
      setTeam(teamResult.members)
      setSavedViews(viewsResult.views)
      const defaultView = viewsResult.views.find(x => x.module.toLowerCase() === 'leads' && x.isDefault)
      const initial = defaultView ? normalizeSaved(JSON.parse(defaultView.filtersJson)) : emptyFilters
      setFilters(initial)
      const result = await queryCrmLeads(toApiFilters(initial, 1, pageSize))
      setRows(result.leads); setTotal(result.total); setPage(result.page); setPageSize(result.pageSize)
      setMessage(defaultView ? `Default view applied: ${defaultView.name}` : `${result.total} lead(s) loaded`)
    } catch (error) {
      setMessage(error instanceof Error ? error.message : String(error))
    } finally { setBusy(false) }
  }

  useEffect(() => { void bootstrap() }, [])

  async function applySaved(view: CrmSavedView) {
    try {
      const next = normalizeSaved(JSON.parse(view.filtersJson))
      setFilters(next)
      await run(1, next)
      setMessage(`Saved view applied: ${view.name}`)
    } catch (error) { setMessage(error instanceof Error ? error.message : String(error)) }
  }

  async function saveCurrentView() {
    if (!saveName.trim()) { setMessage('Saved view name is required'); return }
    setBusy(true)
    try {
      await saveCrmSavedView({
        module: 'Leads',
        name: saveName.trim(),
        filtersJson: JSON.stringify(filters),
        isDefault: saveDefault,
      })
      const views = await listCrmSavedViews()
      setSavedViews(views.views)
      setSaveName(''); setSaveDefault(false)
      setMessage('Current lead filters saved')
    } catch (error) { setMessage(error instanceof Error ? error.message : String(error)) }
    finally { setBusy(false) }
  }

  function patch<K extends keyof LeadFilters>(key: K, value: LeadFilters[K]) {
    setFilters(current => ({ ...current, [key]: value }))
  }

  function reset() {
    setFilters(emptyFilters)
    setPage(1)
    void run(1, emptyFilters)
  }

  return <div className="crm2-app crm-advanced-app">
    <aside className="crm2-sidebar">
      <div className="crm2-brand"><div className="crm2-brand-mark">o</div><div><strong>oRRbit</strong><span>BusinessOS</span></div></div>
      <nav className="crm2-nav">
        <a className="crm-advanced-back" href="/crm"><span>←</span>CRM Home</a>
        <a className="crm-advanced-back" href="/crm/manage"><span>☆</span>Saved Views</a>
        <a className="crm-advanced-back" href="/crm/pipeline-board"><span>◇</span>Pipeline Board</a>
      </nav>
      <div className="crm2-sidebar-foot"><strong>ADVANCED LEAD QUERY</strong><small>Server filters · pagination · saved views</small></div>
    </aside>
    <main className="crm2-main">
      <header className="crm2-topbar"><div><span className="crm2-kicker">LEAD INTELLIGENCE</span><h1>Advanced lead search & saved filters</h1></div><div className="crm2-top-actions"><button className="crm2-refresh" disabled={busy} onClick={() => void bootstrap()}>↻ Refresh</button></div></header>
      <section className="crm2-statusbar"><div><span className={busy ? 'pulse busy' : 'pulse'} />{message}</div><span>{session ? `${session.member.displayName} · ${session.canViewAllOwnedRecords ? 'Team scope' : 'My scope'}` : 'CRM'}</span></section>

      <section className="crm2-table-card" style={{ marginBottom: 18 }}>
        <div className="crm2-section-head"><div><span>FILTERS</span><h2>Find exactly the leads you need</h2></div><div className="crm2-top-actions"><button onClick={reset}>Reset</button><button className="crm2-primary" disabled={busy} onClick={() => void run(1)}>Apply filters</button></div></div>
        <div className="crm2-form-grid">
          <label>Search<input value={filters.q} onChange={e => patch('q', e.target.value)} placeholder="Business, contact, phone, email…" /></label>
          <label>Status<select value={filters.status} onChange={e => patch('status', e.target.value)}><option value="">All</option><option>New</option><option>Contacted</option><option>Qualified</option><option>Converted</option><option>Unqualified</option></select></label>
          <label>Priority<select value={filters.priority} onChange={e => patch('priority', e.target.value)}><option value="">All</option><option>Low</option><option>Normal</option><option>High</option><option>Urgent</option></select></label>
          <label>Lead source<input value={filters.leadSource} onChange={e => patch('leadSource', e.target.value)} placeholder="WhatsApp, Website, Partner…" /></label>
          <label>Product / service<input value={filters.product} onChange={e => patch('product', e.target.value)} /></label>
          <label>Tag<input value={filters.tag} onChange={e => patch('tag', e.target.value)} /></label>
          {session?.canViewAllOwnedRecords ? <label>Owner<select value={filters.ownerUserId} onChange={e => patch('ownerUserId', e.target.value)}><option value="">All owners</option>{activeTeam.map(x => <option key={x.id} value={x.id}>{x.displayName} · {x.role}</option>)}</select></label> : null}
          <label>Created from<input type="date" value={filters.createdFrom} onChange={e => patch('createdFrom', e.target.value)} /></label>
          <label>Created to<input type="date" value={filters.createdTo} onChange={e => patch('createdTo', e.target.value)} /></label>
          <label>Follow-up from<input type="date" value={filters.followUpFrom} onChange={e => patch('followUpFrom', e.target.value)} /></label>
          <label>Follow-up to<input type="date" value={filters.followUpTo} onChange={e => patch('followUpTo', e.target.value)} /></label>
          <label>Page size<select value={pageSize} onChange={e => { const size = Number(e.target.value); setPageSize(size); void run(1, filters, size) }}><option value={25}>25</option><option value={50}>50</option><option value={100}>100</option></select></label>
        </div>
      </section>

      <section className="crm-advanced-grid" style={{ marginBottom: 18 }}>
        <article className="crm2-table-card"><div className="crm2-section-head"><div><span>SAVED VIEWS</span><h2>{leadViews.length} lead view(s)</h2></div></div><div className="crm-advanced-list">{leadViews.map(view => <article key={view.id}><div><b>{view.isDefault ? 'Default' : 'Saved'}</b><strong>{view.name}</strong><small>{view.filtersJson}</small></div><button onClick={() => void applySaved(view)}>Apply</button></article>)}{!leadViews.length ? <p>No saved Lead views yet.</p> : null}</div></article>
        <article className="crm2-table-card"><div className="crm2-section-head"><div><span>SAVE CURRENT</span><h2>Reuse this filter set</h2></div></div><div className="crm-advanced-form stacked"><input value={saveName} onChange={e => setSaveName(e.target.value)} placeholder="e.g. High priority repair leads" /><label><input type="checkbox" checked={saveDefault} onChange={e => setSaveDefault(e.target.checked)} /> Default Leads view</label><button className="crm2-primary" disabled={busy} onClick={() => void saveCurrentView()}>Save current filters</button></div></article>
      </section>

      <section className="crm2-table-card">
        <div className="crm2-section-head"><div><span>RESULTS</span><h2>{total} matching leads</h2></div><div className="crm2-top-actions"><button disabled={busy || page <= 1} onClick={() => void run(page - 1)}>← Previous</button><span>Page {page} / {totalPages}</span><button disabled={busy || page >= totalPages} onClick={() => void run(page + 1)}>Next →</button></div></div>
        <div className="crm2-table-head crm2-rich-head"><span>Lead</span><span>Contact</span><span>Product</span><span>Stage</span><span>Next follow-up</span><span>Updated</span></div>
        <div className="crm2-table-body">{rows.map(row => <article className="crm2-row crm2-rich-row" key={row.id}>
          <div className="crm2-lead-name"><i>{row.title.slice(0, 1).toUpperCase()}</i><span><strong>{row.title}</strong><small>{row.leadSource || 'Direct'} · {row.priority}{row.tags.length ? ` · ${row.tags.join(', ')}` : ''}</small></span></div>
          <span><b className="crm2-cell-main">{row.contactName || '—'}</b><small>{row.mobileNumber || row.email || 'No contact'}</small></span>
          <span>{row.productInterest || '—'}</span>
          <span><em className={`crm2-stage stage-${row.status.toLowerCase()}`}>{row.status}</em></span>
          <span className="crm2-created">{when(row.nextFollowUpAtUtc)}</span>
          <span className="crm2-created">{when(row.updatedAtUtc)}</span>
        </article>)}{!rows.length ? <div className="crm2-empty"><strong>No matching leads</strong><span>Change filters or reset the view.</span></div> : null}</div>
      </section>
    </main>
  </div>
}
