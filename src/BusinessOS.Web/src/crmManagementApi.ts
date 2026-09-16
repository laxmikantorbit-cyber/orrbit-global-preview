import { apiBase } from './businessosApi'
import { getCrmDemoUserId } from './crmApi'

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

export type CrmSavedView = {
  id: string
  tenantId: string
  userId: string
  module: string
  name: string
  filtersJson: string
  isDefault: boolean
  createdAtUtc: string
  updatedAtUtc: string
}

export type CrmMasterItem = {
  id: string
  tenantId: string
  category: string
  code: string
  name: string
  active: boolean
  sortOrder: number
  updatedAtUtc: string
}

export type CrmAuditEntry = {
  id: string
  tenantId: string
  actorUserId?: string | null
  action: string
  entityType: string
  entityId?: string | null
  detail?: string | null
  createdAtUtc: string
}

export async function listCrmSavedViews() {
  return call<{ views: CrmSavedView[] }>('/saved-views')
}

export async function saveCrmSavedView(input: {
  id?: string
  module: string
  name: string
  filtersJson: string
  isDefault: boolean
}) {
  return call<CrmSavedView>('/saved-views', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  })
}

export async function deleteCrmSavedView(id: string) {
  return call<{ deleted: boolean; id: string }>(`/saved-views/${id}/delete`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}',
  })
}

export async function listCrmMasters(category?: string) {
  const suffix = category ? `?category=${encodeURIComponent(category)}` : ''
  return call<{ masters: CrmMasterItem[] }>(`/masters${suffix}`)
}

export async function saveCrmMaster(input: {
  id?: string
  category: string
  code: string
  name: string
  active: boolean
  sortOrder: number
}) {
  return call<CrmMasterItem>('/masters', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  })
}

export async function listCrmAudit(take = 100) {
  return call<{ audit: CrmAuditEntry[] }>(`/audit?take=${take}`)
}
