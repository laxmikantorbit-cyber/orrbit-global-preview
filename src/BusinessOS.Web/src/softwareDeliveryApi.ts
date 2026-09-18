import { apiBase } from './businessosApi'

export type SoftwareRelease = {
  id: string
  tenantId: string
  productCode: string
  version: string
  channel: string
  platform: string
  architecture: string
  fileName: string
  downloadUrl: string
  sha256: string
  sizeBytes?: number | null
  releaseNotes?: string | null
  publishedAtUtc: string
  active: boolean
}

export type SoftwareDeliveryEvent = {
  id: string
  tenantId: string
  subscriptionId: string
  releaseId?: string | null
  productCode: string
  channel: string
  platform: string
  architecture: string
  action: string
  downloadEntitled: boolean
  unavailableReason?: string | null
  occurredAtUtc: string
}

export type SoftwareDelivery = {
  tenantId: string
  organisationId: string
  subscriptionId: string
  licenseId: string
  productCode: string
  subscriptionStatus: string
  validUntil: string
  downloadEntitled: boolean
  unavailableReason?: string | null
  release?: SoftwareRelease | null
  downloadUrl?: string | null
  activationCode?: string | null
}

export type SoftwareReleaseCreate = {
  productCode: string
  version: string
  channel: string
  platform: string
  architecture: string
  fileName: string
  downloadUrl: string
  sha256: string
  sizeBytes?: number | null
  releaseNotes?: string | null
  publishedAtUtc?: string | null
}

function headers(token: string): HeadersInit {
  return {
    Authorization: `Bearer ${token}`,
    'Content-Type': 'application/json',
  }
}

async function parse<T>(response: Response): Promise<T> {
  const text = await response.text()
  const data = text ? JSON.parse(text) : null
  if (!response.ok)
    throw new Error(data?.error || data?.detail || data?.title || `HTTP ${response.status}`)
  return data as T
}

export async function getSoftwareDelivery(
  token: string,
  subscriptionId: string,
  options?: { channel?: string; platform?: string; architecture?: string },
) {
  const query = new URLSearchParams()
  if (options?.channel) query.set('channel', options.channel)
  if (options?.platform) query.set('platform', options.platform)
  if (options?.architecture) query.set('architecture', options.architecture)
  const suffix = query.size ? `?${query.toString()}` : ''
  const response = await fetch(
    `${apiBase}/api/software/subscriptions/${subscriptionId}/delivery${suffix}`,
    { headers: headers(token) },
  )
  return parse<SoftwareDelivery>(response)
}

export async function listSoftwareReleases(token: string, productCode?: string) {
  const query = new URLSearchParams({ take: '100' })
  if (productCode) query.set('productCode', productCode)
  const response = await fetch(
    `${apiBase}/api/software/admin/releases?${query.toString()}`,
    { headers: headers(token) },
  )
  return parse<SoftwareRelease[]>(response)
}

export async function publishSoftwareRelease(
  token: string,
  request: SoftwareReleaseCreate,
) {
  const response = await fetch(`${apiBase}/api/software/admin/releases`, {
    method: 'POST',
    headers: headers(token),
    body: JSON.stringify(request),
  })
  return parse<SoftwareRelease>(response)
}

export async function deactivateSoftwareRelease(token: string, releaseId: string) {
  const response = await fetch(
    `${apiBase}/api/software/admin/releases/${releaseId}/deactivate`,
    { method: 'POST', headers: headers(token) },
  )
  if (!response.ok) {
    const text = await response.text()
    const data = text ? JSON.parse(text) : null
    throw new Error(data?.error || data?.detail || data?.title || `HTTP ${response.status}`)
  }
}


export async function listSoftwareDeliveryEvents(
  token: string,
  options?: { subscriptionId?: string; take?: number },
) {
  const query = new URLSearchParams({ take: String(options?.take ?? 100) })
  if (options?.subscriptionId) query.set('subscriptionId', options.subscriptionId)
  const response = await fetch(
    `${apiBase}/api/software/admin/delivery-events?${query.toString()}`,
    { headers: headers(token) },
  )
  return parse<SoftwareDeliveryEvent[]>(response)
}
