import { apiBase } from './businessosApi'
import { getCrmDemoUserId } from './crmApi'

export type CrmEntityActivity = {
  id: string
  tenantId: string
  entityType: string
  entityId: string
  contactId?: string | null
  channel: string
  summary: string
  details?: string | null
  actorUserId?: string | null
  occurredAtUtc: string
}

async function call<T>(path: string, init: RequestInit = {}) {
  const headers = new Headers(init.headers)
  const userId = getCrmDemoUserId()
  if (userId) headers.set('X-CRM-Demo-User-Id', userId)
  const response = await fetch(`${apiBase}/api/testing/public/crm${path}`, { ...init, headers })
  const text = await response.text()
  const data = text ? JSON.parse(text) : null
  if (!response.ok) throw new Error(data?.error || data?.detail || `HTTP ${response.status}`)
  return data as T
}

export async function listCrmEntityActivities(entityType: string, entityId: string) {
  return call<{ activities: CrmEntityActivity[] }>(`/activity/${encodeURIComponent(entityType)}/${entityId}`)
}

export async function addCrmEntityActivity(entityType: string, entityId: string, input: {
  channel: string
  summary: string
  details?: string
  occurredAtUtc?: string
}) {
  return call<CrmEntityActivity>(`/activity/${encodeURIComponent(entityType)}/${entityId}`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(input),
  })
}
