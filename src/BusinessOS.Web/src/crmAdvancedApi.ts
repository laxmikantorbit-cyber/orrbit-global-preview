import { apiBase } from './businessosApi'

const CRM_DEMO_USER_KEY = 'businessos.crm.demoUserId'

async function parseResponse<T>(response: Response): Promise<T> {
  const text = await response.text()
  const data = text ? JSON.parse(text) : null
  if (!response.ok) throw new Error(data?.error || data?.detail || data?.title || `HTTP ${response.status}`)
  return data as T
}

async function crmFetch(path: string, init: RequestInit = {}) {
  const headers = new Headers(init.headers)
  const userId = window.localStorage.getItem(CRM_DEMO_USER_KEY)
  if (userId) headers.set('X-CRM-Demo-User-Id', userId)
  return fetch(`${apiBase}/api/testing/public/crm${path}`, { ...init, headers })
}

export type CrmDuplicateHit = { type: string; id: string; name: string; mobile?: string | null; email?: string | null }
export type CrmDuplicateCheck = { hasDuplicates: boolean; matches: CrmDuplicateHit[] }
export type CrmGlobalSearchHit = { type: string; id: string; title: string; status: string; subtitle?: string | null; secondary?: string | null }
export type CrmReportSummary = {
  totalLeads: number
  convertedLeads: number
  conversionPercent: number
  openPipelineValue: number
  weightedPipelineValue: number
  wonValue: number
  lostDeals: number
  openFollowUps: number
  overdueFollowUps: number
  openTasks: number
  overdueTasks: number
  leadSources: Record<string, number>
  opportunityStages: Record<string, number>
  lostReasons: Record<string, number>
}
export type CrmNotification = {
  type: string
  title: string
  detail: string
  leadId?: string | null
  recordId: string
  dueAtUtc?: string | null
  severity: string
}
export type CrmCustomer360 = {
  account: {
    id: string
    name: string
    legalName?: string | null
    gstin?: string | null
    status: string
    primaryContactName?: string | null
    primaryContactEmail?: string | null
    primaryContactPhone?: string | null
    contactCount: number
  }
  opportunities: Array<{
    id: string
    title: string
    stage: string
    estimatedValue: number
    currencyCode: string
    probabilityPercent: number
    expectedCloseDate?: string | null
  }>
  orders: Array<{ commerceOrderId: string; amount: number; currencyCode: string; orderStatus: string; paidAtUtc?: string | null; productCode?: string | null; subscriptionId?: string | null }>
  activations: Array<{ subscriptionId: string; licenseId: string; productCode: string; startsOn: string; validUntil: string }>
  renewals: Array<{ subscriptionId: string; renewalId: string; previousValidUntil: string; newValidUntil: string }>
  paidOrderValue: number
}

export async function checkCrmDuplicates(input: { mobile?: string; email?: string; business?: string }) {
  const params = new URLSearchParams()
  if (input.mobile) params.set('mobile', input.mobile)
  if (input.email) params.set('email', input.email)
  if (input.business) params.set('business', input.business)
  return parseResponse<CrmDuplicateCheck>(await crmFetch(`/duplicates?${params.toString()}`))
}

export async function setCrmLeadTag(leadId: string, tag: string, remove = false) {
  return parseResponse<{ leadId: string; tags: string[] }>(await crmFetch(`/leads/${leadId}/tags`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ tag, remove }),
  }))
}

export async function bulkUpdateCrmLeads(input: {
  leadIds: string[]
  status?: string
  priority?: string
  changeOwner?: boolean
  ownerUserId?: string | null
  reason?: string
  note?: string
}) {
  return parseResponse<{ updatedLeadIds: string[]; failed: Array<{ leadId: string; error: string }> }>(await crmFetch('/leads/bulk', {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(input),
  }))
}

export async function rescheduleCrmFollowUp(followUpId: string, input: { dueAtUtc: string; channel: string; purpose: string; ownerUserId?: string | null }) {
  return parseResponse(await crmFetch(`/follow-ups/${followUpId}/reschedule`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(input),
  }))
}

export async function cancelCrmFollowUp(followUpId: string, reason?: string) {
  return parseResponse(await crmFetch(`/follow-ups/${followUpId}/cancel`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ reason }),
  }))
}

export async function updateCrmTask(taskId: string, input: { title: string; details?: string; dueAtUtc?: string; priority: string; assigneeUserId?: string | null }) {
  return parseResponse(await crmFetch(`/tasks/${taskId}/update`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(input),
  }))
}

export async function cancelCrmTask(taskId: string) {
  return parseResponse(await crmFetch(`/tasks/${taskId}/cancel`, { method: 'POST', body: '{}' }))
}

export async function globalCrmSearch(query: string) {
  return parseResponse<{ results: CrmGlobalSearchHit[] }>(await crmFetch(`/global-search?q=${encodeURIComponent(query)}`))
}

export async function getCrmReportSummary() {
  return parseResponse<CrmReportSummary>(await crmFetch('/reports/summary'))
}

export async function getCrmNotifications() {
  return parseResponse<{ items: CrmNotification[] }>(await crmFetch('/notifications'))
}

export async function getCrmCustomer360(accountId: string) {
  return parseResponse<CrmCustomer360>(await crmFetch(`/accounts/${accountId}/customer360`))
}

export async function exitCrmEmployee(memberId: string, reassignToUserId: string) {
  return parseResponse<{ exitedUserId: string; reassignedToUserId: string; leadsReassigned: number; followUpsReassigned: number; tasksReassigned: number; opportunitiesReassigned: number }>(await crmFetch(`/team/${memberId}/exit`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ reassignToUserId }),
  }))
}
