import { useMemo, useState } from 'react'
import {
  createCrmSalesItem,
  createCrmSalesItemGroup,
  updateCrmSalesItem,
  updateCrmSalesItemGroup,
  type CrmSalesItem,
  type CrmSalesItemGroup,
} from './crmApi'
import { exportCrmSpreadsheet, pickCrmSpreadsheet, type CrmSpreadsheetFormat } from './crmSpreadsheet'

type Props = {
  items: CrmSalesItem[]
  groups: CrmSalesItemGroup[]
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

export function CrmSalesItemsView({ items, groups, busy, refresh, notify, canManageSales }: Props) {
  const [query, setQuery] = useState('')
  const [status, setStatus] = useState<'All' | 'Active' | 'Inactive'>('All')
  const [groupFilter, setGroupFilter] = useState('All')
  const [editingId, setEditingId] = useState<string | null>(null)
  const [open, setOpen] = useState(false)
  const [code, setCode] = useState('')
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [rate, setRate] = useState('0')
  const [tax, setTax] = useState('18')
  const [itemStatus, setItemStatus] = useState<'Active' | 'Inactive'>('Active')
  const [groupId, setGroupId] = useState('')
  const [showGroups, setShowGroups] = useState(false)
  const [groupName, setGroupName] = useState('')
  const [editingGroupId, setEditingGroupId] = useState<string | null>(null)
  const [editingGroupName, setEditingGroupName] = useState('')
  const [pageSize, setPageSize] = useState(25)
  const [selectedIds, setSelectedIds] = useState<string[]>([])
  const [localBusy, setLocalBusy] = useState(false)

  const working = busy || localBusy
  const filtered = useMemo(() => {
    const search = query.trim().toLowerCase()
    return items.filter((item) => {
      const itemGroup = groups.find(group => group.id === item.groupId)
      return (status === 'All' || item.status === status) &&
        (groupFilter === 'All' || (groupFilter === 'Ungrouped' ? !item.groupId : item.groupId === groupFilter)) &&
        (!search || [item.code, item.name, item.description, itemGroup?.name].filter(Boolean).join(' ').toLowerCase().includes(search))
    })
  }, [groupFilter, groups, items, query, status])
  const visible = filtered.slice(0, pageSize)

  function beginNew() {
    setEditingId(null); setCode(''); setName(''); setDescription('')
    setRate('0'); setTax('18'); setItemStatus('Active'); setGroupId(''); setOpen(true)
  }

  function beginEdit(item: CrmSalesItem) {
    setEditingId(item.id); setCode(item.code); setName(item.name)
    setDescription(item.description || ''); setRate(String(item.defaultRate))
    setTax(String(item.defaultTaxPercent)); setItemStatus(item.status); setGroupId(item.groupId || ''); setOpen(true)
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
          groupId: groupId || null,
        })
        notify('Sales item updated')
      } else {
        await createCrmSalesItem({
          code: code.trim(), name: name.trim(), description: description.trim() || undefined,
          defaultRate: Math.max(0, Number(rate) || 0),
          defaultTaxPercent: Math.max(0, Number(tax) || 0), status: itemStatus,
          groupId: groupId || null,
        })
        notify('Sales item created')
      }
      setOpen(false); await refresh()
    } catch (error) {
      notify(error instanceof Error ? error.message : String(error))
    }
  }

  async function exportItems(format: CrmSpreadsheetFormat) {
    try {
      await exportCrmSpreadsheet(`crm-sales-items-${new Date().toISOString().slice(0, 10)}`, {
        headers: ['Code', 'Name', 'Group', 'Description', 'Rate', 'Tax %', 'Status'],
        rows: filtered.map(item => [
          item.code, item.name, groups.find(group => group.id === item.groupId)?.name || '',
          item.description || '', item.defaultRate, item.defaultTaxPercent, item.status,
        ]),
      }, format)
      notify(`Sales items exported to ${format.toUpperCase()}`)
    } catch (error) {
      notify(error instanceof Error ? error.message : String(error))
    }
  }

  function normalizeHeader(value: string) {
    return value.trim().toLowerCase().replace(/[^a-z0-9]/g, '')
  }

  function findColumn(headers: string[], aliases: string[]) {
    const normalized = headers.map(normalizeHeader)
    return aliases.map(normalizeHeader).map(alias => normalized.indexOf(alias)).find(index => index >= 0) ?? -1
  }

  async function importRows(rows: string[][], fileName: string) {
    if (rows.length < 2) { notify('Item spreadsheet must include a header and data rows'); return }
    const headers = rows[0]
    const col = {
      code: findColumn(headers, ['Code', 'Item Code', 'SKU']),
      name: findColumn(headers, ['Name', 'Item', 'Item Name']),
      group: findColumn(headers, ['Group', 'Item Group', 'Category']),
      description: findColumn(headers, ['Description']),
      rate: findColumn(headers, ['Rate', 'Default Rate', 'Price']),
      tax: findColumn(headers, ['Tax %', 'Tax', 'GST %', 'GST']),
      status: findColumn(headers, ['Status']),
    }
    if (col.code < 0 || col.name < 0 || col.rate < 0) {
      notify('Import needs Code, Name and Rate columns')
      return
    }

    let created = 0
    let updated = 0
    const skipped: number[] = []
    setLocalBusy(true)
    try {
      for (let rowIndex = 1; rowIndex < rows.length; rowIndex += 1) {
        const row = rows[rowIndex]
        const codeValue = row[col.code]?.trim() || ''
        const nameValue = row[col.name]?.trim() || ''
        const rateValue = Number((row[col.rate] || '').replace(/[₹,\s]/g, ''))
        const taxValue = col.tax >= 0 ? Number((row[col.tax] || '0').replace(/[%\s]/g, '')) : 0
        const statusValue = col.status >= 0 ? row[col.status]?.trim() || 'Active' : 'Active'
        if (!codeValue || !nameValue || !Number.isFinite(rateValue) || rateValue < 0 || !Number.isFinite(taxValue) || taxValue < 0 || taxValue > 100) {
          skipped.push(rowIndex + 1)
          continue
        }
        const existing = items.find(item => item.code.toLowerCase() === codeValue.toLowerCase())
        const groupValue = col.group >= 0 ? row[col.group]?.trim() || '' : ''
        const matchedGroup = groupValue ? groups.find(group => group.active && group.name.toLowerCase() === groupValue.toLowerCase()) : undefined
        if (groupValue && !matchedGroup) { skipped.push(rowIndex + 1); continue }
        try {
          if (existing) {
            await updateCrmSalesItem(existing.id, {
              name: nameValue,
              description: col.description >= 0 ? row[col.description]?.trim() || undefined : undefined,
              defaultRate: rateValue,
              defaultTaxPercent: taxValue,
              status: statusValue.toLowerCase() === 'inactive' ? 'Inactive' : 'Active',
              groupId: matchedGroup?.id || null,
            })
            updated += 1
          } else {
            await createCrmSalesItem({
              code: codeValue,
              name: nameValue,
              description: col.description >= 0 ? row[col.description]?.trim() || undefined : undefined,
              defaultRate: rateValue,
              defaultTaxPercent: taxValue,
              status: statusValue.toLowerCase() === 'inactive' ? 'Inactive' : 'Active',
              groupId: matchedGroup?.id || null,
            })
            created += 1
          }
        } catch {
          skipped.push(rowIndex + 1)
        }
      }
      await refresh()
      notify(`${fileName}: ${created} created, ${updated} updated${skipped.length ? `; ${skipped.length} row(s) skipped` : ''}`)
    } finally {
      setLocalBusy(false)
    }
  }

  function importItems() {
    pickCrmSpreadsheet(
      (rows, fileName) => { void importRows(rows, fileName) },
      message => notify(message),
    )
  }

  function toggleSelection(id: string) {
    setSelectedIds(current => current.includes(id) ? current.filter(value => value !== id) : [...current, id])
  }

  function toggleVisibleSelection() {
    const ids = visible.map(item => item.id)
    const allSelected = ids.length > 0 && ids.every(id => selectedIds.includes(id))
    setSelectedIds(allSelected
      ? selectedIds.filter(id => !ids.includes(id))
      : Array.from(new Set([...selectedIds, ...ids])))
  }

  async function bulkStatus(next: 'Active' | 'Inactive') {
    const selected = items.filter(item => selectedIds.includes(item.id))
    if (selected.length === 0) { notify('Select at least one sales item'); return }
    setLocalBusy(true)
    try {
      for (const item of selected) {
        await updateCrmSalesItem(item.id, {
          name: item.name,
          description: item.description || undefined,
          defaultRate: item.defaultRate,
          defaultTaxPercent: item.defaultTaxPercent,
          status: next,
          groupId: item.groupId || null,
        })
      }
      setSelectedIds([])
      await refresh()
      notify(`${selected.length} item(s) moved to ${next}`)
    } catch (error) {
      notify(error instanceof Error ? error.message : String(error))
    } finally {
      setLocalBusy(false)
    }
  }

  async function addGroup() {
    if (!groupName.trim()) { notify('Group name is required'); return }
    setLocalBusy(true)
    try {
      await createCrmSalesItemGroup({ name: groupName.trim(), active: true })
      setGroupName('')
      await refresh()
      notify('Sales item group created')
    } catch (error) {
      notify(error instanceof Error ? error.message : String(error))
    } finally { setLocalBusy(false) }
  }

  async function toggleGroup(group: CrmSalesItemGroup) {
    setLocalBusy(true)
    try {
      await updateCrmSalesItemGroup(group.id, { name: group.name, active: !group.active })
      await refresh()
      notify(`Group ${group.active ? 'deactivated' : 'activated'}`)
    } catch (error) {
      notify(error instanceof Error ? error.message : String(error))
    } finally { setLocalBusy(false) }
  }

  async function renameGroup(group: CrmSalesItemGroup) {
    if (!editingGroupName.trim()) { notify('Group name is required'); return }
    setLocalBusy(true)
    try {
      await updateCrmSalesItemGroup(group.id, { name: editingGroupName.trim(), active: group.active })
      setEditingGroupId(null); setEditingGroupName('')
      await refresh()
      notify('Sales item group renamed')
    } catch (error) {
      notify(error instanceof Error ? error.message : String(error))
    } finally { setLocalBusy(false) }
  }

  return <section className="crm2-ref-list-page">
    <div className="crm2-ref-action-row">
      {canManageSales ? <button className="crm2-ref-primary" onClick={beginNew}>+ Add Item</button> : null}
      {canManageSales ? <button onClick={importItems} disabled={working}>Import Items</button> : null}
      {canManageSales ? <button onClick={() => setShowGroups(value => !value)} disabled={working}>Groups</button> : null}
      <span className="crm2-action-spacer" />
      <button onClick={() => void exportItems('xlsx')} disabled={working || filtered.length === 0}>Export XLSX</button>
      <button onClick={() => void exportItems('csv')} disabled={working || filtered.length === 0}>CSV</button>
    </div>
    {showGroups && canManageSales ? <section className="crm2-ref-filter-card">
      <strong>Sales Item Groups</strong>
      <div className="crm2-ref-filter-grid"><input value={groupName} onChange={(e) => setGroupName(e.target.value)} placeholder="New group name" /><button onClick={() => void addGroup()} disabled={working || !groupName.trim()}>Add Group</button></div>
      <div className="crm2-work-list">{groups.length === 0 ? <p className="crm2-muted">No groups yet.</p> : groups.map(group => <article key={group.id}>
        {editingGroupId === group.id ? <><input value={editingGroupName} onChange={(e) => setEditingGroupName(e.target.value)} /><button onClick={() => void renameGroup(group)} disabled={working}>Save</button><button onClick={() => setEditingGroupId(null)}>Cancel</button></> :
          <><strong>{group.name}</strong><span>{group.active ? 'Active' : 'Inactive'}</span><button onClick={() => { setEditingGroupId(group.id); setEditingGroupName(group.name) }} disabled={working}>Rename</button><button onClick={() => void toggleGroup(group)} disabled={working}>{group.active ? 'Deactivate' : 'Activate'}</button></>}
      </article>)}</div>
    </section> : null}
    {selectedIds.length > 0 && canManageSales ? <div className="crm2-ref-action-row">
      <strong>{selectedIds.length} selected</strong>
      <button onClick={() => void bulkStatus('Active')} disabled={working}>Mark Active</button>
      <button onClick={() => void bulkStatus('Inactive')} disabled={working}>Mark Inactive</button>
      <button onClick={() => setSelectedIds([])} disabled={working}>Clear</button>
    </div> : null}
    <section className="crm2-ref-table-card">
      <div className="crm2-ref-table-tools">
        <select value={pageSize} onChange={(e) => setPageSize(Number(e.target.value))}><option value={25}>25</option><option value={50}>50</option><option value={100}>100</option></select>
        {canManageSales ? <button onClick={toggleVisibleSelection} disabled={working || visible.length === 0}>{visible.length > 0 && visible.every(item => selectedIds.includes(item.id)) ? 'Clear Selection' : 'Select Visible'}</button> : null}
        <button onClick={() => void refresh()} disabled={working}>↻</button>
        <select value={status} onChange={(e) => setStatus(e.target.value as typeof status)}><option>All</option><option>Active</option><option>Inactive</option></select>
        <select value={groupFilter} onChange={(e) => setGroupFilter(e.target.value)}><option value="All">All groups</option><option value="Ungrouped">Ungrouped</option>{groups.map(group => <option key={group.id} value={group.id}>{group.name}{group.active ? '' : ' (Inactive)'}</option>)}</select>
        <span /><label><b>⌕</b><input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Search..." /></label>
      </div>
      <div className="crm2-item-head">
        <span></span><span>Code</span><span>Item</span><span>Group</span><span>Description</span><span>Rate</span><span>Tax</span><span>Status</span>
      </div>
      {filtered.length === 0 ? <p className="crm2-reference-empty">No sales items found</p> :
        visible.map((item) => <div className="crm2-item-row" key={item.id} onDoubleClick={() => canManageSales && beginEdit(item)}>
          <span><input type="checkbox" checked={selectedIds.includes(item.id)} onChange={() => toggleSelection(item.id)} disabled={!canManageSales} /></span>
          <span><strong>{item.code}</strong></span><span>{item.name}</span><span>{groups.find(group => group.id === item.groupId)?.name || '—'}</span><span>{item.description || '-'}</span>
          <span>{money(item.defaultRate)}</span><span>{item.defaultTaxPercent}%</span>
          <span><em className={`crm2-sales-status ${item.status.toLowerCase()}`}>{item.status}</em></span>
        </div>)}
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
          <label>Group<select value={groupId} onChange={(e) => setGroupId(e.target.value)}><option value="">Ungrouped</option>{groups.filter(group => group.active || group.id === groupId).map(group => <option key={group.id} value={group.id}>{group.name}{group.active ? '' : ' (Inactive)'}</option>)}</select></label>
        </div>
        <div className="crm2-drawer-actions">
          <button onClick={() => setOpen(false)}>Cancel</button>
          <button className="crm2-primary" disabled={working} onClick={() => void save()}>Save Item</button>
        </div>
      </section>
    </div> : null}
  </section>
}
