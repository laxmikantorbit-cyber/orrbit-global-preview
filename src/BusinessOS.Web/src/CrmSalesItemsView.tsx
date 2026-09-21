import { useMemo, useState } from 'react'
import {
  createCrmSalesItem,
  updateCrmSalesItem,
  type CrmSalesItem,
} from './crmApi'

type Props = {
  items: CrmSalesItem[]
  busy: boolean
  refresh: () => Promise<void>
  notify: (message: string) => void
  canManageSales: boolean
}

function money(value: number) {
  return new Intl.NumberFormat('en-IN', {
    style: 'currency', currency: 'INR', maximumFractionDigits: 2,
  }).format(value)
}

export function CrmSalesItemsView({ items, busy, refresh, notify, canManageSales }: Props) {
  const [query, setQuery] = useState('')
  const [status, setStatus] = useState<'All' | 'Active' | 'Inactive'>('All')
  const [editingId, setEditingId] = useState<string | null>(null)
  const [open, setOpen] = useState(false)
  const [code, setCode] = useState('')
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [rate, setRate] = useState('0')
  const [tax, setTax] = useState('18')
  const [itemStatus, setItemStatus] = useState<'Active' | 'Inactive'>('Active')

  const filtered = useMemo(() => {
    const search = query.trim().toLowerCase()
    return items.filter((item) =>
      (status === 'All' || item.status === status) &&
      (!search || [item.code, item.name, item.description].filter(Boolean).join(' ').toLowerCase().includes(search)))
  }, [items, query, status])

  function beginNew() {
    setEditingId(null); setCode(''); setName(''); setDescription('')
    setRate('0'); setTax('18'); setItemStatus('Active'); setOpen(true)
  }

  function beginEdit(item: CrmSalesItem) {
    setEditingId(item.id); setCode(item.code); setName(item.name)
    setDescription(item.description || ''); setRate(String(item.defaultRate))
    setTax(String(item.defaultTaxPercent)); setItemStatus(item.status); setOpen(true)
  }

  async function save() {
    if (!name.trim() || (!editingId && !code.trim())) {
      notify('Item code and name are required'); return
    }
    try {
      if (editingId) {
        await updateCrmSalesItem(editingId, {
          name: name.trim(), description: description.trim() || undefined,
          defaultRate: Math.max(0, Number(rate) || 0),
          defaultTaxPercent: Math.max(0, Number(tax) || 0), status: itemStatus,
        })
        notify('Sales item updated')
      } else {
        await createCrmSalesItem({
          code: code.trim(), name: name.trim(), description: description.trim() || undefined,
          defaultRate: Math.max(0, Number(rate) || 0),
          defaultTaxPercent: Math.max(0, Number(tax) || 0), status: itemStatus,
        })
        notify('Sales item created')
      }
      setOpen(false); await refresh()
    } catch (error) {
      notify(error instanceof Error ? error.message : String(error))
    }
  }

  return <section className="crm2-ref-list-page">
    <div className="crm2-ref-action-row">
      {canManageSales ? <button className="crm2-ref-primary" onClick={beginNew}>+ Add Item</button> : null}
    </div>
    <section className="crm2-ref-filter-card">
      <strong>Reusable products & services</strong>
      <div className="crm2-ref-filter-grid">
        <select value={status} onChange={(e) => setStatus(e.target.value as typeof status)}>
          <option>All</option><option>Active</option><option>Inactive</option>
        </select>
        <input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Search code, item, description..." />
        <button onClick={() => void refresh()} disabled={busy}>Refresh</button>
      </div>
    </section>
    <section className="crm2-ref-table-card">
      <div className="crm2-item-head">
        <span>Code</span><span>Item</span><span>Description</span><span>Rate</span><span>Tax</span><span>Status</span>
      </div>
      {filtered.length === 0 ? <p className="crm2-reference-empty">No sales items found</p> :
        filtered.map((item) => <button className="crm2-item-row" key={item.id} onClick={() => canManageSales && beginEdit(item)}>
          <span><strong>{item.code}</strong></span><span>{item.name}</span><span>{item.description || '-'}</span>
          <span>{money(item.defaultRate)}</span><span>{item.defaultTaxPercent}%</span>
          <span><em className={`crm2-sales-status ${item.status.toLowerCase()}`}>{item.status}</em></span>
        </button>)}
    </section>

    {open ? <div className="crm2-overlay" onMouseDown={() => setOpen(false)}>
      <section className="crm2-drawer" onMouseDown={(e) => e.stopPropagation()}>
        <div className="crm2-drawer-head">
          <div><span className="crm2-kicker">{editingId ? 'EDIT ITEM' : 'NEW ITEM'}</span><h2>{editingId ? code : 'Add Product / Service'}</h2></div>
          <button onClick={() => setOpen(false)}>×</button>
        </div>
        <label>Item code<input value={code} disabled={!!editingId} onChange={(e) => setCode(e.target.value)} placeholder="e.g. SUPPORT-ANNUAL" /></label>
        <label>Name<input value={name} onChange={(e) => setName(e.target.value)} placeholder="Annual Support" /></label>
        <label>Description<textarea rows={3} value={description} onChange={(e) => setDescription(e.target.value)} /></label>
        <div className="crm2-form-grid">
          <label>Default rate<input type="number" min="0" step="0.01" value={rate} onChange={(e) => setRate(e.target.value)} /></label>
          <label>Default tax %<input type="number" min="0" max="100" step="0.01" value={tax} onChange={(e) => setTax(e.target.value)} /></label>
          <label>Status<select value={itemStatus} onChange={(e) => setItemStatus(e.target.value as typeof itemStatus)}><option>Active</option><option>Inactive</option></select></label>
        </div>
        <div className="crm2-drawer-actions">
          <button onClick={() => setOpen(false)}>Cancel</button>
          <button className="crm2-primary" disabled={busy} onClick={() => void save()}>Save Item</button>
        </div>
      </section>
    </div> : null}
  </section>
}
