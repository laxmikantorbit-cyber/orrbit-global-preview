import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'
import { CrmAdvancedHub } from './CrmAdvancedHub.tsx'

const content = window.location.pathname === '/crm/advanced' ? <CrmAdvancedHub /> : <App />

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    {content}
  </StrictMode>,
)
