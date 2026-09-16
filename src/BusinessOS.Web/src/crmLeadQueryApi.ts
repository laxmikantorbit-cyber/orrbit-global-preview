import { apiBase } from './businessosApi'
import { getCrmDemoUserId } from './crmApi'

export type CrmLeadQueryFilters = {
  q?: string
  status?: string
  priority?: string
  leadSource?: string
  product?: string
  tag?: string
  ownerUserId?: string
  createdFromUtc?: string
  createdToUtc?: string
  followUpFromUtc?: string
  followUpToUtc?: string
  page?: number
  pageSize?: number
}

export type CrmLeadQueryItem = {
  id: string
  title: string
  status: string
  priority: string
  leadSource?: string | null
  contactName?: string | null
  mobileNumber?: string | null
  email?: string | null
  productInterest?: string | null
  ownerUserId?: string | null
  nextFollowUpAtUtc?: string | null
  createdAtUtc: string
  updatedAtUtc: string
  tags: string[]
}

export type CrmLeadQueryResult = {
  total: number
  page: number
  pageSize: number
  leads: CrmLeadQueryItem[]
}

export async function queryCrmLeads(filters: CrmLeadQueryFilters) {
  const params = new URLSearchParams()
  Object.entries(filters).forEach(([key, value]) => {
    if (value !== undefined && value !== null && value !== '') params.set(key, String(value))
  })
  const headers = new Headers()
  const userId = getCrmDemoUserId()
  if (userId) headers.set('X-CRM-Demo-User-Id', userId)
  const response = await fetch(`${apiBase}/api/testing/public/crm/leads/query?${params}`, { headers })
  const text = await response.text()
  const data = text ? JSON.parse(text) : null
  if (!response.ok) throw new Error(data?.error || data?.detail || `HTTP ${response.status}`)
  return data as CrmLeadQueryResult
}
