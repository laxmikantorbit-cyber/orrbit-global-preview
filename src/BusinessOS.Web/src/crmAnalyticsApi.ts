import { apiBase } from './businessosApi'
import { getCrmDemoUserId } from './crmApi'

export type CrmExecutivePerformance = {
  userId: string
  userName: string
  leads: number
  convertedLeads: number
  conversionPercent: number
  completedFollowUps: number
  completedTasks: number
  openOpportunities: number
  openPipelineValue: number
  weightedPipelineValue: number
  wonValue: number
}

export type CrmProductivity = {
  userId?: string | null
  userName: string
  total: number
  open: number
  completed: number
  cancelled: number
  overdue: number
}

export type CrmForecastBucket = {
  label: string
  opportunityCount: number
  pipelineValue: number
  weightedValue: number
}

export type CrmAccountBusiness = {
  accountId: string
  accountName: string
  opportunityCount: number
  openPipelineValue: number
  weightedPipelineValue: number
  wonValue: number
}

export type CrmMonthlyTrend = {
  month: string
  leadsCreated: number
  leadsConverted: number
  expectedClosures: number
}

export type CrmDetailedAnalytics = {
  executivePerformance: CrmExecutivePerformance[]
  followUpProductivity: CrmProductivity[]
  taskPerformance: CrmProductivity[]
  ownerForecast: CrmForecastBucket[]
  monthForecast: CrmForecastBucket[]
  accountBusiness: CrmAccountBusiness[]
  monthlyTrends: CrmMonthlyTrend[]
}

export type CrmLeadAgingBucket = {
  label: string
  leadAgeCount: number
  inactivityCount: number
}

export type CrmLeadAgingItem = {
  leadId: string
  title: string
  status: string
  priority: string
  ownerUserId?: string | null
  ownerName: string
  ageDays: number
  inactiveDays: number
  nextFollowUpAtUtc?: string | null
  productInterest?: string | null
}

export type CrmLeadAging = {
  openLeadCount: number
  buckets: CrmLeadAgingBucket[]
  leads: CrmLeadAgingItem[]
}

function headers() {
  const value = new Headers()
  const userId = getCrmDemoUserId()
  if (userId) value.set('X-CRM-Demo-User-Id', userId)
  return value
}

async function get<T>(path: string) {
  const response = await fetch(`${apiBase}/api/testing/public/crm${path}`, { headers: headers() })
  const text = await response.text()
  const data = text ? JSON.parse(text) : null
  if (!response.ok) throw new Error(data?.error || data?.detail || `HTTP ${response.status}`)
  return data as T
}

export const getCrmDetailedAnalytics = () => get<CrmDetailedAnalytics>('/reports/detailed')
export const getCrmLeadAging = () => get<CrmLeadAging>('/reports/lead-aging')
