import { apiBase } from './businessosApi'
import { getCrmDemoUserId } from './crmApi'

export type CrmContactDirectoryItem = {
  accountId: string
  accountName: string
  contactId: string
  name: string
  designation?: string | null
  email?: string | null
  phone?: string | null
  isPrimary: boolean
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

export const listCrmContactDirectory = () => call<{ contacts: CrmContactDirectoryItem[] }>('/contacts/directory')
export const updateCrmContactDesignation = (accountId: string, contactId: string, designation: string) =>
  call<CrmContactDirectoryItem>(`/accounts/${accountId}/contacts/${contactId}/designation`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ designation }),
  })
