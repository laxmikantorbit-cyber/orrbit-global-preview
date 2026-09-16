import { apiBase } from './businessosApi'
import { getCrmDemoUserId, type CrmAccount, type CrmOpportunity } from './crmApi'

async function call<T>(path: string, body: unknown) {
  const headers = new Headers({ 'Content-Type': 'application/json' })
  const userId = getCrmDemoUserId()
  if (userId) headers.set('X-CRM-Demo-User-Id', userId)
  const response = await fetch(`${apiBase}/api/testing/public/crm${path}`, {
    method: 'POST', headers, body: JSON.stringify(body),
  })
  const text = await response.text()
  const data = text ? JSON.parse(text) : null
  if (!response.ok) throw new Error(data?.error || data?.detail || `HTTP ${response.status}`)
  return data as T
}

export async function updateCrmAccount(accountId: string, input: {
  name: string
  legalName?: string
  gstin?: string
  displayCode?: string
  status?: string
}) {
  return call<CrmAccount>(`/accounts/${accountId}/profile`, input)
}

export async function updateCrmContact(accountId: string, contactId: string, input: {
  name: string
  email?: string
  phone?: string
  isPrimary: boolean
}) {
  return call<CrmAccount>(`/accounts/${accountId}/contacts/${contactId}/profile`, input)
}

export async function deactivateCrmContact(accountId: string, contactId: string) {
  return call<CrmAccount>(`/accounts/${accountId}/contacts/${contactId}/deactivate`, {})
}

export async function mergeCrmLead(sourceLeadId: string, targetLeadId: string) {
  return call<{
    sourceLeadId: string
    targetLeadId: string
    followUpsMoved: number
    tasksMoved: number
    targetTags: string[]
  }>(`/leads/${sourceLeadId}/merge`, { targetLeadId })
}

export async function updateCrmOpportunity(opportunityId: string, input: {
  title: string
  estimatedValue: number
  currencyCode?: string
  probabilityPercent: number
  expectedCloseDate?: string
  ownerUserId?: string | null
}) {
  return call<CrmOpportunity>(`/opportunities/${opportunityId}/update`, {
    currencyCode: 'INR', ...input,
  })
}
