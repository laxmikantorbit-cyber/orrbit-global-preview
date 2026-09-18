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
const CRM_DEMO_USER_KEY = 'businessos.crm.demoUserId'

export function setCrmDemoUserId(userId?: string | null) {
  if (userId) window.localStorage.setItem(CRM_DEMO_USER_KEY, userId)
  else window.localStorage.removeItem(CRM_DEMO_USER_KEY)
}

export function getCrmDemoUserId() {
  return window.localStorage.getItem(CRM_DEMO_USER_KEY)
}

async function crmFetch(path: string, init: RequestInit = {}) {
  const headers = new Headers(init.headers)
  const userId = getCrmDemoUserId()
  if (userId) headers.set('X-CRM-Demo-User-Id', userId)
  return fetch(`${apiBase}/api/testing/public/crm${path}`, { ...init, headers })
}

export type CrmLead = {
  id: string
  organisationId: string
  title: string
  status: string
  leadSource?: string | null
  contactName?: string | null
  mobileNumber?: string | null
  email?: string | null
  productInterest?: string | null
  notes?: string | null
  priority?: string
  ownerUserId?: string | null
  unqualifiedReason?: string | null
  createdAtUtc?: string
  updatedAtUtc?: string
  lastContactAtUtc?: string | null
  nextFollowUpAtUtc?: string | null
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

export type CrmActivity = {
  id: string
  leadId: string
  type: string
  summary: string
  details?: string | null
  actorUserId?: string | null
  occurredAtUtc: string
}

export type CrmFollowUp = {
  id: string
  leadId: string
  dueAtUtc: string
  channel: string
  purpose: string
  ownerUserId?: string | null
  status: string
  outcome?: string | null
  createdAtUtc: string
  completedAtUtc?: string | null
}
export type CrmTask = {
  id: string
  leadId?: string | null
  title: string
  details?: string | null
  dueAtUtc?: string | null
  priority: string
  assigneeUserId?: string | null
  status: string
  createdAtUtc: string
  completedAtUtc?: string | null
}

export type CrmLeadWorkspace = {
  lead: CrmLead & { tags?: string[] }
  activities: CrmActivity[]
  followUps: CrmFollowUp[]
  tasks: CrmTask[]
}

export type CrmWorkSummary = {
  openFollowUps: number
  overdueFollowUps: number
  dueTodayFollowUps: number
  openTasks: number
  overdueTasks: number
}

export async function listCrmLeads() {
  const response = await crmFetch(`/leads`)
  return parseResponse<{ leads: CrmLead[] }>(response)
}
export async function crmDashboard() {
  const response = await crmFetch(`/dashboard`)
  return parseResponse<CrmDashboard>(response)
}

export async function crmWorkSummary() {
  const response = await crmFetch(`/work-summary`)
  return parseResponse<CrmWorkSummary>(response)
}

export async function createCrmLead(input: {
  title: string
  leadSource?: string
  contactName?: string
  mobileNumber?: string
  email?: string
  productInterest?: string
  notes?: string
  priority?: string
}) {
  const response = await crmFetch(`/leads`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  })
  return parseResponse<CrmLead>(response)
}

export async function changeCrmLeadStatus(leadId: string, status: string, reason?: string) {
  const response = await crmFetch(`/leads/${leadId}/status`, {    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ status, reason }),
  })
  return parseResponse<CrmLead>(response)
}

export async function getCrmLeadWorkspace(leadId: string) {
  const response = await crmFetch(`/leads/${leadId}/workspace`)
  return parseResponse<CrmLeadWorkspace>(response)
}

export async function updateCrmLeadProfile(leadId: string, input: {
  title: string
  contactName?: string
  mobileNumber?: string
  email?: string
  productInterest?: string
  notes?: string
}) {
  const response = await crmFetch(`/leads/${leadId}/profile`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  })
  return parseResponse<CrmLead>(response)
}

export async function changeCrmLeadPriority(leadId: string, priority: string) {
  const response = await crmFetch(`/leads/${leadId}/priority`, {    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ priority }),
  })
  return parseResponse<CrmLead>(response)
}

export async function addCrmActivity(leadId: string, input: {
  type: string
  summary: string
  details?: string
}) {
  const response = await crmFetch(`/leads/${leadId}/activities`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  })
  return parseResponse<CrmActivity>(response)
}

export async function createCrmFollowUp(leadId: string, input: {
  dueAtUtc: string
  channel: string
  purpose: string
  ownerUserId?: string
}) {
  const response = await crmFetch(`/leads/${leadId}/follow-ups`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  })
  return parseResponse<CrmFollowUp>(response)
}
export async function listCrmFollowUps() {
  const response = await crmFetch(`/follow-ups`)
  return parseResponse<{ followUps: CrmFollowUp[] }>(response)
}

export async function completeCrmFollowUp(followUpId: string, outcome?: string) {
  const response = await crmFetch(`/follow-ups/${followUpId}/complete`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ outcome }),
  })
  return parseResponse<CrmFollowUp>(response)
}

export async function listCrmTasks() {
  const response = await crmFetch(`/tasks`)
  return parseResponse<{ tasks: CrmTask[] }>(response)
}

export async function createCrmTask(input: {
  title: string
  leadId?: string
  details?: string
  dueAtUtc?: string
  priority?: string
  assigneeUserId?: string
}) {
  const response = await crmFetch(`/tasks`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  })
  return parseResponse<CrmTask>(response)
}

export async function completeCrmTask(taskId: string) {
  const response = await crmFetch(`/tasks/${taskId}/complete`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: '{}',
  })
  return parseResponse<CrmTask>(response)
}

export type CrmContact = {
  id: string
  name: string
  email?: string | null
  phone?: string | null
  isPrimary: boolean
}

export type CrmAccount = {
  id: string
  name: string
  legalName?: string | null
  gstin?: string | null
  displayCode?: string | null
  status: string
  primaryContact?: CrmContact | null
  contacts: CrmContact[]
}

export type CrmOpportunity = {
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
export async function listCrmAccounts() {
  const response = await crmFetch(`/accounts`)
  return parseResponse<{ accounts: CrmAccount[] }>(response)
}

export async function createCrmAccount(input: {
  name: string
  legalName?: string
  gstin?: string
  displayCode?: string
  contactName?: string
  email?: string
  phone?: string
}) {
  const response = await crmFetch(`/accounts`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(input),
  })
  return parseResponse<CrmAccount>(response)
}

export async function addCrmContact(accountId: string, input: {
  name: string
  email?: string
  phone?: string
  isPrimary?: boolean
}) {
  const response = await crmFetch(`/accounts/${accountId}/contacts`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(input),
  })
  return parseResponse<CrmAccount>(response)
}
export async function listCrmOpportunities() {
  const response = await crmFetch(`/opportunities`)
  return parseResponse<{ opportunities: CrmOpportunity[] }>(response)
}

export async function createCrmOpportunity(input: {
  accountId: string
  title: string
  estimatedValue: number
  currencyCode?: string
  probabilityPercent?: number
  expectedCloseDate?: string
  originatingLeadId?: string
  productService?: string
}) {
  const response = await crmFetch(`/opportunities`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ currencyCode: 'INR', probabilityPercent: 50, ...input }),
  })
  return parseResponse<CrmOpportunity>(response)
}

export async function changeCrmOpportunityStage(opportunityId: string, stage: string, reason?: string) {
  const response = await crmFetch(`/opportunities/${opportunityId}/stage`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ stage, reason }),
  })
  return parseResponse<CrmOpportunity>(response)
}
export async function convertCrmLead(leadId: string, input: {
  accountName?: string
  opportunityTitle?: string
  estimatedValue: number
  currencyCode?: string
  probabilityPercent?: number
  expectedCloseDate?: string
}) {
  const response = await crmFetch(`/leads/${leadId}/convert`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ currencyCode: 'INR', probabilityPercent: 60, ...input }),
  })
  return parseResponse<{ account: CrmAccount; opportunity: CrmOpportunity }>(response)
}

export type CrmRole = {
  role: string
  permissions: string[]
}

export type CrmTeamMember = {
  id: string
  displayName: string
  email: string
  mobileNumber?: string | null
  role: string
  active: boolean
  createdAtUtc: string
  permissions: string[]
}
export type CrmSession = {
  member: CrmTeamMember
  canViewAllOwnedRecords: boolean
}

export async function getCrmSession() {
  const response = await crmFetch('/session')
  return parseResponse<CrmSession>(response)
}

export async function listCrmRoles() {
  const response = await crmFetch(`/roles`)
  return parseResponse<{ roles: CrmRole[] }>(response)
}

export async function listCrmTeam() {
  const response = await crmFetch(`/team`)
  return parseResponse<{ members: CrmTeamMember[] }>(response)
}

export async function createCrmTeamMember(input: {
  displayName: string
  email: string
  mobileNumber?: string
  role: string
}) {
  const response = await crmFetch(`/team`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(input),
  })
  return parseResponse<CrmTeamMember>(response)
}

export async function changeCrmTeamRole(memberId: string, role: string) {
  const response = await crmFetch(`/team/${memberId}/role`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ role }),
  })
  return parseResponse<CrmTeamMember>(response)
}

export async function changeCrmTeamStatus(memberId: string, active: boolean) {
  const response = await crmFetch(`/team/${memberId}/status`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ active }),
  })
  return parseResponse<CrmTeamMember>(response)
}

export async function assignCrmLead(leadId: string, ownerUserId?: string | null) {
  const response = await crmFetch(`/leads/${leadId}/assign`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ ownerUserId }),
  })
  return parseResponse<CrmLead>(response)
}

export type CrmGlobalSearchHit = {
  type: string
  id: string
  title: string
  status: string
  subtitle?: string | null
  secondary?: string | null
}

export async function globalCrmSearch(q: string) {
  const response = await crmFetch(`/global-search?q=${encodeURIComponent(q)}`)
  return parseResponse<{ results: CrmGlobalSearchHit[] }>(response)
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
  const response = await crmFetch(`/leads/bulk`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(input),
  })
  return parseResponse<{ updated: string[]; failed: Array<{ leadId: string; reason: string }> }>(response)
}

export async function updateCrmLeadTag(leadId: string, tag: string, remove = false) {
  const response = await crmFetch(`/leads/${leadId}/tags`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ tag, remove }),
  })
  return parseResponse<{ leadId: string; tags: string[] }>(response)
}


export type CrmNotificationItem = {
  type: string
  title: string
  detail: string
  leadId?: string | null
  recordId: string
  dueAtUtc?: string | null
  severity: string
}

export async function getCrmNotifications() {
  const response = await crmFetch(`/notifications`)
  return parseResponse<{ items: CrmNotificationItem[] }>(response)
}

