import { apiBase } from './businessosApi'

export type OrganisationProfileContact = {
  id?: string | null
  name: string
  email?: string | null
  phone?: string | null
  designation?: string | null
}

export type OrganisationBillingAddress = {
  id?: string | null
  line1: string
  line2?: string | null
  city: string
  state: string
  postalCode: string
  countryCode: string
  stateCode: string
}

export type OrganisationProfile = {
  organisationId: string
  name: string
  legalName?: string | null
  gstin?: string | null
  primaryContact?: OrganisationProfileContact | null
  billingAddress?: OrganisationBillingAddress | null
  billingProfileComplete: boolean
  billingProfileIssue?: string | null
}

export type OrganisationProfileUpdate = {
  name: string
  legalName?: string | null
  gstin?: string | null
  primaryContact: OrganisationProfileContact
  billingAddress: OrganisationBillingAddress
}

function headers(token: string): HeadersInit {
  return {
    Authorization: `Bearer ${token}`,
    'Content-Type': 'application/json',
  }
}

async function parse<T>(response: Response): Promise<T> {
  const text = await response.text()
  const data = text ? JSON.parse(text) : null
  if (!response.ok)
    throw new Error(data?.error || data?.detail || data?.title || `HTTP ${response.status}`)
  return data as T
}

export async function getOrganisationProfile(token: string, subscriptionId: string) {
  const response = await fetch(
    `${apiBase}/api/customer-portal/subscriptions/${subscriptionId}/organisation-profile`,
    { headers: headers(token) },
  )
  return parse<OrganisationProfile>(response)
}

export async function saveOrganisationProfile(
  token: string,
  subscriptionId: string,
  request: OrganisationProfileUpdate,
) {
  const response = await fetch(
    `${apiBase}/api/customer-portal/subscriptions/${subscriptionId}/organisation-profile`,
    { method: 'PUT', headers: headers(token), body: JSON.stringify(request) },
  )
  return parse<OrganisationProfile>(response)
}
