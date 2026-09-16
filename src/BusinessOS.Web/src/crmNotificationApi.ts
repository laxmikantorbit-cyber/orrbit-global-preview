import { apiBase } from './businessosApi'
import { getCrmDemoUserId } from './crmApi'

export type CrmPersistentNotification = {
  id: string
  tenantId: string
  userId: string
  sourceKey: string
  type: string
  title: string
  detail: string
  leadId?: string | null
  recordId?: string | null
  dueAtUtc?: string | null
  severity: string
  isRead: boolean
  active: boolean
  createdAtUtc: string
  updatedAtUtc: string
}

export type CrmNotificationInbox = {
  unreadCount: number
  totalCount: number
  items: CrmPersistentNotification[]
}

export type CrmDailySummary = {
  newLeads: number
  qualifiedLeads: number
  overdueFollowUps: number
  dueTodayFollowUps: number
  overdueTasks: number
  dueTodayTasks: number
  openOpportunities: number
  weightedPipelineValue: number
  unreadNotifications: number
  topActions: CrmPersistentNotification[]
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

export async function getCrmNotificationInbox(includeRead = true) {
  return call<CrmNotificationInbox>(`/notifications/inbox?includeRead=${includeRead}`)
}

export async function setCrmNotificationRead(notificationId: string, isRead: boolean) {
  return call<{ notificationId: string; isRead: boolean }>(`/notifications/${notificationId}/read`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ isRead }),
  })
}

export async function markAllCrmNotificationsRead() {
  return call<{ markedRead: number }>('/notifications/read-all', { method: 'POST', body: '{}' })
}

export async function getCrmDailySummary() {
  return call<CrmDailySummary>('/daily-summary')
}
