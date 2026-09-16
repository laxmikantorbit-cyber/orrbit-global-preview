import { apiBase } from './businessosApi'
import { getCrmDemoUserId } from './crmApi'

export type CrmAddress = {
  id: string
  line1: string
  line2?: string | null
  city: string
  state: string
  postalCode: string
  countryCode: string
  isPrimary: boolean
}

async function request<T>(path: string, init: RequestInit = {}) {
  const headers = new Headers(init.headers)
  const userId = getCrmDemoUserId()
  if (userId) headers.set('X-CRM-Demo-User-Id', userId)
  const response = await fetch(`${apiBase}/api/testing/public/crm${path}`, { ...init, headers })
  const text = await response.text()
  const data = text ? JSON.parse(text) : null
  if (!response.ok) throw new Error(data?.error || data?.detail || `HTTP ${response.status}`)
  return data as T
}

export async function listCrmAddresses(accountId: string) {
  return request<{ addresses: CrmAddress[] }>(`/accounts/${accountId}/addresses`)
}

export async function addCrmAddress(accountId: string, input: Omit<CrmAddress, 'id'>) {
  return request<CrmAddress>(`/accounts/${accountId}/addresses`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(input),
  })
}

export async function updateCrmAddress(accountId: string, addressId: string, input: Omit<CrmAddress, 'id'>) {
  return request<CrmAddress>(`/accounts/${accountId}/addresses/${addressId}/update`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(input),
  })
}

export async function deactivateCrmAddress(accountId: string, addressId: string) {
  return request<{ removed: boolean; addressId: string; addresses: CrmAddress[] }>(`/accounts/${accountId}/addresses/${addressId}/deactivate`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: '{}',
  })
}
