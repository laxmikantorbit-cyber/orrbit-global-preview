import { useEffect, useState } from 'react'
import './CrmDemo.css'
import './CrmAdvancedHub.css'
import {
  getCrmSession,
  listCrmAccounts,
  listCrmFollowUps,
  listCrmLeads,
  listCrmOpportunities,
  listCrmTasks,
} from './crmApi'

type Dataset = 'leads' | 'accounts' | 'opportunities' | 'followups' | 'tasks'
type Row = Record<string, string | number | boolean | null | undefined>

function esc(value: unknown) {
  const text = value == null ? '' : String(value)
  return /[",\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text
}

function filtered(rows: Row[], query: string) {
  const q = query.trim().toLowerCase()
  if (!q) return rows
  return rows.filter(row => Object.values(row).some(value => String(value ?? '').toLowerCase().includes(q)))
}

function downloadCsv(name: string, rows: Row[]) {
  if (!rows.length) throw new Error('No records available for export.')
  const headers = Object.keys(rows[0])
  const content = [headers.join(','), ...rows.map(row => headers.map(h => esc(row[h])).join(','))].join('\r\n')
  const href = URL.createObjectURL(new Blob([content], { type: 'text/csv;charset=utf-8' }))
  const link = document.createElement('a'); link.href = href; link.download = `${name}.csv`; link.click(); URL.revokeObjectURL(href)
}

function html(value: unknown) {
  return String(value ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
}

function downloadExcel(name: string, rows: Row[]) {
  if (!rows.length) throw new Error('No records available for export.')
  const headers = Object.keys(rows[0])
  const table = `<table><thead><tr>${headers.map(x => `<th>${html(x)}</th>`).join('')}</tr></thead><tbody>${rows.map(row => `<tr>${headers.map(h => `<td>${html(row[h])}</td>`).join('')}</tr>`).join('')}</tbody></table>`
  const documentHtml = `<html><head><meta charset="utf-8"></head><body>${table}</body></html>`
  const href = URL.createObjectURL(new Blob([documentHtml], { type: 'application/vnd.ms-excel;charset=utf-8' }))
  const link = document.createElement('a'); link.href = href; link.download = `${name}.xls`; link.click(); URL.revokeObjectURL(href)
}

async function load(dataset: Dataset): Promise<Row[]> {
  if (dataset === 'leads') {
    const x = await listCrmLeads()
    return x.leads.map(v => ({ id: v.id, title: v.title, status: v.status, priority: v.priority, leadSource: v.leadSource, contactName: v.contactName, mobile: v.mobileNumber, email: v.email, product: v.productInterest, ownerUserId: v.ownerUserId, nextFollowUp: v.nextFollowUpAtUtc, createdAt: v.createdAtUtc }))
  }
  if (dataset === 'accounts') {
    const x = await listCrmAccounts()
    return x.accounts.map(v => ({ id: v.id, name: v.name, legalName: v.legalName, gstin: v.gstin, displayCode: v.displayCode, status: v.status, primaryContact: v.primaryContact?.name, primaryMobile: v.primaryContact?.phone, primaryEmail: v.primaryContact?.email, contacts: v.contacts.length }))
  }
  if (dataset === 'opportunities') {
    const x = await listCrmOpportunities()
    return x.opportunities.map(v => ({ id: v.id, title: v.title, accountId: v.accountId, stage: v.stage, estimatedValue: v.estimatedValue, currency: v.currencyCode, probability: v.probabilityPercent, expectedCloseDate: v.expectedCloseDate, ownerUserId: v.ownerUserId, lostReason: v.lossReason }))
  }
  if (dataset === 'followups') {
    const x = await listCrmFollowUps()
    return x.followUps.map(v => ({ id: v.id, leadId: v.leadId, channel: v.channel, purpose: v.purpose, dueAt: v.dueAtUtc, ownerUserId: v.ownerUserId, status: v.status, outcome: v.outcome, createdAt: v.createdAtUtc, completedAt: v.completedAtUtc }))
  }
  const x = await listCrmTasks()
  return x.tasks.map(v => ({ id: v.id, leadId: v.leadId, title: v.title, details: v.details, dueAt: v.dueAtUtc, priority: v.priority, assigneeUserId: v.assigneeUserId, status: v.status, createdAt: v.createdAtUtc, completedAt: v.completedAtUtc }))
}

export function CrmDataExportHub() {
  const [dataset, setDataset] = useState<Dataset>('leads')
  const [query, setQuery] = useState('')
  const [busy, setBusy] = useState(false)
  const [canExport, setCanExport] = useState(false)
  const [message, setMessage] = useState('Checking export permission...')

  useEffect(() => {
    void getCrmSession().then(session => {
      const allowed = session.member.permissions.includes('ExportData')
      setCanExport(allowed)
      setMessage(allowed ? 'CSV / Excel export ready' : 'ExportData permission is required.')
    }).catch(error => setMessage(error instanceof Error ? error.message : String(error)))
  }, [])

  async function run(format: 'csv' | 'excel') {
    if (!canExport) { setMessage('ExportData permission is required.'); return }
    setBusy(true)
    try {
      const rows = filtered(await load(dataset), query)
      const name = `businessos-crm-${dataset}`
      if (format === 'csv') downloadCsv(name, rows); else downloadExcel(name, rows)
      setMessage(`${rows.length} ${dataset} record(s) exported to ${format === 'csv' ? 'CSV' : 'Excel'}`)
    } catch (error) { setMessage(error instanceof Error ? error.message : String(error)) }
    finally { setBusy(false) }
  }

  return <div className="crm2-app crm-advanced-app">
    <aside className="crm2-sidebar"><div className="crm2-brand"><div className="crm2-brand-mark">o</div><div><strong>oRRbit</strong><span>BusinessOS</span></div></div><nav className="crm2-nav"><a className="crm-advanced-back" href="/crm/manage"><span>←</span>CRM Management</a><a className="active" href="/crm/export"><span>⇩</span>Data Export</a></nav><div className="crm2-sidebar-foot"><strong>DATA PORTABILITY</strong><small>CSV · Excel · filtered export · permission controlled</small></div></aside>
    <main className="crm2-main">
      <header className="crm2-topbar"><div><span className="crm2-kicker">CRM IMPORT / EXPORT</span><h1>Business data export</h1></div></header>
      <section className="crm2-statusbar"><div><span className={busy ? 'pulse busy' : 'pulse'} />{message}</div><span>Role-scoped data only</span></section>
      <section className="crm-advanced-grid">
        <article className="crm2-table-card"><div className="crm2-section-head"><div><span>DATASET</span><h2>Select records</h2></div></div><div className="crm-advanced-form stacked"><select value={dataset} onChange={e => setDataset(e.target.value as Dataset)}><option value="leads">Leads</option><option value="accounts">Accounts / Customers</option><option value="opportunities">Opportunities / Deals</option><option value="followups">Follow-ups</option><option value="tasks">Tasks</option></select><input value={query} onChange={e => setQuery(e.target.value)} placeholder="Optional filter: status, owner, product, name..."/><small>Filter matches any visible field before export.</small></div></article>
        <article className="crm2-table-card"><div className="crm2-section-head"><div><span>FORMAT</span><h2>Download export</h2></div></div><div className="crm-advanced-form stacked"><button className="crm2-primary" disabled={busy || !canExport} onClick={() => void run('csv')}>Export CSV</button><button className="crm2-primary" disabled={busy || !canExport} onClick={() => void run('excel')}>Export Excel (.xls)</button><small>Requires ExportData permission. Excel export opens directly in Microsoft Excel.</small></div></article>
      </section>
    </main>
  </div>
}
