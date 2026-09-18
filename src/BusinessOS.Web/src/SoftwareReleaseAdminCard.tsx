import { useEffect, useState } from 'react'
import {
  deactivateSoftwareRelease,
  listSoftwareReleases,
  publishSoftwareRelease,
  type SoftwareRelease,
  type SoftwareReleaseCreate,
} from './softwareDeliveryApi'

type Props = {
  token: string
}

const emptyForm: SoftwareReleaseCreate = {
  productCode: 'AI_REPAIR',
  version: '',
  channel: 'Stable',
  platform: 'Windows',
  architecture: 'x64',
  fileName: '',
  downloadUrl: '',
  sha256: '',
  sizeBytes: null,
  releaseNotes: '',
  publishedAtUtc: null,
}

export function SoftwareReleaseAdminCard({ token }: Props) {
  const [items, setItems] = useState<SoftwareRelease[]>([])
  const [form, setForm] = useState<SoftwareReleaseCreate>(emptyForm)
  const [message, setMessage] = useState('')

  const load = async () => {
    if (!token) return
    try {
      setItems(await listSoftwareReleases(token))
      setMessage('Software release registry refreshed.')
    } catch (error) {
      setMessage(error instanceof Error ? error.message : String(error))
    }
  }

  useEffect(() => {
    if (token) void load()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token])

  const publish = async () => {
    setMessage('Publishing release metadata…')
    try {
      await publishSoftwareRelease(token, {
        ...form,
        productCode: form.productCode.trim(),
        version: form.version.trim(),
        fileName: form.fileName.trim(),
        downloadUrl: form.downloadUrl.trim(),
        sha256: form.sha256.trim().toLowerCase(),
        releaseNotes: form.releaseNotes?.trim() || null,
      })
      setForm(current => ({
        ...emptyForm,
        productCode: current.productCode || 'AI_REPAIR',
      }))
      await load()
      setMessage('Release published. Entitled customers can now receive this release.')
    } catch (error) {
      setMessage(error instanceof Error ? error.message : String(error))
    }
  }

  const deactivate = async (id: string) => {
    setMessage('Deactivating release…')
    try {
      await deactivateSoftwareRelease(token, id)
      await load()
      setMessage('Release deactivated.')
    } catch (error) {
      setMessage(error instanceof Error ? error.message : String(error))
    }
  }

  return <section className="bos-card">
    <div className="bos-profile-heading">
      <div><h2>Software release registry</h2>
        <p>Publish only verified installer metadata. Use HTTPS only, supported targets only, and calculate SHA-256 from the final uploaded installer.</p></div>
      <span className="pill muted">{items.filter(x => x.active).length} active</span>
    </div>

    <div className="bos-profile-grid">
      <label>Product code<input value={form.productCode}
        onChange={e => setForm({ ...form, productCode: e.target.value.toUpperCase() })} /></label>
      <label>Version<input value={form.version}
        onChange={e => setForm({ ...form, version: e.target.value })} placeholder="1.0.0" /></label>
      <label>Channel<select value={form.channel}
        onChange={e => setForm({ ...form, channel: e.target.value })}>
        <option>Stable</option><option>Beta</option><option>Internal</option>
      </select></label>
      <label>Platform<select value={form.platform}
        onChange={e => setForm({ ...form, platform: e.target.value })}>
        <option>Windows</option>
      </select></label>
      <label>Architecture<select value={form.architecture}
        onChange={e => setForm({ ...form, architecture: e.target.value })}>
        <option>x64</option><option>x86</option><option>arm64</option>
      </select></label>
      <label>Installer filename<input value={form.fileName}
        onChange={e => setForm({ ...form, fileName: e.target.value })} placeholder="oRRbit-AI-Repair-1.0.0.exe" /></label>
      <label className="bos-wide-field">HTTPS download URL<input value={form.downloadUrl}
        onChange={e => setForm({ ...form, downloadUrl: e.target.value })} placeholder="https://..." /></label>
      <label className="bos-wide-field">SHA-256 checksum<input value={form.sha256}
        onChange={e => setForm({ ...form, sha256: e.target.value.toLowerCase() })} maxLength={64}
        placeholder="Get-FileHash -Algorithm SHA256 .\installer.exe" /></label>
      <label>Size in bytes<input type="number" min="0" value={form.sizeBytes ?? ''}
        onChange={e => setForm({ ...form, sizeBytes: e.target.value ? Number(e.target.value) : null })} /></label>
      <label className="bos-wide-field">Release notes<input value={form.releaseNotes || ''}
        onChange={e => setForm({ ...form, releaseNotes: e.target.value })} maxLength={4000} /></label>
    </div>

    <div className="bos-actions">
      <button onClick={publish} disabled={!token}>Publish verified release</button>
      <button onClick={load} disabled={!token}>Refresh registry</button>
    </div>
    <p className="bos-message">{message}</p>

    <div className="bos-table-wrap"><table className="bos-table"><thead><tr>
      <th>Product</th><th>Version</th><th>Target</th><th>Published</th><th>Status</th><th>Action</th>
    </tr></thead><tbody>{items.map(item => <tr key={item.id}>
      <td>{item.productCode}</td><td>{item.version}</td>
      <td>{item.channel} · {item.platform}/{item.architecture}</td>
      <td>{new Date(item.publishedAtUtc).toLocaleString()}</td>
      <td><span className={item.active ? 'pill ok' : 'pill muted'}>{item.active ? 'Active' : 'Inactive'}</span></td>
      <td>{item.active && <button className="danger" onClick={() => deactivate(item.id)}>Deactivate</button>}</td>
    </tr>)}</tbody></table></div>
    {items.length === 0 && <p>No software releases have been published yet.</p>}
  </section>
}
