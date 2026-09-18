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
import { CrmContactDirectoryHub } from './CrmContactDirectoryHub.tsx'
import { CrmDealAgingHub } from './CrmDealAgingHub.tsx'
import { BusinessOSAdminHub } from './BusinessOSAdminHub.tsx'
import { BusinessOSCustomerPortal } from './BusinessOSCustomerPortal.tsx'

const path = window.location.pathname

const content = path === '/businessos/admin'
  ? <BusinessOSAdminHub />
  : path === '/businessos/portal'
    ? <BusinessOSCustomerPortal />
    : path === '/crm/deal-aging'
  ? <CrmDealAgingHub />
  : path === '/crm/contacts'
    ? <CrmContactDirectoryHub />
    : path === '/crm/export'
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
                            ? <CrmAdvancedHub />
                            : path === '/crm'
                              ? <App />
                              : <App />

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    {content}
  </StrictMode>,
)
