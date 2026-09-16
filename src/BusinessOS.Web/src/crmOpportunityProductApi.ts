import { apiBase } from './businessosApi'
import { getCrmDemoUserId } from './crmApi'

export type CrmDetailedOpportunity = {
  id: string
  accountId: string
  originatingLeadId?: string | null
  title: string
  productService?: string | null
  stage: string
  estimatedValue: number
  currencyCode: string
  probabilityPercent: number
  expectedCloseDate?: string | null
  ownerUserId?: string | null
  lossReason?: string | null
}

async function crmCall<T>(path: string, init: RequestInit = {}) {
  const headers = new Headers(init.headers)
  const userId = getCrmDemoUserId()
  if (userId) headers.set('X-CRM-Demo-User-Id', userId)
  const response = await fetch(`${apiBase}/api/testing/public/crm${path}`, { ...init, headers })
  const text = await response.text()
  const data = text ? JSON.parse(text) : null
  if (!response.ok) throw new Error(data?.error || data?.detail || `HTTP ${response.status}`)
  return data as T
}

export async function listDetailedCrmOpportunities() {
  return crmCall<{ opportunities: CrmDetailedOpportunity[] }>('/opportunities/detailed')
}

export async function updateCrmOpportunityProductService(opportunityId: string, productService?: string | null) {
  return crmCall<{ id: string; productService?: string | null }>(`/opportunities/${opportunityId}/product-service`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ productService }),
  })
}
