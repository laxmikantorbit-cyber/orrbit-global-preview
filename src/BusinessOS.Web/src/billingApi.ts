import { apiBase } from './businessosApi'

const headers = (token: string): HeadersInit => ({
  Authorization: `Bearer ${token}`,
  'Content-Type': 'application/json',
})

async function read<T>(response: Response): Promise<T> {
  const text = await response.text()
  const data = text ? JSON.parse(text) : null
  if (!response.ok) throw new Error(data?.error || data?.detail || data?.title || `HTTP ${response.status}`)
  return data as T
}

export type BillingInvoice = {
  id: string
  orderId: string
  subscriptionId?: string | null
  invoiceNumber: string
  invoiceType: string
  issuedAtUtc: string
  productCode: string
  description: string
  currencyCode: string
  financialYear: string
  documentMode: string
  taxDocumentValid: boolean
  tax: {
    gstRate: number
    taxableAmount: number
    cgstAmount: number
    sgstAmount: number
    igstAmount: number
    totalTax: number
    grossAmount: number
    supplyType: string
  }
  paymentId: string
  paidAtUtc: string
}

export type BillingReceipt = {
  invoiceId: string
  invoiceNumber: string
  orderId: string
  paymentId: string
  paidAtUtc: string
  amountReceived: number
  currencyCode: string
  receiptFor: string
  documentMode: string
}

export async function listBillingHistory(
  token: string,
  filters: { organisationId?: string; subscriptionId?: string; take?: number } = {},
) {
  const params = new URLSearchParams()
  if (filters.organisationId) params.set('organisationId', filters.organisationId)
  if (filters.subscriptionId) params.set('subscriptionId', filters.subscriptionId)
  params.set('take', String(filters.take ?? 50))
  return read<BillingInvoice[]>(await fetch(
    `${apiBase}/api/billing/history?${params.toString()}`,
    { headers: headers(token) },
  ))
}

export async function createInvoice(
  token: string,
  orderId: string,
  request: { buyerStateCode?: string; sellerStateCode?: string } = {},
) {
  return read<BillingInvoice>(await fetch(
    `${apiBase}/api/billing/admin/orders/${orderId}/invoice`,
    { method: 'POST', headers: headers(token), body: JSON.stringify(request) },
  ))
}

export async function getBillingReceipt(token: string, invoiceId: string) {
  return read<BillingReceipt>(await fetch(
    `${apiBase}/api/billing/invoices/${invoiceId}/receipt`,
    { headers: headers(token) },
  ))
}
