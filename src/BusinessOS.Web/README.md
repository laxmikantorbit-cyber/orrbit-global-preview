# BusinessOS Web Staging Checkout

React + Vite frontend for testing the oRRbit AI Repair checkout flow against the free staging API.

## Current mode

- API: `https://businessos-commerce-api-live.onrender.com`
- Environment: Staging / FreeTesting
- Storage: InMemory
- Payments: Razorpay simulator only
- Paid DB/live Razorpay: not used

## Run locally

```powershell
cd src/BusinessOS.Web
npm install
npm run dev
```
## Build

```powershell
npm run build
```

## Test flow

1. Open the frontend.
2. Paste the staging bearer token manually.
3. Click **Buy Now — Test Full Flow**.
4. Verify health, readiness, checkout, simulated capture, subscription, entitlement and admin status.

No token is committed in source code. Public website integration must never expose owner/admin tokens in production.
