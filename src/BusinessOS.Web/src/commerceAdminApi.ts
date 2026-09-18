import { apiBase } from './businessosApi'

const headers = (token: string): HeadersInit => ({
  Authorization: `Bearer ${token}`,
  'Content-Type': 'application/json',
})

async function read<T>(response: Response): Promise<T> {
  const text = await response.text()
  const data = text ? JSON.parse(text) : null
  if (!response.ok) throw new Error(data?.error || data?.detail || data?.title || `HTTP ${response.status}`)
  return data as T
}

export type DeviceItem = {
  id: string
  deviceFingerprint: string
  deviceName?: string | null
  appVersion?: string | null
  active: boolean
  activatedAtUtc: string
  lastValidatedAtUtc?: string | null
}

export type DeviceInventory = {
  tenantId: string
  subscriptionId: string
  activeDesktopDevices: number
  desktopDeviceLimit: number
  devices: DeviceItem[]
}

export type DeviceLifecycleEvent = {
  id: string
  tenantId: string
  subscriptionId: string
  deviceFingerprint: string
  previousDeviceFingerprint?: string | null
  action: string
  outcome: string
  deviceName?: string | null
  appVersion?: string | null
  occurredAtUtc: string
}

export type DeviceLifecycleResponse = {
  tenantId: string
  subscriptionId: string
  events: DeviceLifecycleEvent[]
}
export type CommerceAdminCounts = {
  pendingOrders: number
  capturedPayments: number
  failedPayments: number
  activeSubscriptions: number
  renewals: number
  needsReconciliation: number
}

export type CommerceAdminOrder = {
  order: {
    commerceOrderId: string
    amount: number
    currencyCode: string
    orderStatus: string
    providerOrderId?: string | null
    razorpayOrderId?: string | null
    productCode?: string | null
    subscriptionId?: string | null
    createdAtUtc: string
  }
  paymentStatus?: string | null
  reconciliationStatus: string
}

export type CommerceAdminStatus = {
  tenantId: string
  generatedAtUtc: string
  counts: CommerceAdminCounts
  orders: CommerceAdminOrder[]
  payments: unknown[]
  activations: Array<{ subscriptionId: string; productCode: string; validUntil: string }>
  renewals: Array<{ subscriptionId: string; newValidUntil: string }>
}
export async function getAdminStatus(token: string) {
  return read<CommerceAdminStatus>(await fetch(`${apiBase}/api/commerce/admin/status`, {
    headers: headers(token),
  }))
}

export async function reconcileOrder(token: string, providerOrderId: string) {
  return read<Record<string, unknown>>(await fetch(
    `${apiBase}/api/commerce/admin/razorpay/orders/${encodeURIComponent(providerOrderId)}/reconcile`,
    { method: 'POST', headers: headers(token) },
  ))
}

export async function getDevices(token: string, subscriptionId: string) {
  return read<DeviceInventory>(await fetch(
    `${apiBase}/api/commerce/admin/subscriptions/${subscriptionId}/devices`,
    { headers: headers(token) },
  ))
}

export async function getDeviceEvents(token: string, subscriptionId: string) {
  return read<DeviceLifecycleResponse>(await fetch(
    `${apiBase}/api/commerce/admin/subscriptions/${subscriptionId}/devices/events?take=100`,
    { headers: headers(token) },
  ))
}

export async function revokeDevice(token: string, subscriptionId: string, deviceFingerprint: string) {
  return read<{ subscriptionId: string; revoked: boolean }>(await fetch(
    `${apiBase}/api/commerce/admin/subscriptions/${subscriptionId}/devices/revoke`,
    { method: 'POST', headers: headers(token), body: JSON.stringify({ deviceFingerprint }) },
  ))
}
export async function replaceDevice(token: string, subscriptionId: string, request: {
  oldDeviceFingerprint: string
  newDeviceFingerprint: string
  deviceName?: string
  appVersion?: string
}) {
  return read<Record<string, unknown>>(await fetch(
    `${apiBase}/api/commerce/admin/subscriptions/${subscriptionId}/devices/replace`,
    { method: 'POST', headers: headers(token), body: JSON.stringify(request) },
  ))
}

export async function getActivationCode(token: string, subscriptionId: string) {
  return read<{ activationCode: string }>(await fetch(
    `${apiBase}/api/desktop/licenses/${subscriptionId}/activation-code`,
    { method: 'POST', headers: headers(token) },
  ))
}
