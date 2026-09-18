const DEFAULT_API_BASE = 'https://businessos-commerce-api-live.onrender.com'

export const apiBase =
  (import.meta.env.VITE_BUSINESSOS_API_BASE as string | undefined)?.replace(/\/$/, '') ||
  DEFAULT_API_BASE

export type ApiError = {
  error?: string
  title?: string
  detail?: string
  status?: number
}

async function parseResponse<T>(response: Response): Promise<T> {
  const text = await response.text()
  const data = text ? JSON.parse(text) : null
  if (!response.ok) {
    const message =
      data?.error || data?.detail || data?.title || `HTTP ${response.status}`
    throw new Error(message)
  }
  return data as T
}
export type CheckoutOrder = {
  commerceOrderId: string
  productCode: string
  razorpayOrderId: string
  razorpayKeyId: string
  razorpayAmount: number
  razorpayCurrency: string
  razorpayStatus: string
}

export type ActivationResponse = {
  tenantId: string
  organisationId: string
  commerceOrderId: string
  subscriptionId: string
  licenseId: string
  productCode: string
  startsOn: string
  validUntil: string
}
export type CaptureResponse = {
  paymentOutcome: string
  activationOutcome: string
  duplicatePaymentEvent: boolean
  paymentId?: string
  providerOrderId: string
  initialActivation?: ActivationResponse
}

export type EntitlementResponse = {
  status: string
  renewalStatus: string
  autoRenewEnabled: boolean
  cancelAtPeriodEnd: boolean
  graceEndsOn?: string | null
  validUntil: string
  autoPayProviderStatus?: string | null
}

export type SubscriptionListItem = {
  subscriptionId: string
  organisationId: string
  productCode: string
  startsOn: string
  validUntil: string
  status: string
  renewalStatus: string
  cancelAtPeriodEnd: boolean
}

export type AutoPaySetupResponse = {
  tenantId: string
  subscriptionId: string
  provider: string
  providerSubscriptionId: string
  providerPlanId: string
  status: string
  providerPublicKeyId: string
  authorizationUrl?: string | null
  startAtUnix: number
  totalCount: number
  existingBinding: boolean
}

export type AutoPayStatus = {
  providerSubscriptionId: string
  status: string
  autoRenewEnabled: boolean
  cancelAtPeriodEnd: boolean
  authorizationUrl?: string | null
}

export type AutoPayAuthorizationResponse = {
  tenantId: string
  subscriptionId: string
  providerSubscriptionId: string
  status: string
  autoRenewEnabled: boolean
  updatedAtUtc: string
}

export type AdminSnapshot = {
  tenantId: string
  generatedAtUtc: string
  pendingOrders: unknown[]
  activations: unknown[]
  renewals: unknown[]
}
function authHeaders(token: string): HeadersInit {
  return {
    Authorization: `Bearer ${token}`,
    'Content-Type': 'application/json',
  }
}

export async function health() {
  const response = await fetch(`${apiBase}/health`)
  return parseResponse<{ status: string }>(response)
}

export async function readiness() {
  const response = await fetch(`${apiBase}/health/ready`)
  return parseResponse<Record<string, unknown>>(response)
}
export async function createInitialCheckout(token: string) {
  const response = await fetch(`${apiBase}/api/commerce/checkout/initial`, {
    method: 'POST',
    headers: authHeaders(token),
    body: JSON.stringify({
      organisationId: '11111111-1111-1111-1111-111111111111',
      productCode: 'AI_REPAIR',
      planId: crypto.randomUUID(),
      planVersionId: crypto.randomUUID(),
      planVersionNumber: 1,
      amount: 29999,
      currencyCode: 'INR',
      termMonths: 12,
      desktopDeviceLimit: 1,
      locationLimit: 1,
      webAdminSeats: 10,
      fieldStaffSeats: 10,
      multiLocationCloud: true,
    }),
  })
  return parseResponse<CheckoutOrder>(response)
}

export async function captureFreePayment(token: string, razorpayOrderId: string) {
  const response = await fetch(
    `${apiBase}/api/testing/payments/razorpay/orders/${razorpayOrderId}/capture`,
    {
      method: 'POST',
      headers: authHeaders(token),
      body: JSON.stringify({ paymentId: null, capturedAtUtc: null }),
    },
  )
  return parseResponse<CaptureResponse>(response)
}
export async function listSubscriptions(token: string) {
  const response = await fetch(
    `${apiBase}/api/commerce/subscriptions?take=100`,
    { headers: authHeaders(token) },
  )
  return parseResponse<SubscriptionListItem[]>(response)
}

export async function getSubscription(token: string, subscriptionId: string) {
  const response = await fetch(
    `${apiBase}/api/commerce/subscriptions/${subscriptionId}`,
    { headers: authHeaders(token) },
  )
  return parseResponse<ActivationResponse>(response)
}

export async function getEntitlement(token: string, subscriptionId: string) {
  const response = await fetch(
    `${apiBase}/api/commerce/subscriptions/${subscriptionId}/entitlement`,
    { headers: authHeaders(token) },
  )
  return parseResponse<EntitlementResponse>(response)
}
export async function cancelAtPeriodEnd(token: string, subscriptionId: string) {
  const response = await fetch(
    `${apiBase}/api/commerce/subscriptions/${subscriptionId}/cancel-at-period-end`,
    { method: 'POST', headers: authHeaders(token) },
  )
  return parseResponse<EntitlementResponse>(response)
}

export async function setupAutoPay(token: string, subscriptionId: string) {
  const response = await fetch(`${apiBase}/api/commerce/subscriptions/${subscriptionId}/autopay/setup`, {
    method: 'POST', headers: authHeaders(token),
  })
  return parseResponse<AutoPaySetupResponse>(response)
}

export async function getAutoPayStatus(token: string, subscriptionId: string) {
  const response = await fetch(`${apiBase}/api/commerce/subscriptions/${subscriptionId}/autopay`, {
    headers: authHeaders(token),
  })
  return parseResponse<AutoPayStatus>(response)
}

export async function authorizeAutoPay(token: string, subscriptionId: string, request: {
  razorpayPaymentId: string
  razorpaySubscriptionId: string
  razorpaySignature: string
}) {
  const response = await fetch(`${apiBase}/api/commerce/subscriptions/${subscriptionId}/autopay/authorize`, {
    method: 'POST', headers: authHeaders(token), body: JSON.stringify(request),
  })
  return parseResponse<AutoPayAuthorizationResponse>(response)
}

export async function simulateAutoPayRenewal(token: string, subscriptionId: string) {
  const response = await fetch(`${apiBase}/api/testing/payments/razorpay/subscriptions/${subscriptionId}/charge`, {
    method: 'POST', headers: authHeaders(token), body: JSON.stringify({ providerOrderId: null, paymentId: null, capturedAtUtc: null }),
  })
  return parseResponse<Record<string, unknown>>(response)
}

export async function getAdminStatus(token: string) {
  const response = await fetch(`${apiBase}/api/commerce/admin/status`, {
    headers: authHeaders(token),
  })
  return parseResponse<AdminSnapshot>(response)
}

export type LicenseActivationCodeResponse = {
  tenantId: string
  organisationId: string
  subscriptionId: string
  licenseId: string
  productCode: string
  activationCode: string
  createdAtUtc: string
}

export type PublicDemoPurchaseResponse = {
  checkout: CheckoutOrder
  duplicatePaymentEvent: boolean
  activation: ActivationResponse
  entitlement?: EntitlementResponse | null
  activationCode?: LicenseActivationCodeResponse | null
}

export async function publicDemoPurchase() {
  const response = await fetch(`${apiBase}/api/testing/public/ai-repair/purchase`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
  })
  return parseResponse<PublicDemoPurchaseResponse>(response)
}
