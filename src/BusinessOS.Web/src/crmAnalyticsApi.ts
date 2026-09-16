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

export async function getCrmDetailedAnalytics() {
  const headers = new Headers()
  const userId = getCrmDemoUserId()
  if (userId) headers.set('X-CRM-Demo-User-Id', userId)
  const response = await fetch(`${apiBase}/api/testing/public/crm/reports/detailed`, { headers })
  const text = await response.text()
  const data = text ? JSON.parse(text) : null
  if (!response.ok) throw new Error(data?.error || data?.detail || `HTTP ${response.status}`)
  return data as CrmDetailedAnalytics
}
