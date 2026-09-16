import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { CrmAdvancedHub } from './CrmAdvancedHub.tsx'
import { CrmManagementHub } from './CrmManagementHub.tsx'
import { CrmDataMaintenanceHub } from './CrmDataMaintenanceHub.tsx'
import { CrmPipelineBoard } from './CrmPipelineBoard.tsx'

const path = window.location.pathname
const advancedLinkStyle = {
  position: 'fixed', right: '18px', bottom: '18px', zIndex: 9999,
  padding: '11px 16px', borderRadius: '12px', background: '#111827', color: '#fff',
  textDecoration: 'none', fontFamily: 'Inter, system-ui, sans-serif', fontSize: '13px', fontWeight: 800,
  boxShadow: '0 12px 30px rgba(15,23,42,.22)',
} as const
const managementLinkStyle = {
  ...advancedLinkStyle,
  bottom: '64px',
  background: '#1d4ed8',
} as const
const maintenanceLinkStyle = {
  ...advancedLinkStyle,
  bottom: '110px',
  background: '#047857',
} as const
const pipelineLinkStyle = {
  ...advancedLinkStyle,
  bottom: '156px',
  background: '#7c3aed',
} as const

const content = path === '/crm/pipeline-board'
  ? <CrmPipelineBoard />
  : path === '/crm/maintenance'
    ? <CrmDataMaintenanceHub />
    : path === '/crm/manage'
      ? <CrmManagementHub />
      : path === '/crm/advanced'
        ? <><CrmAdvancedHub /><a href="/crm/manage" style={managementLinkStyle}>CRM Settings & Audit →</a><a href="/crm/maintenance" style={maintenanceLinkStyle}>Data Maintenance →</a><a href="/crm/pipeline-board" style={pipelineLinkStyle}>Drag Pipeline →</a></>
        : path === '/crm'
          ? <><App /><a href="/crm/advanced" style={advancedLinkStyle}>Advanced CRM →</a><a href="/crm/manage" style={managementLinkStyle}>CRM Settings →</a><a href="/crm/maintenance" style={maintenanceLinkStyle}>Data Maintenance →</a><a href="/crm/pipeline-board" style={pipelineLinkStyle}>Drag Pipeline →</a></>
          : <App />

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    {content}
  </StrictMode>,
)
