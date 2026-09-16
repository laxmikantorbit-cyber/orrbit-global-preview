import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { CrmAdvancedHub } from './CrmAdvancedHub.tsx'
import { CrmManagementHub } from './CrmManagementHub.tsx'
import { CrmDataMaintenanceHub } from './CrmDataMaintenanceHub.tsx'
import { CrmPipelineBoard } from './CrmPipelineBoard.tsx'
import { CrmAddressHub } from './CrmAddressHub.tsx'
import { CrmLeadQueryHub } from './CrmLeadQueryHub.tsx'
import { CrmAnalyticsHub } from './CrmAnalyticsHub.tsx'
import { CrmOpportunityProductHub } from './CrmOpportunityProductHub.tsx'
import { CrmInboxHub } from './CrmInboxHub.tsx'
import { CrmCommunicationHub } from './CrmCommunicationHub.tsx'
import { CrmIntelligenceHub } from './CrmIntelligenceHub.tsx'
import { CrmDataExportHub } from './CrmDataExportHub.tsx'

const path = window.location.pathname
const advancedLinkStyle = {
  position: 'fixed', right: '18px', bottom: '18px', zIndex: 9999,
  padding: '11px 16px', borderRadius: '12px', background: '#111827', color: '#fff',
  textDecoration: 'none', fontFamily: 'Inter, system-ui, sans-serif', fontSize: '13px', fontWeight: 800,
  boxShadow: '0 12px 30px rgba(15,23,42,.22)',
} as const
const managementLinkStyle = { ...advancedLinkStyle, bottom: '64px', background: '#1d4ed8' } as const
const maintenanceLinkStyle = { ...advancedLinkStyle, bottom: '110px', background: '#047857' } as const
const pipelineLinkStyle = { ...advancedLinkStyle, bottom: '156px', background: '#7c3aed' } as const
const addressLinkStyle = { ...advancedLinkStyle, bottom: '202px', background: '#b45309' } as const
const queryLinkStyle = { ...advancedLinkStyle, bottom: '248px', background: '#0f766e' } as const
const analyticsLinkStyle = { ...advancedLinkStyle, bottom: '294px', background: '#be123c' } as const
const productsLinkStyle = { ...advancedLinkStyle, bottom: '340px', background: '#4338ca' } as const
const inboxLinkStyle = { ...advancedLinkStyle, bottom: '386px', background: '#0369a1' } as const
const communicationLinkStyle = { ...advancedLinkStyle, bottom: '432px', background: '#a21caf' } as const
const intelligenceLinkStyle = { ...advancedLinkStyle, bottom: '478px', background: '#c2410c' } as const
const exportLinkStyle = { ...advancedLinkStyle, bottom: '524px', background: '#334155' } as const

const utilityLinks = <>
  <a href="/crm/advanced" style={advancedLinkStyle}>Advanced CRM →</a>
  <a href="/crm/manage" style={managementLinkStyle}>CRM Settings →</a>
  <a href="/crm/maintenance" style={maintenanceLinkStyle}>Data Maintenance →</a>
  <a href="/crm/pipeline-board" style={pipelineLinkStyle}>Drag Pipeline →</a>
  <a href="/crm/addresses" style={addressLinkStyle}>Customer Addresses →</a>
  <a href="/crm/leads-query" style={queryLinkStyle}>Advanced Lead Search →</a>
  <a href="/crm/analytics" style={analyticsLinkStyle}>Detailed Analytics →</a>
  <a href="/crm/opportunity-products" style={productsLinkStyle}>Deal Products →</a>
  <a href="/crm/inbox" style={inboxLinkStyle}>My CRM Day →</a>
  <a href="/crm/communications" style={communicationLinkStyle}>Communications →</a>
  <a href="/crm/intelligence" style={intelligenceLinkStyle}>AI Sales Command →</a>
  <a href="/crm/export" style={exportLinkStyle}>CSV / Excel Export →</a>
</>

const content = path === '/crm/export'
  ? <CrmDataExportHub />
  : path === '/crm/intelligence'
    ? <CrmIntelligenceHub />
    : path === '/crm/communications'
      ? <CrmCommunicationHub />
      : path === '/crm/inbox'
        ? <CrmInboxHub />
        : path === '/crm/opportunity-products'
          ? <CrmOpportunityProductHub />
          : path === '/crm/analytics'
            ? <CrmAnalyticsHub />
            : path === '/crm/leads-query'
              ? <CrmLeadQueryHub />
              : path === '/crm/addresses'
                ? <CrmAddressHub />
                : path === '/crm/pipeline-board'
                  ? <CrmPipelineBoard />
                  : path === '/crm/maintenance'
                    ? <CrmDataMaintenanceHub />
                    : path === '/crm/manage'
                      ? <CrmManagementHub />
                      : path === '/crm/advanced'
                        ? <><CrmAdvancedHub />{utilityLinks}</>
                        : path === '/crm'
                          ? <><App />{utilityLinks}</>
                          : <App />

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    {content}
  </StrictMode>,
)
