import { useEffect, useMemo, useState } from 'react'
import './CrmDemo.css'
import {
  changeCrmLeadStatus,
  createCrmLead,
  crmDashboard,
  listCrmLeads,
  type CrmDashboard,
  type CrmLead,
} from './crmApi'

const statuses = ['New', 'Contacted', 'Qualified', 'Converted', 'Unqualified']

const statusLabels: Record<string, string> = {
  New: 'New lead',
  Contacted: 'Contacted',
  Qualified: 'Qualified',
  Converted: 'Converted',
  Unqualified: 'Unqualified',
}

function initialDashboard(): CrmDashboard {
  return { totalLeads: 0, new: 0, contacted: 0, qualified: 0, converted: 0, unqualified: 0, statusCounts: {} }
}
function formatCreated(value: string) {
  const parsed = new Date(value)
  if (Number.isNaN(parsed.getTime())) return value || '—'
  return new Intl.DateTimeFormat('en-IN', {
    day: '2-digit',
    month: 'short',
    hour: '2-digit',
    minute: '2-digit',
  }).format(parsed)
}

export function CrmDemo() {
  const [leads, setLeads] = useState<CrmLead[]>([])
  const [dashboard, setDashboard] = useState<CrmDashboard>(initialDashboard())
  const [title, setTitle] = useState('')
  const [source, setSource] = useState('WhatsApp')
  const [message, setMessage] = useState('CRM staging ready')
  const [loading, setLoading] = useState(false)
  const [query, setQuery] = useState('')
  const [statusFilter, setStatusFilter] = useState('All')
  const [showAddLead, setShowAddLead] = useState(false)
  const [selectedLeadId, setSelectedLeadId] = useState<string | null>(null)
  const pipeline = useMemo(() => statuses.map((status) => ({
    status,
    count: leads.filter((lead) => lead.status === status).length,
  })), [leads])

  const filteredLeads = useMemo(() => {
    const search = query.trim().toLowerCase()
    return leads.filter((lead) => {
      const matchesStatus = statusFilter === 'All' || lead.status === statusFilter
      const matchesSearch = !search ||
        lead.title.toLowerCase().includes(search) ||
        (lead.leadSource || '').toLowerCase().includes(search)
      return matchesStatus && matchesSearch
    })
  }, [leads, query, statusFilter])

  const selectedLead = useMemo(
    () => leads.find((lead) => lead.id === selectedLeadId) ?? null,
    [leads, selectedLeadId],
  )

  const conversionRate = dashboard.totalLeads > 0
    ? Math.round((dashboard.converted / dashboard.totalLeads) * 100)
    : 0
  async function refresh() {
    setLoading(true)
    try {
      const [leadResult, dashResult] = await Promise.all([
        listCrmLeads(),
        crmDashboard(),
      ])
      setLeads(leadResult.leads)
      setDashboard(dashResult)
      setMessage('Live data refreshed')
    } catch (error) {
      setMessage(error instanceof Error ? error.message : String(error))
    } finally {
      setLoading(false)
    }
  }

  async function addLead() {
    if (!title.trim()) return
    setLoading(true)
    try {
      await createCrmLead({ title: title.trim(), leadSource: source.trim() || undefined })
      setTitle('')
      setShowAddLead(false)
      setMessage('New lead added')
      await refresh()
    } catch (error) {
      setMessage(error instanceof Error ? error.message : String(error))
    } finally {
      setLoading(false)
    }
  }

  async function moveLead(leadId: string, status: string) {
    setLoading(true)
    try {
      await changeCrmLeadStatus(
        leadId,
        status,
        status === 'Unqualified' ? 'Not ready now' : undefined,
      )
      setMessage(`Lead moved to ${status}`)
      await refresh()
    } catch (error) {
      setMessage(error instanceof Error ? error.message : String(error))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { void refresh() }, [])
  return (
    <div className="crm2-app">
      <aside className="crm2-sidebar">
        <div className="crm2-brand">
          <div className="crm2-brand-mark">o</div>
          <div><strong>oRRbit</strong><span>BusinessOS CRM</span></div>
        </div>
        <nav className="crm2-nav" aria-label="CRM navigation">
          <button className="active"><span>⌂</span>Overview</button>
          <button><span>◎</span>Leads <b>{dashboard.totalLeads}</b></button>
          <button><span>◇</span>Pipeline</button>
          <button><span>✓</span>Follow-ups</button>
          <button><span>↗</span>Reports</button>
        </nav>
        <div className="crm2-sidebar-foot">
          <a href="/">Licensing & Checkout</a>
          <small>Free staging workspace</small>
        </div>
      </aside>

      <main className="crm2-main">
        <header className="crm2-topbar">
          <div>
            <span className="crm2-kicker">CRM COMMAND CENTRE</span>
            <h1>Sales overview</h1>
          </div>
          <div className="crm2-top-actions">
            <button className="crm2-refresh" disabled={loading} onClick={refresh}>↻ Refresh</button>
            <button className="crm2-primary" onClick={() => setShowAddLead(true)}>＋ Add lead</button>
          </div>
        </header>

        <section className="crm2-statusbar">
          <div><span className={loading ? 'pulse busy' : 'pulse'} />{message}</div>
          <span>Live staging API</span>
        </section>

        <section className="crm2-metrics">
          <article><span>Total leads</span><strong>{dashboard.totalLeads}</strong><small>All active records</small></article>
          <article><span>Contacted</span><strong>{dashboard.contacted}</strong><small>Conversation started</small></article>
          <article><span>Qualified</span><strong>{dashboard.qualified}</strong><small>Sales-ready opportunities</small></article>
          <article className="accent"><span>Conversion</span><strong>{conversionRate}%</strong><small>{dashboard.converted} converted leads</small></article>
        </section>

        <section className="crm2-pipeline-card">
          <div className="crm2-section-head">
            <div><span>PIPELINE HEALTH</span><h2>Lead movement</h2></div>
            <button onClick={() => setStatusFilter('All')}>View all leads</button>
          </div>
          <div className="crm2-pipeline">
            {pipeline.map((item) => {
              const width = dashboard.totalLeads > 0 ? Math.max(8, Math.round((item.count / dashboard.totalLeads) * 100)) : 8
              return (
                <button key={item.status} onClick={() => setStatusFilter(item.status)} className={statusFilter === item.status ? 'selected' : ''}>
                  <div><span>{statusLabels[item.status]}</span><strong>{item.count}</strong></div>
                  <i><b style={{ width: `${width}%` }} /></i>
                </button>
              )
            })}
          </div>
        </section>

        <section className="crm2-workspace">
          <div className="crm2-table-card">
            <div className="crm2-table-tools">
              <div>
                <span className="crm2-kicker">LEAD WORKSPACE</span>
                <h2>All leads</h2>
              </div>
              <div className="crm2-filters">
                <input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Search leads or source…" />
                <select value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)}>
                  <option value="All">All stages</option>
                  {statuses.map((status) => <option key={status}>{status}</option>)}
                </select>
              </div>
            </div>
            <div className="crm2-table-head">
              <span>Lead</span><span>Source</span><span>Stage</span><span>Created</span><span />
            </div>
            <div className="crm2-table-body">
              {filteredLeads.length === 0 ? (
                <div className="crm2-empty"><strong>No matching leads</strong><span>Try another filter or add a new lead.</span></div>
              ) : filteredLeads.map((lead) => (
                <article className="crm2-row" key={lead.id} onClick={() => setSelectedLeadId(lead.id)}>
                  <div className="crm2-lead-name"><i>{lead.title.slice(0, 1).toUpperCase()}</i><span><strong>{lead.title}</strong><small>{lead.id.slice(0, 8)}</small></span></div>
                  <span>{lead.leadSource || 'Direct'}</span>
                  <span><em className={`crm2-stage stage-${lead.status.toLowerCase()}`}>{statusLabels[lead.status] || lead.status}</em></span>
                  <span className="crm2-created">{formatCreated(lead.createdSort)}</span>
                  <button className="crm2-more" aria-label={`Open ${lead.title}`}>›</button>
                </article>
              ))}
            </div>
            <div className="crm2-table-foot">Showing {filteredLeads.length} of {leads.length} leads</div>
          </div>

          <aside className="crm2-insight-card">
            <span className="crm2-kicker">TODAY'S FOCUS</span>
            <h2>Pipeline snapshot</h2>
            <div className="crm2-insight-number"><strong>{dashboard.qualified}</strong><span>qualified leads</span></div>
            <p>Move contacted leads forward after qualification and keep unqualified records clearly separated.</p>
            <div className="crm2-mini-stats"><div><span>New</span><b>{dashboard.new}</b></div><div><span>Contacted</span><b>{dashboard.contacted}</b></div><div><span>Won</span><b>{dashboard.converted}</b></div></div>
          </aside>
        </section>
        {showAddLead ? (
          <div className="crm2-overlay" onMouseDown={() => setShowAddLead(false)}>
            <section className="crm2-drawer" onMouseDown={(e) => e.stopPropagation()}>
              <div className="crm2-drawer-head"><div><span className="crm2-kicker">NEW OPPORTUNITY</span><h2>Add lead</h2></div><button onClick={() => setShowAddLead(false)}>×</button></div>
              <label>Lead / business name<input autoFocus value={title} onChange={(e) => setTitle(e.target.value)} placeholder="e.g. Sharma Mobile Care" /></label>
              <label>Lead source<select value={source} onChange={(e) => setSource(e.target.value)}><option>WhatsApp</option><option>Website</option><option>Referral</option><option>Partner</option><option>Calling</option><option>Other</option></select></label>
              <div className="crm2-drawer-note"><strong>Starts in New</strong><span>You can move the lead through the pipeline after creation.</span></div>
              <div className="crm2-drawer-actions"><button className="crm2-cancel" onClick={() => setShowAddLead(false)}>Cancel</button><button className="crm2-primary" disabled={loading || !title.trim()} onClick={addLead}>{loading ? 'Adding…' : 'Add lead'}</button></div>
            </section>
          </div>
        ) : null}

        {selectedLead ? (
          <div className="crm2-overlay" onMouseDown={() => setSelectedLeadId(null)}>
            <section className="crm2-drawer crm2-detail" onMouseDown={(e) => e.stopPropagation()}>
              <div className="crm2-drawer-head"><div><span className="crm2-kicker">LEAD DETAILS</span><h2>{selectedLead.title}</h2></div><button onClick={() => setSelectedLeadId(null)}>×</button></div>
              <div className="crm2-detail-grid"><div><span>Source</span><strong>{selectedLead.leadSource || 'Direct'}</strong></div><div><span>Created</span><strong>{formatCreated(selectedLead.createdSort)}</strong></div><div><span>Lead ID</span><strong>{selectedLead.id.slice(0, 12)}</strong></div><div><span>Current stage</span><strong>{statusLabels[selectedLead.status]}</strong></div></div>
              <label>Move to stage<select value={selectedLead.status} disabled={loading} onChange={(e) => moveLead(selectedLead.id, e.target.value)}>{statuses.map((status) => <option key={status}>{status}</option>)}</select></label>
              {selectedLead.unqualifiedReason ? <div className="crm2-reason"><span>Reason</span><strong>{selectedLead.unqualifiedReason}</strong></div> : null}
            </section>
          </div>
        ) : null}
      </main>
    </div>
  )
}
