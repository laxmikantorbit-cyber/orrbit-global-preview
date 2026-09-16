import type { AutoPaySetupResponse } from './businessosApi'

export type RazorpaySubscriptionAuthorization = {
  razorpay_payment_id: string
  razorpay_subscription_id: string
  razorpay_signature: string
}

type CheckoutInstance = { open: () => void }
type CheckoutConstructor = new (options: Record<string, unknown>) => CheckoutInstance

declare global {
  interface Window {
    Razorpay?: CheckoutConstructor
  }
}

const checkoutScript = 'https://checkout.razorpay.com/v1/checkout.js'

async function loadCheckoutScript() {
  if (window.Razorpay) return
  const existing = document.querySelector<HTMLScriptElement>(`script[src="${checkoutScript}"]`)
  if (existing) {
    await new Promise<void>((resolve, reject) => {
      existing.addEventListener('load', () => resolve(), { once: true })
      existing.addEventListener('error', () => reject(new Error('Razorpay Checkout failed to load.')), { once: true })
    })
    return
  }
  await new Promise<void>((resolve, reject) => {
    const script = document.createElement('script')
    script.src = checkoutScript
    script.async = true
    script.onload = () => resolve()
    script.onerror = () => reject(new Error('Razorpay Checkout failed to load.'))
    document.head.appendChild(script)
  })
}

export async function openRazorpaySubscriptionAuthorization(
  setup: AutoPaySetupResponse,
): Promise<RazorpaySubscriptionAuthorization> {
  if (setup.providerPublicKeyId === 'rzp_test_free_testing')
    throw new Error('FreeTesting AutoPay uses the local renewal simulator, not Razorpay Checkout.')

  await loadCheckoutScript()
  if (!window.Razorpay) throw new Error('Razorpay Checkout is unavailable.')

  return new Promise<RazorpaySubscriptionAuthorization>((resolve, reject) => {
    const checkout = new window.Razorpay!({
      key: setup.providerPublicKeyId,
      subscription_id: setup.providerSubscriptionId,
      name: 'oRRbit™ Slickteq Softech Pvt. Ltd.',
      description: 'BusinessOS AutoPay authorization',
      handler: (response: RazorpaySubscriptionAuthorization) => resolve(response),
      modal: { ondismiss: () => reject(new Error('AutoPay authorization was cancelled.')) },
    })
    checkout.open()
  })
}
