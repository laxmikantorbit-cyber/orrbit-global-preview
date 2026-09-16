import { useEffect, useMemo, useState } from 'react'
import './CrmDemo.css'
import './CrmAdvancedHub.css'
import { changeCrmLeadStatus, listCrmLeads, type CrmLead } from './crmApi'

const stages = ['New', 'Contacted', 'Qualified', 'Unqualified', 'Converted'] as const
const movableStages = new Set(['New', 'Contacted', 'Qualified', 'Unqualified'])
const labels: Record<string, string> = {
  New: 'New', Contacted: 'Contacted', Qualified: 'Qualified', Unqualified: 'Unqualified', Converted: 'Converted',
}

export function CrmPipelineBoard() {
  const [leads, setLeads] = useState<CrmLead[]>([])
  const [busyLeadId, setBusyLeadId] = useState<string | null>(null)
  const [dragLeadId, setDragLeadId] = useState<string | null>(null)
  const [message, setMessage] = useState('Drag open leads between pipeline stages')

  async function refresh() {
    try {
      const result = await listCrmLeads()
      setLeads(result.leads)
    } catch (error) { setMessage(error instanceof Error ? error.message : String(error)) }
  }

  useEffect(() => { void refresh() }, [])

  const grouped = useMemo(() => Object.fromEntries(stages.map(stage => [stage, leads.filter(x => x.status === stage)])) as Record<string, CrmLead[]>, [leads])

  async function move(leadId: string, target: string) {
    const lead = leads.find(x => x.id === leadId)
    if (!lead || lead.status === target) return
    if (lead.status === 'Converted') { setMessage('Converted leads are locked.'); return }
    if (!movableStages.has(target)) { setMessage('Use the proper conversion workflow to move a lead to Converted.'); return }
    setBusyLeadId(leadId)
    try {
      await changeCrmLeadStatus(leadId, target, target === 'Unqualified' ? 'Moved to unqualified from pipeline board' : undefined)
      setMessage(`${lead.title} moved from ${lead.status} to ${target}`)
      await refresh()
    } catch (error) { setMessage(error instanceof Error ? error.message : String(error)) }
    finally { setBusyLeadId(null); setDragLeadId(null) }
  }

  return <div className="crm2-app crm-advanced-app">
    <aside className="crm2-sidebar">
      <div className="crm2-brand"><div className="crm2-brand-mark">o</div><div><strong>oRRbit</strong><span>BusinessOS</span></div></div>
      <nav className="crm2-nav">
        <a className="crm-advanced-back" href="/crm"><span>←</span>CRM Home</a>
        <a className="crm-advanced-back" href="/crm/advanced"><span>↗</span>Advanced CRM</a>
        <a className="crm-advanced-back" href="/crm/maintenance"><span>✓</span>Data Maintenance</a>
      </nav>
      <div className="crm2-sidebar-foot"><strong>SALES PIPELINE</strong><small>Drag & drop with lifecycle safeguards</small></div>
    </aside>
    <main className="crm2-main">
      <header className="crm2-topbar"><div><span className="crm2-kicker">PIPELINE WORKSPACE</span><h1>Drag & drop sales funnel</h1></div><div className="crm2-top-actions"><button className="crm2-refresh" onClick={() => void refresh()}>↻ Refresh</button></div></header>
      <section className="crm2-statusbar"><div><span className={busyLeadId ? 'pulse busy' : 'pulse'} />{message}</div><span>Converted is protected · use Lead Conversion workflow</span></section>
      <section className="crm2-kanban-wrap" style={{ overflowX: 'auto' }}>
        {stages.map(stage => {
          const items = grouped[stage] || []
          const droppable = movableStages.has(stage)
          return <div
            className="crm2-kanban-column"
            key={stage}
            onDragOver={e => { if (droppable) e.preventDefault() }}
            onDrop={e => { e.preventDefault(); const id = e.dataTransfer.getData('text/crm-lead') || dragLeadId; if (id && droppable) void move(id, stage) }}
            style={{ minWidth: 260, outline: dragLeadId && droppable ? '1px dashed rgba(29,78,216,.35)' : undefined }}
          >
            <header><span>{labels[stage]}</span><b>{items.length}</b></header>
            <div className="crm2-kanban-stack">
              {!items.length ? <p>{stage === 'Converted' ? 'Converted leads appear here after customer/opportunity conversion.' : 'Drop lead here'}</p> : items.map(lead => <article
                key={lead.id}
                draggable={lead.status !== 'Converted' && busyLeadId !== lead.id}
                onDragStart={e => { e.dataTransfer.effectAllowed = 'move'; e.dataTransfer.setData('text/crm-lead', lead.id); setDragLeadId(lead.id) }}
                onDragEnd={() => setDragLeadId(null)}
                style={{ cursor: lead.status === 'Converted' ? 'default' : 'grab', opacity: busyLeadId === lead.id ? .55 : 1 }}
              >
                <div><strong>{lead.title}</strong><em>{lead.priority || 'Normal'}</em></div>
                <span>{lead.contactName || lead.mobileNumber || lead.email || lead.leadSource || 'Direct lead'}</span>
                <small>{lead.productInterest || 'No product selected'}</small>
                {lead.status === 'Converted' ? <small>Customer & opportunity created · locked</small> : <small>Drag to change stage</small>}
              </article>)}
            </div>
          </div>
        })}
      </section>
    </main>
  </div>
}
