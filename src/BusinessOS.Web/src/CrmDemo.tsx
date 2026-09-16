import { useEffect, useMemo, useState } from 'react'
import {
  changeCrmLeadStatus,
  createCrmLead,
  crmDashboard,
  listCrmLeads,
  type CrmDashboard,
  type CrmLead,
} from './crmApi'

const statuses = ['New', 'Contacted', 'Qualified', 'Converted', 'Unqualified']

function initialDashboard(): CrmDashboard {
  return { totalLeads: 0, new: 0, contacted: 0, qualified: 0, converted: 0, unqualified: 0, statusCounts: {} }
}

export function CrmDemo() {
  const [leads, setLeads] = useState<CrmLead[]>([])
  const [dashboard, setDashboard] = useState<CrmDashboard>(initialDashboard())
  const [title, setTitle] = useState('Repair shop lead from WhatsApp')
  const [source, setSource] = useState('WhatsApp')
  const [message, setMessage] = useState('CRM staging ready.')
  const [loading, setLoading] = useState(false)

  const pipeline = useMemo(() => statuses.map((status) => ({
    status,
    count: leads.filter((lead) => lead.status === status).length,
  })), [leads])

  async function refresh() {
    setLoading(true)
    try {
      const [leadResult, dashResult] = await Promise.all([
        listCrmLeads(),
        crmDashboard(),
      ])
      setLeads(leadResult.leads)
      setDashboard(dashResult)
      setMessage('CRM data refreshed.')
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
      setMessage('Lead added successfully.')
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
      await changeCrmLeadStatus(leadId, status, status === 'Unqualified' ? 'Not ready now' : undefined)
      setMessage(`Lead moved to ${status}.`)
      await refresh()
    } catch (error) {
      setMessage(error instanceof Error ? error.message : String(error))
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => { void refresh() }, [])

  return (
    <main className="page-shell crm-shell">
      <section className="hero-card crm-hero">
        <p className="eyebrow">oRRbit BusinessOS CRM</p>
        <h1>CRM Staging Dashboard</h1>
        <p className="hero-copy">
          Leads, follow-up status, qualification and conversion flow ko free staging API par test karein.
        </p>
        <div className="hero-actions">
          <a className="secondary-link" href="/">Checkout/Licensing</a>
          <button type="button" className="ghost" disabled={loading} onClick={refresh}>Refresh CRM</button>
          <span className="api-pill">{message}</span>
        </div>
      </section>

      <section className="crm-grid">
        <div className="panel crm-card"><span>Total Leads</span><strong>{dashboard.totalLeads}</strong></div>
        <div className="panel crm-card"><span>Contacted</span><strong>{dashboard.contacted}</strong></div>
        <div className="panel crm-card"><span>Qualified</span><strong>{dashboard.qualified}</strong></div>
        <div className="panel crm-card"><span>Converted</span><strong>{dashboard.converted}</strong></div>
      </section>

      <section className="checkout-grid">
        <div className="panel buy-panel">
          <h2>Add Lead</h2>
          <label htmlFor="lead-title">Lead / Business Name</label>
          <input id="lead-title" value={title} onChange={(event) => setTitle(event.target.value)} />
          <label htmlFor="lead-source">Lead Source</label>
          <input id="lead-source" value={source} onChange={(event) => setSource(event.target.value)} />
          <div className="button-row">
            <button type="button" disabled={loading || !title.trim()} onClick={addLead}>Add Lead</button>
          </div>
          <p className="token-help">Public free-staging CRM only. Data is temporary on free in-memory server.</p>
        </div>

        <div className="panel status-panel">
          <h2>Pipeline</h2>
          <div className="crm-pipeline">
            {pipeline.map((item) => <div key={item.status}><span>{item.status}</span><strong>{item.count}</strong></div>)}
          </div>
        </div>
      </section>

      <section className="panel crm-leads">
        <h2>Lead List</h2>
        {leads.length === 0 ? <p>No leads yet. Add first test lead.</p> : null}
        {leads.map((lead) => (
          <article className="lead-row" key={lead.id}>
            <div>
              <strong>{lead.title}</strong>
              <small>{lead.leadSource || 'No source'} · {lead.status}</small>
              {lead.unqualifiedReason ? <small>{lead.unqualifiedReason}</small> : null}
            </div>
            <select
              value={lead.status}
              onChange={(event) => moveLead(lead.id, event.target.value)}
              disabled={loading}
            >
              {statuses.map((status) => <option key={status} value={status}>{status}</option>)}
            </select>
          </article>
        ))}
      </section>
    </main>
  )
}
