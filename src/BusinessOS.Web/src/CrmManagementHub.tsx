import { useEffect, useMemo, useState } from 'react'
import './CrmDemo.css'
import './CrmAdvancedHub.css'
import {
  createCrmLead,
  getCrmSession,
  listCrmAccounts,
  listCrmLeads,
  listCrmOpportunities,
  type CrmSession,
} from './crmApi'
import {
  deleteCrmSavedView,
  listCrmAudit,
  listCrmMasters,
  listCrmSavedViews,
  saveCrmMaster,
  saveCrmSavedView,
  type CrmAuditEntry,
  type CrmMasterItem,
  type CrmSavedView,
} from './crmManagementApi'

type View = 'saved' | 'masters' | 'audit' | 'import-export'
const categories = ['LeadSource', 'Priority', 'FollowUpChannel', 'TaskType', 'OpportunityStage', 'LostReason', 'Tag', 'ProductService']

function formatDate(value: string) {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : new Intl.DateTimeFormat('en-IN', { dateStyle: 'medium', timeStyle: 'short' }).format(date)
}

function csvEscape(value: unknown) {
  const text = value == null ? '' : String(value)
  return /[",\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text
}

function downloadCsv(name: string, rows: Record<string, unknown>[]) {
  if (!rows.length) throw new Error('No records available to export.')
  const headers = Object.keys(rows[0])
  const content = [headers.join(','), ...rows.map(row => headers.map(h => csvEscape(row[h])).join(','))].join('\r\n')
  const url = URL.createObjectURL(new Blob([content], { type: 'text/csv;charset=utf-8' }))
  const link = document.createElement('a')
  link.href = url
  link.download = name
  document.body.appendChild(link)
  link.click()
  link.remove()
  URL.revokeObjectURL(url)
}

function parseCsv(text: string) {
  const rows: string[][] = []
  let row: string[] = []
  let cell = ''
  let quoted = false
  for (let i = 0; i < text.length; i += 1) {
    const ch = text[i]
    if (ch === '"') {
      if (quoted && text[i + 1] === '"') { cell += '"'; i += 1 }
      else quoted = !quoted
    } else if (ch === ',' && !quoted) {
      row.push(cell.trim()); cell = ''
    } else if ((ch === '\n' || ch === '\r') && !quoted) {
      if (ch === '\r' && text[i + 1] === '\n') i += 1
      row.push(cell.trim()); cell = ''
      if (row.some(x => x.length)) rows.push(row)
      row = []
    } else cell += ch
  }
  row.push(cell.trim())
  if (row.some(x => x.length)) rows.push(row)
  return rows
}

export function CrmManagementHub() {
  const [view, setView] = useState<View>('saved')
  const [session, setSession] = useState<CrmSession | null>(null)
  const [savedViews, setSavedViews] = useState<CrmSavedView[]>([])
  const [masters, setMasters] = useState<CrmMasterItem[]>([])
  const [audit, setAudit] = useState<CrmAuditEntry[]>([])
  const [message, setMessage] = useState('CRM management ready')
  const [busy, setBusy] = useState(false)
  const [viewName, setViewName] = useState('My Priority Leads')
  const [viewModule, setViewModule] = useState('Leads')
  const [viewFilters, setViewFilters] = useState('{"status":"Qualified","priority":"High"}')
  const [viewDefault, setViewDefault] = useState(false)
  const [masterCategory, setMasterCategory] = useState('LeadSource')
  const [masterCode, setMasterCode] = useState('')
  const [masterName, setMasterName] = useState('')
  const [masterSort, setMasterSort] = useState(100)
  const [importText, setImportText] = useState('title,leadSource,contactName,mobileNumber,email,productInterest,notes,priority\n')
  const [importResult, setImportResult] = useState('')

  const filteredMasters = useMemo(() => masters.filter(x => x.category === masterCategory), [masters, masterCategory])
  const canManageMasters = session?.member.role === 'Owner' || session?.member.role === 'Admin'

  async function refresh() {
    setBusy(true)
    try {
      const [sessionResult, viewsResult, mastersResult, auditResult] = await Promise.all([
        getCrmSession(), listCrmSavedViews(), listCrmMasters(), listCrmAudit(150),
      ])
      setSession(sessionResult)
      setSavedViews(viewsResult.views)
      setMasters(mastersResult.masters)
      setAudit(auditResult.audit)
      setMessage('Persistent CRM management data refreshed')
    } catch (error) {
      setMessage(error instanceof Error ? error.message : String(error))
    } finally { setBusy(false) }
  }

  useEffect(() => { void refresh() }, [])

  async function createView() {
    setBusy(true)
    try {
      JSON.parse(viewFilters)
      await saveCrmSavedView({ module: viewModule, name: viewName, filtersJson: viewFilters, isDefault: viewDefault })
      setMessage('Saved view persisted')
      await refresh()
    } catch (error) { setMessage(error instanceof Error ? error.message : String(error)); setBusy(false) }
  }

  async function removeView(id: string) {
    setBusy(true)
    try { await deleteCrmSavedView(id); setMessage('Saved view deleted'); await refresh() }
    catch (error) { setMessage(error instanceof Error ? error.message : String(error)); setBusy(false) }
  }

  async function createMaster() {
    if (!masterCode.trim() || !masterName.trim()) return
    setBusy(true)
    try {
      await saveCrmMaster({ category: masterCategory, code: masterCode.trim().toUpperCase(), name: masterName.trim(), active: true, sortOrder: masterSort })
      setMasterCode(''); setMasterName('')
      setMessage('CRM master saved')
      await refresh()
    } catch (error) { setMessage(error instanceof Error ? error.message : String(error)); setBusy(false) }
  }

  async function toggleMaster(item: CrmMasterItem) {
    setBusy(true)
    try {
      await saveCrmMaster({ id: item.id, category: item.category, code: item.code, name: item.name, active: !item.active, sortOrder: item.sortOrder })
      setMessage(`${item.name} ${item.active ? 'deactivated' : 'activated'}`)
      await refresh()
    } catch (error) { setMessage(error instanceof Error ? error.message : String(error)); setBusy(false) }
  }

  async function importLeads() {
    setBusy(true)
    try {
      const rows = parseCsv(importText)
      if (rows.length < 2) throw new Error('CSV requires a header and at least one data row.')
      const headers = rows[0].map(x => x.trim())
      let created = 0
      const failures: string[] = []
      for (let i = 1; i < rows.length; i += 1) {
        const values = rows[i]
        const item = Object.fromEntries(headers.map((h, index) => [h, values[index] || ''])) as Record<string, string>
        if (!item.title?.trim()) { failures.push(`Row ${i + 1}: title missing`); continue }
        try {
          await createCrmLead({
            title: item.title,
            leadSource: item.leadSource,
            contactName: item.contactName,
            mobileNumber: item.mobileNumber,
            email: item.email,
            productInterest: item.productInterest,
            notes: item.notes,
            priority: item.priority || 'Normal',
          })
          created += 1
        } catch (error) {
          failures.push(`Row ${i + 1}: ${error instanceof Error ? error.message : String(error)}`)
        }
      }
      setImportResult(`${created} lead(s) imported. ${failures.length} failed.${failures.length ? ` ${failures.slice(0, 5).join(' | ')}` : ''}`)
      setMessage('CSV lead import completed with duplicate validation')
    } catch (error) { setMessage(error instanceof Error ? error.message : String(error)) }
    finally { setBusy(false) }
  }

  async function exportLeads() {
    try {
      const result = await listCrmLeads()
      downloadCsv('businessos-crm-leads.csv', result.leads.map(x => ({
        title: x.title, status: x.status, leadSource: x.leadSource, contactName: x.contactName,
        mobileNumber: x.mobileNumber, email: x.email, productInterest: x.productInterest,
        priority: x.priority, ownerUserId: x.ownerUserId, createdAtUtc: x.createdAtUtc,
      })))
      setMessage(`${result.leads.length} leads exported`)
    } catch (error) { setMessage(error instanceof Error ? error.message : String(error)) }
  }

  async function exportAccounts() {
    try {
      const result = await listCrmAccounts()
      downloadCsv('businessos-crm-accounts.csv', result.accounts.map(x => ({
        name: x.name, legalName: x.legalName, gstin: x.gstin, displayCode: x.displayCode,
        status: x.status, primaryContact: x.primaryContact?.name, primaryMobile: x.primaryContact?.phone,
        primaryEmail: x.primaryContact?.email, contactCount: x.contacts.length,
      })))
      setMessage(`${result.accounts.length} accounts exported`)
    } catch (error) { setMessage(error instanceof Error ? error.message : String(error)) }
  }

  async function exportOpportunities() {
    try {
      const result = await listCrmOpportunities()
      downloadCsv('businessos-crm-opportunities.csv', result.opportunities.map(x => ({
        title: x.title, accountId: x.accountId, stage: x.stage, estimatedValue: x.estimatedValue,
        currencyCode: x.currencyCode, probabilityPercent: x.probabilityPercent,
        expectedCloseDate: x.expectedCloseDate, ownerUserId: x.ownerUserId, lossReason: x.lossReason,
      })))
      setMessage(`${result.opportunities.length} opportunities exported`)
    } catch (error) { setMessage(error instanceof Error ? error.message : String(error)) }
  }

  return <div className="crm2-app crm-advanced-app">
    <aside className="crm2-sidebar">
      <div className="crm2-brand"><div className="crm2-brand-mark">o</div><div><strong>oRRbit</strong><span>BusinessOS</span></div></div>
      <nav className="crm2-nav">
        <a className="crm-advanced-back" href="/crm/advanced"><span>←</span>Advanced CRM</a>
        <button className={view === 'saved' ? 'active' : ''} onClick={() => setView('saved')}><span>☆</span>Saved Views</button>
        <button className={view === 'masters' ? 'active' : ''} onClick={() => setView('masters')}><span>⚙</span>CRM Masters</button>
        <button className={view === 'audit' ? 'active' : ''} onClick={() => setView('audit')}><span>≡</span>Audit Trail</button>
        <button className={view === 'import-export' ? 'active' : ''} onClick={() => setView('import-export')}><span>⇅</span>Import / Export</button>
      </nav>
      <div className="crm2-sidebar-foot"><strong>PERSISTENT CRM CONTROL</strong><small>Saved Views · Masters · Audit · Data portability</small></div>
    </aside>
    <main className="crm2-main">
      <header className="crm2-topbar"><div><span className="crm2-kicker">BUSINESSOS CRM MANAGEMENT</span><h1>{view === 'saved' ? 'Saved views & filters' : view === 'masters' ? 'CRM masters & settings' : view === 'audit' ? 'CRM audit trail' : 'Import & export'}</h1></div><div className="crm2-top-actions"><button className="crm2-refresh" disabled={busy} onClick={() => void refresh()}>↻ Refresh</button></div></header>
      <section className="crm2-statusbar"><div><span className={busy ? 'pulse busy' : 'pulse'} />{message}</div><span>{session ? `${session.member.displayName} · ${session.member.role}` : 'CRM user'} · Postgres ready</span></section>

      {view === 'saved' ? <section className="crm-advanced-grid">
        <article className="crm2-table-card"><div className="crm2-section-head"><div><span>NEW VIEW</span><h2>Save filters</h2></div></div><div className="crm-advanced-form stacked"><select value={viewModule} onChange={e => setViewModule(e.target.value)}><option>Leads</option><option>Accounts</option><option>Opportunities</option><option>FollowUps</option><option>Tasks</option></select><input value={viewName} onChange={e => setViewName(e.target.value)} placeholder="View name"/><textarea rows={6} value={viewFilters} onChange={e => setViewFilters(e.target.value)} /><label><input type="checkbox" checked={viewDefault} onChange={e => setViewDefault(e.target.checked)}/> Default view for this module</label><button className="crm2-primary" onClick={() => void createView()}>Save View</button></div></article>
        <article className="crm2-table-card"><div className="crm2-section-head"><div><span>PERSISTED</span><h2>{savedViews.length} saved view(s)</h2></div></div><div className="crm-advanced-list">{savedViews.map(item => <article key={item.id}><div><b>{item.module}{item.isDefault ? ' · Default' : ''}</b><strong>{item.name}</strong><small>{item.filtersJson}</small></div><button onClick={() => void removeView(item.id)}>Delete</button></article>)}{!savedViews.length ? <p>No saved views yet.</p> : null}</div></article>
      </section> : null}

      {view === 'masters' ? <section className="crm-advanced-grid">
        <article className="crm2-table-card"><div className="crm2-section-head"><div><span>SETTINGS</span><h2>Master categories</h2></div></div><div className="crm-advanced-form stacked"><select value={masterCategory} onChange={e => setMasterCategory(e.target.value)}>{categories.map(x => <option key={x}>{x}</option>)}</select><input value={masterCode} onChange={e => setMasterCode(e.target.value)} placeholder="Code"/><input value={masterName} onChange={e => setMasterName(e.target.value)} placeholder="Display name"/><input type="number" value={masterSort} onChange={e => setMasterSort(Number(e.target.value))}/><button className="crm2-primary" disabled={!canManageMasters} onClick={() => void createMaster()}>Add Master</button>{!canManageMasters ? <small>Owner/Admin required to change masters.</small> : null}</div></article>
        <article className="crm2-table-card"><div className="crm2-section-head"><div><span>{masterCategory.toUpperCase()}</span><h2>{filteredMasters.length} item(s)</h2></div></div><div className="crm-advanced-list">{filteredMasters.map(item => <article key={item.id}><div><b>{item.code}</b><strong>{item.name}</strong><small>Sort {item.sortOrder} · {item.active ? 'Active' : 'Inactive'}</small></div>{canManageMasters ? <button onClick={() => void toggleMaster(item)}>{item.active ? 'Deactivate' : 'Activate'}</button> : null}</article>)}</div></article>
      </section> : null}

      {view === 'audit' ? <section className="crm2-table-card"><div className="crm2-section-head"><div><span>IMMUTABLE HISTORY</span><h2>{audit.length} recent CRM events</h2></div></div><div className="crm-advanced-list">{audit.map(item => <article key={item.id}><div><b>{item.action}</b><strong>{item.entityType}{item.entityId ? ` · ${item.entityId}` : ''}</strong><small>{item.detail || 'No additional detail'} · Actor {item.actorUserId || 'System'}</small></div><span>{formatDate(item.createdAtUtc)}</span></article>)}{!audit.length ? <p>No audit events recorded yet.</p> : null}</div></section> : null}

      {view === 'import-export' ? <section className="crm-advanced-grid">
        <article className="crm2-table-card"><div className="crm2-section-head"><div><span>CSV IMPORT</span><h2>Import leads</h2></div></div><textarea style={{ width: '100%', minHeight: 260 }} value={importText} onChange={e => setImportText(e.target.value)} /><div className="crm2-top-actions" style={{ marginTop: 14 }}><label className="crm2-refresh">Load CSV file<input hidden type="file" accept=".csv,text/csv" onChange={async e => { const file=e.target.files?.[0]; if(file)setImportText(await file.text()) }}/></label><button className="crm2-primary" onClick={() => void importLeads()}>Import Leads</button></div>{importResult ? <p>{importResult}</p> : null}<small>Duplicate mobile/email protection is enforced by the CRM API during import.</small></article>
        <article className="crm2-table-card"><div className="crm2-section-head"><div><span>DATA PORTABILITY</span><h2>Filtered business exports</h2></div></div><div className="crm-advanced-list"><article><div><b>Leads</b><strong>Lead master export</strong><small>Contact, owner, source, status, priority and product</small></div><button onClick={() => void exportLeads()}>Export CSV</button></article><article><div><b>Accounts</b><strong>Customer accounts export</strong><small>GST, contacts and account status</small></div><button onClick={() => void exportAccounts()}>Export CSV</button></article><article><div><b>Opportunities</b><strong>Pipeline export</strong><small>Stage, forecast, owner and probability</small></div><button onClick={() => void exportOpportunities()}>Export CSV</button></article></div></article>
      </section> : null}
    </main>
  </div>
}
