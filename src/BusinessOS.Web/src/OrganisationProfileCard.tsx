import { useEffect, useState } from 'react'
import {
  getOrganisationProfile,
  saveOrganisationProfile,
  type OrganisationProfile,
  type OrganisationProfileUpdate,
} from './organisationProfileApi'

type Props = {
  token: string
  subscriptionId: string
  title?: string
}

const emptyForm: OrganisationProfileUpdate = {
  name: '',
  legalName: '',
  gstin: '',
  primaryContact: { name: '', email: '', phone: '', designation: '' },
  billingAddress: {
    line1: '',
    line2: '',
    city: '',
    state: '',
    postalCode: '',
    countryCode: 'IN',
    stateCode: '',
  },
}

export function OrganisationProfileCard({
  token,
  subscriptionId,
  title = 'Organisation & GST billing profile',
}: Props) {
  const [profile, setProfile] = useState<OrganisationProfile | null>(null)
  const [form, setForm] = useState<OrganisationProfileUpdate>(emptyForm)
  const [message, setMessage] = useState('')

  const load = async () => {
    if (!token || !subscriptionId) return
    setMessage('Loading billing profile…')
    try {
      const result = await getOrganisationProfile(token, subscriptionId)
      setProfile(result)
      setForm({
        name: result.name || '',
        legalName: result.legalName || '',
        gstin: result.gstin || '',
        primaryContact: {
          id: result.primaryContact?.id,
          name: result.primaryContact?.name || '',
          email: result.primaryContact?.email || '',
          phone: result.primaryContact?.phone || '',
          designation: result.primaryContact?.designation || '',
        },
        billingAddress: {
          id: result.billingAddress?.id,
          line1: result.billingAddress?.line1 || '',
          line2: result.billingAddress?.line2 || '',
          city: result.billingAddress?.city || '',
          state: result.billingAddress?.state || '',
          postalCode: result.billingAddress?.postalCode || '',
          countryCode: result.billingAddress?.countryCode || 'IN',
          stateCode: result.billingAddress?.stateCode || '',
        },
      })
      setMessage(result.billingProfileComplete
        ? 'Billing profile is complete.'
        : result.billingProfileIssue || 'Billing profile needs attention.')
    } catch (error) {
      setMessage(error instanceof Error ? error.message : String(error))
    }
  }

  useEffect(() => {
    void load()
    // Reload only when the selected account changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token, subscriptionId])

  const save = async () => {
    if (!token || !subscriptionId) {
      setMessage('Load a subscription first.')
      return
    }
    setMessage('Saving billing profile…')
    try {
      const result = await saveOrganisationProfile(token, subscriptionId, form)
      setProfile(result)
      setMessage(result.billingProfileComplete
        ? 'Billing profile saved and ready for future invoices.'
        : result.billingProfileIssue || 'Billing profile saved.')
    } catch (error) {
      setMessage(error instanceof Error ? error.message : String(error))
    }
  }

  const updateContact = (
    field: keyof OrganisationProfileUpdate['primaryContact'],
    value: string,
  ) => setForm(current => ({
    ...current,
    primaryContact: { ...current.primaryContact, [field]: value },
  }))

  const updateAddress = (
    field: keyof OrganisationProfileUpdate['billingAddress'],
    value: string,
  ) => setForm(current => ({
    ...current,
    billingAddress: { ...current.billingAddress, [field]: value },
  }))

  return <section className="bos-card">
    <div className="bos-profile-heading">
      <div><h2>{title}</h2>
        <p>These details are used on future invoices and receipts.</p></div>
      {profile && <span className={profile.billingProfileComplete ? 'pill ok' : 'pill muted'}>
        {profile.billingProfileComplete ? 'Complete' : 'Needs attention'}
      </span>}
    </div>
    <div className="bos-profile-grid">
      <label>Business / display name<input value={form.name}
        onChange={e => setForm({ ...form, name: e.target.value })} /></label>
      <label>Legal name<input value={form.legalName || ''}
        onChange={e => setForm({ ...form, legalName: e.target.value })} /></label>
      <label>GSTIN (optional)<input value={form.gstin || ''} maxLength={15}
        onChange={e => setForm({ ...form, gstin: e.target.value.toUpperCase() })} /></label>
      <label>Primary contact<input value={form.primaryContact.name}
        onChange={e => updateContact('name', e.target.value)} /></label>
      <label>Contact email<input value={form.primaryContact.email || ''}
        onChange={e => updateContact('email', e.target.value)} /></label>
      <label>Contact phone<input value={form.primaryContact.phone || ''}
        onChange={e => updateContact('phone', e.target.value)} /></label>
      <label>Designation<input value={form.primaryContact.designation || ''}
        onChange={e => updateContact('designation', e.target.value)} /></label>
      <label>Address line 1<input value={form.billingAddress.line1}
        onChange={e => updateAddress('line1', e.target.value)} /></label>
      <label>Address line 2<input value={form.billingAddress.line2 || ''}
        onChange={e => updateAddress('line2', e.target.value)} /></label>
      <label>City<input value={form.billingAddress.city}
        onChange={e => updateAddress('city', e.target.value)} /></label>
      <label>State<input value={form.billingAddress.state}
        onChange={e => updateAddress('state', e.target.value)} /></label>
      <label>GST state code<input value={form.billingAddress.stateCode} maxLength={2}
        onChange={e => updateAddress('stateCode', e.target.value.replace(/\D/g, '').slice(0, 2))} /></label>
      <label>PIN / postal code<input value={form.billingAddress.postalCode}
        onChange={e => updateAddress('postalCode', e.target.value)} /></label>
      <label>Country code<input value={form.billingAddress.countryCode} maxLength={2}
        onChange={e => updateAddress('countryCode', e.target.value.toUpperCase())} /></label>
    </div>
    <div className="bos-actions">
      <button onClick={save}>Save billing profile</button>
      <button onClick={load}>Refresh</button>
    </div>
    <p className="bos-message">{message}</p>
  </section>
}
