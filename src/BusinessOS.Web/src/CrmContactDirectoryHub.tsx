import { useEffect, useMemo, useState } from 'react'
import './CrmDemo.css'
import './CrmAdvancedHub.css'
import { listCrmContactDirectory, updateCrmContactDesignation, type CrmContactDirectoryItem } from './crmContactsApi'

export function CrmContactDirectoryHub() {
  const [contacts, setContacts] = useState<CrmContactDirectoryItem[]>([])
  const [query, setQuery] = useState('')
  const [message, setMessage] = useState('Contact directory ready')
  const [busy, setBusy] = useState(false)
  const [editing, setEditing] = useState<string | null>(null)
  const [designation, setDesignation] = useState('')

  async function refresh() {
    setBusy(true)
    try {
      const result = await listCrmContactDirectory()
      setContacts(result.contacts)
      setMessage(`${result.contacts.length} contact(s) loaded`)
    } catch (error) { setMessage(error instanceof Error ? error.message : String(error)) }
    finally { setBusy(false) }
  }

  useEffect(() => { void refresh() }, [])

  const visible = useMemo(() => {
    const q = query.trim().toLowerCase()
    if (!q) return contacts
    return contacts.filter(x => [x.accountName, x.name, x.designation, x.email, x.phone].some(v => String(v ?? '').toLowerCase().includes(q)))
  }, [contacts, query])

  async function save(item: CrmContactDirectoryItem) {
    setBusy(true)
    try {
      await updateCrmContactDesignation(item.accountId, item.contactId, designation)
      setEditing(null)
      setDesignation('')
      await refresh()
      setMessage('Contact designation updated')
    } catch (error) { setMessage(error instanceof Error ? error.message : String(error)); setBusy(false) }
  }

  return <div className="crm2-app crm-advanced-app">
    <aside className="crm2-sidebar">
      <div className="crm2-brand"><div className="crm2-brand-mark">o</div><div><strong>oRRbit</strong><span>BusinessOS</span></div></div>
      <nav className="crm2-nav"><a className="crm-advanced-back" href="/crm"><span>←</span>CRM Home</a><a className="active" href="/crm/contacts"><span>◎</span>Contact Directory</a><a href="/crm/communications"><span>✉</span>Communications</a><a href="/crm/addresses"><span>⌂</span>Customer Addresses</a></nav>
      <div className="crm2-sidebar-foot"><strong>CONTACTS</strong><small>Account · Designation · Email · Mobile</small></div>
    </aside>
    <main className="crm2-main">
      <header className="crm2-topbar"><div><span className="crm2-kicker">CRM CONTACT MANAGEMENT</span><h1>Contact directory</h1></div><div className="crm2-top-actions"><button className="crm2-refresh" disabled={busy} onClick={() => void refresh()}>↻ Refresh</button></div></header>
      <section className="crm2-statusbar"><div><span className={busy ? 'pulse busy' : 'pulse'} />{message}</div><span>Role-scoped customer contacts</span></section>
      <section className="crm2-table-card">
        <div className="crm2-section-head"><div><span>DIRECTORY</span><h2>{visible.length} visible contact(s)</h2></div><input value={query} onChange={e => setQuery(e.target.value)} placeholder="Search account, contact, designation, mobile..." /></div>
        <div className="crm-advanced-list">
          {visible.map(item => <article key={item.contactId}>
            <div><b>{item.accountName}{item.isPrimary ? ' · Primary' : ''}</b><strong>{item.name}</strong><small>{item.designation || 'Designation not set'} · {item.phone || 'No mobile'} · {item.email || 'No email'}</small></div>
            {editing === item.contactId ? <div className="crm2-top-actions"><input value={designation} onChange={e => setDesignation(e.target.value)} placeholder="Designation"/><button className="crm2-primary" disabled={busy} onClick={() => void save(item)}>Save</button><button onClick={() => setEditing(null)}>Cancel</button></div> : <button onClick={() => { setEditing(item.contactId); setDesignation(item.designation || '') }}>Edit designation</button>}
          </article>)}
          {!visible.length ? <p>No matching contacts.</p> : null}
        </div>
      </section>
    </main>
  </div>
}
