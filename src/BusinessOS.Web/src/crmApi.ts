import { apiBase } from './businessosApi'

async function parseResponse<T>(response: Response): Promise<T> {
  const text = await response.text()
  const data = text ? JSON.parse(text) : null
  if (!response.ok) {
    const message = data?.error || data?.detail || data?.title || `HTTP ${response.status}`
    throw new Error(message)
  }
  return data as T
}

export type CrmLead = {
  id: string
  organisationId: string
  title: string
  status: string
  leadSource?: string | null
  unqualifiedReason?: string | null
  createdSort: string
}

export type CrmDashboard = {
  totalLeads: number
  new: number
  contacted: number
  qualified: number
  converted: number
  unqualified: number
  statusCounts: Record<string, number>
}

export async function listCrmLeads() {
  const response = await fetch(`${apiBase}/api/testing/public/crm/leads`)
  return parseResponse<{ leads: CrmLead[] }>(response)
}

export async function crmDashboard() {
  const response = await fetch(`${apiBase}/api/testing/public/crm/dashboard`)
  return parseResponse<CrmDashboard>(response)
}

export async function createCrmLead(input: {
  title: string
  leadSource?: string
  contactName?: string
  mobileNumber?: string
  notes?: string
}) {
  const response = await fetch(`${apiBase}/api/testing/public/crm/leads`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  })
  return parseResponse<CrmLead>(response)
}

export async function changeCrmLeadStatus(
  leadId: string,
  status: string,
  reason?: string,
) {
  const response = await fetch(`${apiBase}/api/testing/public/crm/leads/${leadId}/status`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ status, reason }),
  })
  return parseResponse<CrmLead>(response)
}
