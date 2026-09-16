import { apiBase } from './businessosApi'
import { getCrmDemoUserId } from './crmApi'

export type CrmAiLeadInsight = {
  engine: string
  leadId: string
  title: string
  priorityScore: number
  priorityBand: string
  isStale: boolean
  staleDays: number
  nextBestAction: string
  reasons: string[]
}

export type CrmAiOpportunityInsight = {
  engine: string
  opportunityId: string
  title: string
  productService?: string | null
  riskScore: number
  riskBand: string
  weightedValue: number
  nextBestAction: string
  reasons: string[]
}

export type CrmAiAction = { type: string; id: string; title: string; action: string; score: number }
export type CrmAiDailyBrief = {
  engine: string
  generatedAtUtc: string
  openLeads: number
  overdueFollowUps: number
  overdueTasks: number
  openOpportunities: number
  weightedPipelineValue: number
  topLeadPriorities: CrmAiLeadInsight[]
  atRiskOpportunities: CrmAiOpportunityInsight[]
  topActions: CrmAiAction[]
}
export type CrmAiAskHit = { type: string; id: string; title: string; status: string; detail?: string | null }
export type CrmAiAskResponse = { engine: string; question: string; intent: string; answer: string; results: CrmAiAskHit[] }

async function call<T>(path: string) {
  const headers = new Headers()
  const userId = getCrmDemoUserId()
  if (userId) headers.set('X-CRM-Demo-User-Id', userId)
  const response = await fetch(`${apiBase}/api/testing/public/crm/ai${path}`, { headers })
  const text = await response.text()
  const data = text ? JSON.parse(text) : null
  if (!response.ok) throw new Error(data?.error || data?.detail || `HTTP ${response.status}`)
  return data as T
}

export const getCrmAiDailyBrief = () => call<CrmAiDailyBrief>('/daily-brief')
export const getCrmAiLeadInsight = (leadId: string) => call<CrmAiLeadInsight>(`/leads/${leadId}`)
export const getCrmAiOpportunityInsight = (opportunityId: string) => call<CrmAiOpportunityInsight>(`/opportunities/${opportunityId}`)
export const askCrmAi = (question: string) => call<CrmAiAskResponse>(`/ask?q=${encodeURIComponent(question)}`)
