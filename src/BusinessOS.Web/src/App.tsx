import { useMemo, useState } from 'react'
import './App.css'
import { CrmDemo } from './CrmDemo'
import { openRazorpaySubscriptionAuthorization } from './razorpaySubscriptionCheckout'
import {
  apiBase,
  authorizeAutoPay,
  cancelAtPeriodEnd,
  captureFreePayment,
  createInitialCheckout,
  getAdminStatus,
  getAutoPayStatus,
  getEntitlement,
  getSubscription,
  health,
  publicDemoPurchase,
  readiness,
  setupAutoPay,
  simulateAutoPayRenewal,
  type EntitlementResponse,
} from './businessosApi'

type StepStatus = 'pending' | 'running' | 'done' | 'error'

type Step = {
  key: string
  label: string
  status: StepStatus
  detail?: string
}
const initialSteps: Step[] = [
  { key: 'health', label: 'API health', status: 'pending' },
  { key: 'ready', label: 'Free staging readiness', status: 'pending' },
  { key: 'checkout', label: 'Create checkout order', status: 'pending' },
  { key: 'capture', label: 'Simulate payment capture', status: 'pending' },
  { key: 'subscription', label: 'Read subscription/license', status: 'pending' },
  { key: 'entitlement', label: 'Read entitlement status', status: 'pending' },
  { key: 'admin', label: 'Read admin status', status: 'pending' },
]

function statusText(status: StepStatus) {
  if (status === 'done') return 'Done'
  if (status === 'running') return 'Running'
  if (status === 'error') return 'Error'
  return 'Pending'
}

function json(data: unknown) {
  return JSON.stringify(data, null, 2)
}
function App() {
  const [token, setToken] = useState('')
  const [steps, setSteps] = useState<Step[]>(initialSteps)
  const [output, setOutput] = useState('Ready for staging checkout test.')
  const [subscriptionId, setSubscriptionId] = useState('')
  const [activationCode, setActivationCode] = useState('')
  const [entitlement, setEntitlement] = useState<EntitlementResponse | null>(null)
  const [autoPayBusy, setAutoPayBusy] = useState(false)

  const canRunProtected = useMemo(() => token.trim().length > 0, [token])

  if (window.location.pathname === '/crm') return <CrmDemo />

  function mark(key: string, status: StepStatus, detail?: string) {
    setSteps((items) =>
      items.map((item) => (item.key === key ? { ...item, status, detail } : item)),
    )
  }

  function reset() {
    setSteps(initialSteps)
    setOutput('Ready for staging checkout test.')
    setSubscriptionId('')
    setActivationCode('')
    setEntitlement(null)
  }

  async function runPublicChecks() {
    reset()
    try {
      mark('health', 'running')
      const healthResult = await health()
      mark('health', 'done', healthResult.status)

      mark('ready', 'running')
      const readyResult = await readiness()
      mark('ready', 'done', String(readyResult.environment ?? 'ready'))
      setOutput(json({ healthResult, readyResult }))
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error)
      setOutput(message)
      setSteps((items) =>
        items.map((item) =>
          item.status === 'running' ? { ...item, status: 'error', detail: message } : item,
        ),
      )
    }
  }

  async function runFullFlow() {
    reset()
    try {
      mark('health', 'running')
      const healthResult = await health()
      mark('health', 'done', healthResult.status)

      mark('ready', 'running')
      const readyResult = await readiness()
      mark('ready', 'done', String(readyResult.environment ?? 'ready'))

      if (!canRunProtected) {
        mark('checkout', 'running')
        const purchase = await publicDemoPurchase()
        mark('checkout', 'done', purchase.checkout.razorpayOrderId)
        mark('capture', 'done', 'public_demo_captured')
        mark('subscription', 'done', purchase.activation.licenseId)
        if (purchase.entitlement) {
          setEntitlement(purchase.entitlement)
          mark('entitlement', 'done', purchase.entitlement.status)
        }
        mark('admin', 'pending', 'Paste token to view admin status')
        setSubscriptionId(purchase.activation.subscriptionId)
        setActivationCode(purchase.activationCode?.activationCode ?? '')
        setOutput(json({ mode: 'public_demo_no_token', healthResult, readyResult, purchase }))
        return
      }

      mark('checkout', 'running')
      const checkout = await createInitialCheckout(token.trim())
      mark('checkout', 'done', checkout.razorpayOrderId)
      mark('capture', 'running')
      const capture = await captureFreePayment(token.trim(), checkout.razorpayOrderId)
      mark('capture', 'done', capture.paymentOutcome)
      const activatedSubscriptionId = capture.initialActivation?.subscriptionId
      if (!activatedSubscriptionId) throw new Error('Activation response missing subscription id.')
      setSubscriptionId(activatedSubscriptionId)

      mark('subscription', 'running')
      const subscription = await getSubscription(token.trim(), activatedSubscriptionId)
      mark('subscription', 'done', subscription.licenseId)

      mark('entitlement', 'running')
      const entitlementResult = await getEntitlement(token.trim(), activatedSubscriptionId)
      setEntitlement(entitlementResult)
      mark('entitlement', 'done', entitlementResult.status)
      mark('admin', 'running')
      const admin = await getAdminStatus(token.trim())
      mark('admin', 'done', `${admin.activations.length} activation(s)`)

      setOutput(json({ healthResult, readyResult, checkout, capture, subscription, entitlementResult, admin }))
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error)
      setOutput(message)
      setSteps((items) =>
        items.map((item) =>
          item.status === 'running' ? { ...item, status: 'error', detail: message } : item,
        ),
      )
    }
  }
  async function setupAutoPayForLastSubscription() {
    if (!subscriptionId || !canRunProtected || autoPayBusy) return
    setAutoPayBusy(true)
    try {
      const setup = await setupAutoPay(token.trim(), subscriptionId)
      if (setup.providerPublicKeyId === 'rzp_test_free_testing') {
        const status = await getAutoPayStatus(token.trim(), subscriptionId)
        setOutput(json({ autoPaySetup: setup, autoPayStatus: status, next: 'Use the simulator button to test a recurring renewal charge.' }))
        return
      }
      const authorization = await openRazorpaySubscriptionAuthorization(setup)
      const verified = await authorizeAutoPay(token.trim(), subscriptionId, {
        razorpayPaymentId: authorization.razorpay_payment_id,
        razorpaySubscriptionId: authorization.razorpay_subscription_id,
        razorpaySignature: authorization.razorpay_signature,
      })
      const status = await getAutoPayStatus(token.trim(), subscriptionId)
      setOutput(json({ autoPaySetup: setup, authorization: verified, autoPayStatus: status }))
    } catch (error) {
      setOutput(error instanceof Error ? error.message : String(error))
    } finally {
      setAutoPayBusy(false)
    }
  }

  async function simulateAutoPayRenewalForLastSubscription() {
    if (!subscriptionId || !canRunProtected || autoPayBusy) return
    setAutoPayBusy(true)
    try {
      const before = await getEntitlement(token.trim(), subscriptionId)
      const charge = await simulateAutoPayRenewal(token.trim(), subscriptionId)
      const after = await getEntitlement(token.trim(), subscriptionId)
      const status = await getAutoPayStatus(token.trim(), subscriptionId)
      setEntitlement(after)
      setOutput(json({ before, simulatedAutoPayCharge: charge, after, autoPayStatus: status }))
    } catch (error) {
      setOutput(error instanceof Error ? error.message : String(error))
    } finally {
      setAutoPayBusy(false)
    }
  }

  async function cancelLastSubscription() {
    if (!subscriptionId || !canRunProtected) return
    try {
      const result = await cancelAtPeriodEnd(token.trim(), subscriptionId)
      setEntitlement(result)
      setOutput(json({ cancelledAtPeriodEnd: result }))
    } catch (error) {
      setOutput(error instanceof Error ? error.message : String(error))
    }
  }

  return (
    <main className="page-shell">
      <section className="hero-card">
        <p className="eyebrow">oRRbit AI Repair Software</p>
        <h1>Free Staging Checkout Integration</h1>
        <p className="hero-copy">
          Website Buy Now flow ko free staging API se test karein — no paid DB,
          no live Razorpay, no production payment.
        </p>
        <div className="hero-actions">
          <a className="secondary-link" href={`${apiBase}/testing/free-checkout`} target="_blank">
            API test page
          </a>
          <span className="api-pill">API: {apiBase}</span>
        </div>
      </section>

      <section className="checkout-grid">
        <div className="panel buy-panel">
          <div className="price-row">
            <div>
              <p className="eyebrow">Hybrid Pro Plan</p>
              <h2>₹29,999 + GST</h2>
            </div>
            <span>12 months</span>
          </div>
          <ul className="feature-list">
            <li>Desktop + Web Admin + Field Staff PWA</li>
            <li>10 named web users for testing</li>
            <li>Simulated Razorpay order and payment capture</li>
            <li>Subscription, license and entitlement status verification</li>
            <li>AutoPay mandate setup, authorization and renewal simulation</li>
          </ul>

          <label htmlFor="token">Staging bearer token</label>
          <input
            id="token"
            type="password"
            value={token}
            onChange={(event) => setToken(event.target.value)}
            placeholder="Paste staging token only while testing"
          />
          <p className={canRunProtected ? 'token-help ok' : 'token-help'}>
            {canRunProtected
              ? 'Token entered. Protected admin/cancel checks are enabled.'
              : 'No token needed for Buy Now demo. Token is only needed for admin status and cancel test.'}
          </p>
          <div className="button-row">
            <button type="button" className="ghost" onClick={runPublicChecks}>
              Check API First
            </button>
            <button type="button" onClick={runFullFlow}>
              Buy Now — Test Full Flow
            </button>
            <button type="button" className="ghost" onClick={reset}>
              Reset
            </button>
          </div>

          <div className="button-row">
            <button
              type="button"
              disabled={!subscriptionId || !canRunProtected || autoPayBusy}
              onClick={setupAutoPayForLastSubscription}
            >
              {autoPayBusy ? 'Working…' : 'Setup / Authorize AutoPay'}
            </button>
            <button
              type="button"
              className="ghost"
              disabled={!subscriptionId || !canRunProtected || autoPayBusy}
              onClick={simulateAutoPayRenewalForLastSubscription}
            >
              Simulate AutoPay Renewal
            </button>
          </div>

          <button
            type="button"
            className="danger"
            disabled={!subscriptionId}
            onClick={cancelLastSubscription}
          >
            Cancel Last Subscription at Period End
          </button>
        </div>
        <div className="panel status-panel">
          <h2>Flow status</h2>
          <div className="steps">
            {steps.map((step) => (
              <article className={`step ${step.status}`} key={step.key}>
                <span>{statusText(step.status)}</span>
                <strong>{step.label}</strong>
                {step.detail ? <small>{step.detail}</small> : null}
              </article>
            ))}
          </div>
        </div>
      </section>
      <section className="result-grid">
        <div className="panel">
          <h2>Latest entitlement</h2>
          {activationCode ? (
            <div className="activation-code-box">
              <span>Desktop Activation Code</span>
              <strong>{activationCode}</strong>
              <small>Use this code in Repair desktop software activation screen.</small>
            </div>
          ) : null}
          {entitlement ? (
            <dl className="summary-list">
              <div><dt>Status</dt><dd>{entitlement.status}</dd></div>
              <div><dt>Renewal</dt><dd>{entitlement.renewalStatus}</dd></div>
              <div><dt>Valid until</dt><dd>{entitlement.validUntil}</dd></div>
              <div><dt>Auto renew</dt><dd>{String(entitlement.autoRenewEnabled)}</dd></div>
              {entitlement.autoPayProviderStatus ? (
                <div><dt>AutoPay provider</dt><dd>{entitlement.autoPayProviderStatus}</dd></div>
              ) : null}
            </dl>
          ) : <p>No subscription tested yet.</p>}
        </div>
        <div className="panel output-panel">
          <h2>Raw API output</h2>
          <pre>{output}</pre>
        </div>
      </section>

      <p className="footer-note">
        Final production me yahi flow live Razorpay + paid DB + production auth se chalega.
      </p>
    </main>
  )
}

export default App
