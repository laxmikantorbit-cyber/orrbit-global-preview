import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { CrmAdvancedHub } from './CrmAdvancedHub.tsx'

const path = window.location.pathname
const advancedLinkStyle = {
  position: 'fixed', right: '18px', bottom: '18px', zIndex: 9999,
  padding: '11px 16px', borderRadius: '12px', background: '#111827', color: '#fff',
  textDecoration: 'none', fontFamily: 'Inter, system-ui, sans-serif', fontSize: '13px', fontWeight: 800,
  boxShadow: '0 12px 30px rgba(15,23,42,.22)',
} as const

const content = path === '/crm/advanced'
  ? <CrmAdvancedHub />
  : path === '/crm'
    ? <><App /><a href="/crm/advanced" style={advancedLinkStyle}>Advanced CRM →</a></>
    : <App />

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    {content}
  </StrictMode>,
)
