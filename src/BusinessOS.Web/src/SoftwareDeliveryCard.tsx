import { useEffect, useState } from 'react'
import { getSoftwareDelivery, type SoftwareDelivery } from './softwareDeliveryApi'

type Props = {
  token: string
  subscriptionId: string
}

function formatBytes(value?: number | null) {
  if (!value) return 'Size unavailable'
  const units = ['B', 'KB', 'MB', 'GB']
  let size = value
  let index = 0
  while (size >= 1024 && index < units.length - 1) {
    size /= 1024
    index++
  }
  return `${size.toFixed(index === 0 ? 0 : 1)} ${units[index]}`
}

export function SoftwareDeliveryCard({ token, subscriptionId }: Props) {
  const [delivery, setDelivery] = useState<SoftwareDelivery | null>(null)
  const [message, setMessage] = useState('')

  const load = async () => {
    if (!token || !subscriptionId) return
    setMessage('Checking software entitlement…')
    try {
      const result = await getSoftwareDelivery(token, subscriptionId)
      setDelivery(result)
      setMessage(result.downloadEntitled
        ? 'Your entitled software release is ready.'
        : result.unavailableReason || 'Software download is not available.')
    } catch (error) {
      setDelivery(null)
      setMessage(error instanceof Error ? error.message : String(error))
    }
  }

  useEffect(() => {
    void load()
    // Reload only when selected subscription/account changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token, subscriptionId])

  const release = delivery?.release

  return <section className="bos-card">
    <div className="bos-profile-heading">
      <div>
        <h2>Software download & activation</h2>
        <p>Downloads are shown only for an active entitled subscription and a published release.</p>
      </div>
      {delivery && <span className={delivery.downloadEntitled ? 'pill ok' : 'pill muted'}>
        {delivery.downloadEntitled ? 'Download entitled' : delivery.subscriptionStatus}
      </span>}
    </div>

    {release && <div className="bos-delivery-grid">
      <div><span>Version</span><strong>{release.version}</strong></div>
      <div><span>Channel</span><strong>{release.channel}</strong></div>
      <div><span>Platform</span><strong>{release.platform} / {release.architecture}</strong></div>
      <div><span>Installer</span><strong>{release.fileName}</strong></div>
      <div><span>Size</span><strong>{formatBytes(release.sizeBytes)}</strong></div>
      <div><span>Published</span><strong>{new Date(release.publishedAtUtc).toLocaleString()}</strong></div>
    </div>}

    {release?.releaseNotes && <p>{release.releaseNotes}</p>}

    {release && <details className="bos-checksum">
      <summary>Verify installer checksum</summary>
      <code>SHA-256: {release.sha256}</code>
    </details>}

    {delivery?.activationCode && <div className="bos-delivery-activation">
      <span>Activation code</span>
      <strong className="code">{delivery.activationCode}</strong>
      <small>Keep this code private and use it only in the BusinessOS desktop activation screen.</small>
    </div>}

    {release && <ol className="bos-install-steps">
      <li>Download the installer and verify the SHA-256 checksum shown above.</li>
      <li>Run the verified Windows installer and complete the setup.</li>
      <li>Open the installed product and choose activation / licence setup.</li>
      <li>Enter the activation code shown above. The licence stays tied to this subscription and device limits.</li>
    </ol>}

    <div className="bos-actions">
      {delivery?.downloadEntitled && delivery.downloadUrl
        ? <a className="bos-link-button" href={delivery.downloadUrl}
            target="_blank" rel="noopener noreferrer">Download installer</a>
        : <button disabled>Download unavailable</button>}
      <button onClick={load}>Refresh entitlement</button>
    </div>
    <p className="bos-message">{message}</p>
  </section>
}
