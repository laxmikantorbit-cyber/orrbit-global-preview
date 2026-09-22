import { useMemo, useState } from 'react'
import {
  archiveCrmKnowledgeArticle,
  closeCrmEstimateRequest,
  convertCrmEstimateRequestToLead,
  createCrmEstimateRequest,
  createCrmKnowledgeArticle,
  publishCrmKnowledgeArticle,
  registerCrmMediaAsset,
  reviewCrmEstimateRequest,
  saveCrmKnowledgeCategory,
  setCrmMediaAssetActive,
  updateCrmEstimateRequest,
  updateCrmKnowledgeArticle,
  type CrmEstimateRequest,
  type CrmKnowledgeArticle,
  type CrmKnowledgeCategory,
  type CrmMediaAsset,
  type CrmTeamMember,
} from './crmApi'

type SharedProps = {
  busy: boolean
  refresh: () => Promise<void>
  notify: (message: string) => void
}

function money(value?: number | null) {
  if (value == null) return '—'
  return new Intl.NumberFormat('en-IN', { style: 'currency', currency: 'INR', maximumFractionDigits: 0 }).format(value)
}

function fmtDate(value: string) {
  return new Date(value).toLocaleDateString('en-IN', { day: '2-digit', month: 'short', year: 'numeric' })
}

export function CrmEstimateRequestsView({
  requests, teamMembers, busy, refresh, notify, canManage,
}: SharedProps & {
  requests: CrmEstimateRequest[]
  teamMembers: CrmTeamMember[]
  canManage: boolean
}) {
  const [query, setQuery] = useState('')
  const [status, setStatus] = useState('All')
  const [editing, setEditing] = useState<CrmEstimateRequest | null>(null)
  const [creating, setCreating] = useState(false)
  const [source, setSource] = useState('Website')
  const [requirement, setRequirement] = useState('')
  const [contactName, setContactName] = useState('')
  const [mobileNumber, setMobileNumber] = useState('')
  const [email, setEmail] = useState('')
  const [expectedValue, setExpectedValue] = useState('')
  const [assignedUserId, setAssignedUserId] = useState('')

  const filtered = useMemo(() => {
    const needle = query.trim().toLowerCase()
    return requests.filter((item) => {
      if (status !== 'All' && item.status !== status) return false
      return !needle || [item.source, item.requirement, item.contactName, item.mobileNumber, item.email, item.status]
        .filter(Boolean).join(' ').toLowerCase().includes(needle)
    })
  }, [query, requests, status])

  function resetForm() {
    setEditing(null); setCreating(false); setSource('Website'); setRequirement('')
    setContactName(''); setMobileNumber(''); setEmail(''); setExpectedValue(''); setAssignedUserId('')
  }

  function openCreate() { resetForm(); setCreating(true) }
  function openEdit(item: CrmEstimateRequest) {
    setCreating(false); setEditing(item); setSource(item.source); setRequirement(item.requirement)
    setContactName(item.contactName || ''); setMobileNumber(item.mobileNumber || ''); setEmail(item.email || '')
    setExpectedValue(item.expectedValue == null ? '' : String(item.expectedValue)); setAssignedUserId(item.assignedUserId || '')
  }

  async function save() {
    if (!requirement.trim()) { notify('Requirement is required'); return }
    const payload = {
      source: source.trim(), requirement: requirement.trim(),
      contactName: contactName.trim() || undefined, mobileNumber: mobileNumber.trim() || undefined,
      email: email.trim() || undefined, expectedValue: expectedValue ? Number(expectedValue) : null,
      assignedUserId: assignedUserId || null,
    }
    try {
      if (editing) await updateCrmEstimateRequest(editing.id, payload)
      else await createCrmEstimateRequest(payload)
      notify(editing ? 'Estimate request updated' : 'Estimate request created')
      resetForm(); await refresh()
    } catch (error) { notify(error instanceof Error ? error.message : String(error)) }
  }

  async function act(item: CrmEstimateRequest, action: 'review' | 'convert' | 'close') {
    try {
      if (action === 'review') await reviewCrmEstimateRequest(item.id)
      if (action === 'convert') await convertCrmEstimateRequestToLead(item.id)
      if (action === 'close') await closeCrmEstimateRequest(item.id)
      notify(action === 'convert' ? 'Estimate request converted to lead' : 'Estimate request updated')
      await refresh()
    } catch (error) { notify(error instanceof Error ? error.message : String(error)) }
  }

  return <section className="crm2-ref-list-page">
    <div className="crm2-reference-module-head">
      <div><span className="crm2-kicker">BUSINESS MODULE</span><h2>Estimate Request</h2><p>Collect website/WhatsApp requests, review them and convert qualified requests into CRM leads.</p></div>
      {canManage ? <button className="crm2-filter-button" onClick={openCreate}>+ New Request</button> : null}
    </div>
    <section className="crm2-ref-filter-card"><strong>Filter by</strong><div className="crm2-ref-filter-grid">
      <select value={status} onChange={(e) => setStatus(e.target.value)}><option>All</option><option>New</option><option>Reviewing</option><option>Converted</option><option>Closed</option></select>
      <input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Search contact or requirement..." />
      <button onClick={() => void refresh()} disabled={busy}>Refresh</button>
    </div></section>
    {canManage && (creating || editing) ? <section className="crm2-ref-filter-card">
      <strong>{editing ? 'Edit request' : 'New estimate request'}</strong>
      <div className="crm2-form-grid">
        <label>Source<select value={source} onChange={(e) => setSource(e.target.value)}><option>Website</option><option>WhatsApp</option><option>Call</option><option>Partner</option><option>Other</option></select></label>
        <label>Contact name<input value={contactName} onChange={(e) => setContactName(e.target.value)} /></label>
        <label>Mobile<input value={mobileNumber} onChange={(e) => setMobileNumber(e.target.value)} /></label>
        <label>Email<input type="email" value={email} onChange={(e) => setEmail(e.target.value)} /></label>
        <label>Expected value<input type="number" min="0" value={expectedValue} onChange={(e) => setExpectedValue(e.target.value)} /></label>
        <label>Assigned<select value={assignedUserId} onChange={(e) => setAssignedUserId(e.target.value)}><option value="">Current user / automatic</option>{teamMembers.filter(x => x.active).map(x => <option key={x.id} value={x.id}>{x.displayName}</option>)}</select></label>
      </div>
      <label>Requirement<textarea rows={4} value={requirement} onChange={(e) => setRequirement(e.target.value)} /></label>
      <div className="crm2-drawer-actions"><button onClick={resetForm}>Cancel</button><button className="crm2-primary" onClick={() => void save()} disabled={busy}>Save</button></div>
    </section> : null}
    <section className="crm2-ref-table-card">
      <div className="crm2-reference-head"><span>Contact</span><span>Requirement</span><span>Value</span><span>Assigned</span><span>Status</span></div>
      {filtered.length === 0 ? <p className="crm2-reference-empty">No estimate requests found</p> : filtered.map(item => {
        const owner = teamMembers.find(x => x.id === item.assignedUserId)
        return <div className="crm2-reference-row" key={item.id}>
          <span><strong>{item.contactName || item.email || item.mobileNumber || 'Anonymous'}</strong><small>{item.source} · {fmtDate(item.createdAtUtc)}</small></span>
          <span>{item.requirement}</span><span>{money(item.expectedValue)}</span><span>{owner?.displayName || 'Unassigned'}</span>
          <span><b>{item.status}</b>{canManage && item.status !== 'Converted' && item.status !== 'Closed' ? <small>
            {item.status === 'New' ? <button onClick={() => void act(item, 'review')}>Review</button> : null}
            <button onClick={() => openEdit(item)}>Edit</button><button onClick={() => void act(item, 'convert')}>Convert</button><button onClick={() => void act(item, 'close')}>Close</button>
          </small> : null}</span>
        </div>
      })}
    </section>
  </section>
}

export function CrmKnowledgeBaseView({
  categories, articles, teamMembers, currentRole, busy, refresh, notify, canManage,
}: SharedProps & {
  categories: CrmKnowledgeCategory[]
  articles: CrmKnowledgeArticle[]
  teamMembers: CrmTeamMember[]
  currentRole?: string
  canManage: boolean
}) {
  const [query, setQuery] = useState('')
  const [editing, setEditing] = useState<CrmKnowledgeArticle | null>(null)
  const [creating, setCreating] = useState(false)
  const [title, setTitle] = useState('')
  const [content, setContent] = useState('')
  const [categoryId, setCategoryId] = useState('')
  const [ownerUserId, setOwnerUserId] = useState('')
  const [visibility, setVisibility] = useState<'Team' | 'Private'>('Team')
  const [categoryName, setCategoryName] = useState('')

  const filtered = useMemo(() => {
    const needle = query.trim().toLowerCase()
    return articles.filter(x => !needle || [x.title, x.content, x.status, x.visibility].join(' ').toLowerCase().includes(needle))
  }, [articles, query])

  function resetArticle() { setEditing(null); setCreating(false); setTitle(''); setContent(''); setCategoryId(''); setOwnerUserId(''); setVisibility('Team') }
  function openCreate() { resetArticle(); setCreating(true); setCategoryId(categories.find(x => x.active)?.id || '') }
  function openEdit(item: CrmKnowledgeArticle) {
    setCreating(false); setEditing(item); setTitle(item.title); setContent(item.content); setCategoryId(item.categoryId)
    setOwnerUserId(item.ownerUserId); setVisibility(item.visibility)
  }

  async function saveArticle() {
    if (!title.trim() || !content.trim() || !categoryId) { notify('Title, content and category are required'); return }
    const payload = { title: title.trim(), content: content.trim(), categoryId, ownerUserId: ownerUserId || null, visibility }
    try {
      if (editing) await updateCrmKnowledgeArticle(editing.id, payload)
      else await createCrmKnowledgeArticle(payload)
      notify(editing ? 'Knowledge article updated' : 'Knowledge article created')
      resetArticle(); await refresh()
    } catch (error) { notify(error instanceof Error ? error.message : String(error)) }
  }

  async function articleAction(item: CrmKnowledgeArticle, action: 'publish' | 'archive') {
    try {
      if (action === 'publish') await publishCrmKnowledgeArticle(item.id)
      else await archiveCrmKnowledgeArticle(item.id)
      notify(action === 'publish' ? 'Article published' : 'Article archived'); await refresh()
    } catch (error) { notify(error instanceof Error ? error.message : String(error)) }
  }

  async function addCategory() {
    if (!categoryName.trim()) { notify('Category name is required'); return }
    try {
      await saveCrmKnowledgeCategory({ name: categoryName.trim(), sortOrder: categories.length * 10 + 10, active: true })
      setCategoryName(''); notify('Knowledge category created'); await refresh()
    } catch (error) { notify(error instanceof Error ? error.message : String(error)) }
  }

  const admin = currentRole === 'Owner' || currentRole === 'Admin'
  return <section className="crm2-ref-list-page">
    <div className="crm2-reference-module-head"><div><span className="crm2-kicker">BUSINESS MODULE</span><h2>Knowledge Base</h2><p>Store FAQs, sales answers, onboarding guides and team support articles.</p></div>{canManage ? <button className="crm2-filter-button" onClick={openCreate}>+ New Article</button> : null}</div>
    <section className="crm2-ref-filter-card"><strong>Knowledge search</strong><div className="crm2-ref-filter-grid"><input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Search articles..." /><button onClick={() => void refresh()} disabled={busy}>Refresh</button></div></section>
    {admin ? <section className="crm2-ref-filter-card"><strong>Categories</strong><div className="crm2-ref-filter-grid"><input value={categoryName} onChange={(e) => setCategoryName(e.target.value)} placeholder="New category name" /><button onClick={() => void addCategory()} disabled={busy}>Add Category</button><span>{categories.filter(x => x.active).map(x => x.name).join(' · ') || 'No categories'}</span></div></section> : null}
    {canManage && (creating || editing) ? <section className="crm2-ref-filter-card"><strong>{editing ? 'Edit' : 'New'} article</strong>
      <div className="crm2-form-grid"><label>Title<input value={title} onChange={(e) => setTitle(e.target.value)} /></label><label>Category<select value={categoryId} onChange={(e) => setCategoryId(e.target.value)}><option value="">Select category</option>{categories.filter(x => x.active).map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label><label>Owner<select value={ownerUserId} onChange={(e) => setOwnerUserId(e.target.value)}><option value="">Current user</option>{teamMembers.filter(x => x.active).map(x => <option key={x.id} value={x.id}>{x.displayName}</option>)}</select></label><label>Visibility<select value={visibility} onChange={(e) => setVisibility(e.target.value as 'Team' | 'Private')}><option>Team</option><option>Private</option></select></label></div>
      <label>Article content<textarea rows={7} value={content} onChange={(e) => setContent(e.target.value)} /></label>
      <div className="crm2-drawer-actions"><button onClick={resetArticle}>Cancel</button><button className="crm2-primary" onClick={() => void saveArticle()} disabled={busy}>Save</button></div>
    </section> : null}
    <section className="crm2-ref-table-card"><div className="crm2-reference-head"><span>Article</span><span>Category</span><span>Owner</span><span>Visibility</span><span>Status</span></div>
      {filtered.length === 0 ? <p className="crm2-reference-empty">No knowledge articles found</p> : filtered.map(item => {
        const category = categories.find(x => x.id === item.categoryId); const owner = teamMembers.find(x => x.id === item.ownerUserId)
        return <div className="crm2-reference-row" key={item.id}><span><strong>{item.title}</strong><small>{fmtDate(item.updatedAtUtc)}</small></span><span>{category?.name || '—'}</span><span>{owner?.displayName || '—'}</span><span>{item.visibility}</span><span><b>{item.status}</b>{canManage && item.status !== 'Archived' ? <small><button onClick={() => openEdit(item)}>Edit</button>{item.status === 'Draft' ? <button onClick={() => void articleAction(item, 'publish')}>Publish</button> : null}<button onClick={() => void articleAction(item, 'archive')}>Archive</button></small> : null}</span></div>
      })}
    </section>
  </section>
}

export function CrmUtilitiesView({
  assets, busy, refresh, notify, canManage,
}: SharedProps & { assets: CrmMediaAsset[]; canManage: boolean }) {
  const [creating, setCreating] = useState(false)
  const [fileName, setFileName] = useState('')
  const [mimeType, setMimeType] = useState('application/pdf')
  const [sizeBytes, setSizeBytes] = useState('')
  const [purpose, setPurpose] = useState('')
  const [storageReference, setStorageReference] = useState('')
  const [entityType, setEntityType] = useState('')
  const [entityId, setEntityId] = useState('')

  async function save() {
    try {
      await registerCrmMediaAsset({
        fileName: fileName.trim(), mimeType: mimeType.trim(), sizeBytes: Number(sizeBytes || 0),
        purpose: purpose.trim(), storageReference: storageReference.trim(),
        entityType: entityType.trim() || null, entityId: entityId.trim() || null,
      })
      setCreating(false); setFileName(''); setPurpose(''); setStorageReference(''); setSizeBytes(''); setEntityType(''); setEntityId('')
      notify('Media asset registered'); await refresh()
    } catch (error) { notify(error instanceof Error ? error.message : String(error)) }
  }

  async function toggle(item: CrmMediaAsset) {
    try { await setCrmMediaAssetActive(item.id, !item.active); notify(item.active ? 'Media asset deactivated' : 'Media asset activated'); await refresh() }
    catch (error) { notify(error instanceof Error ? error.message : String(error)) }
  }

  return <section className="crm2-ref-list-page">
    <div className="crm2-reference-module-head"><div><span className="crm2-kicker">BUSINESS MODULE</span><h2>Utilities & Media</h2><p>Register CRM files and attachment references used by imports, exports, customers and internal work.</p></div>{canManage ? <button className="crm2-filter-button" onClick={() => setCreating(true)}>+ Register Media</button> : null}</div>
    {canManage && creating ? <section className="crm2-ref-filter-card"><strong>Register media reference</strong><div className="crm2-form-grid">
      <label>File name<input value={fileName} onChange={(e) => setFileName(e.target.value)} /></label><label>MIME type<input value={mimeType} onChange={(e) => setMimeType(e.target.value)} /></label>
      <label>Size in bytes<input type="number" min="0" value={sizeBytes} onChange={(e) => setSizeBytes(e.target.value)} /></label><label>Purpose<input value={purpose} onChange={(e) => setPurpose(e.target.value)} /></label>
      <label>Storage reference<input value={storageReference} onChange={(e) => setStorageReference(e.target.value)} placeholder="R2/Drive/object key or URL" /></label><label>Entity type<input value={entityType} onChange={(e) => setEntityType(e.target.value)} placeholder="Lead / Account / Contract..." /></label>
      <label>Entity id<input value={entityId} onChange={(e) => setEntityId(e.target.value)} placeholder="Optional GUID" /></label>
    </div><div className="crm2-drawer-actions"><button onClick={() => setCreating(false)}>Cancel</button><button className="crm2-primary" onClick={() => void save()} disabled={busy}>Save</button></div></section> : null}
    <section className="crm2-ref-table-card"><div className="crm2-reference-head"><span>File</span><span>Purpose</span><span>Type</span><span>Size</span><span>Status</span></div>
      {assets.length === 0 ? <p className="crm2-reference-empty">No media assets registered</p> : assets.map(item => <div className="crm2-reference-row" key={item.id}><span><strong>{item.fileName}</strong><small>{fmtDate(item.createdAtUtc)}</small></span><span>{item.purpose}</span><span>{item.mimeType}</span><span>{Math.max(0, item.sizeBytes / 1024).toFixed(1)} KB</span><span><b>{item.active ? 'Active' : 'Inactive'}</b>{canManage ? <small><button onClick={() => void toggle(item)}>{item.active ? 'Deactivate' : 'Activate'}</button></small> : null}</span></div>)}
    </section>
  </section>
}
