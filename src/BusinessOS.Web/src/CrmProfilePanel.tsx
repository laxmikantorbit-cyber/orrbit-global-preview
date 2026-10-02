import { useEffect, useState } from 'react'
import { updateCrmProfile, type CrmTeamMember } from './crmApi'

type Props = {
  member: CrmTeamMember
  busy: boolean
  close: () => void
  refresh: () => Promise<void>
  notify: (message: string) => void
}

export function CrmProfilePanel({ member, busy, close, refresh, notify }: Props) {
  const [name, setName] = useState(member.displayName)
  const [email, setEmail] = useState(member.email)
  const [mobile, setMobile] = useState(member.mobileNumber || '')
  const [saving, setSaving] = useState(false)

  useEffect(() => {
    setName(member.displayName)
    setEmail(member.email)
    setMobile(member.mobileNumber || '')
  }, [member])

  async function save() {
    if (!name.trim() || !email.trim()) {
      notify('Name and email are required')
      return
    }
    setSaving(true)
    try {
      await updateCrmProfile({
        displayName: name.trim(),
        email: email.trim(),
        mobileNumber: mobile.trim() || undefined,
      })
      await refresh()
      notify('Profile updated')
      close()
    } catch (error) {
      notify(error instanceof Error ? error.message : String(error))
    } finally {
      setSaving(false)
    }
  }

  return <section className="crm2-notification-panel crm2-profile-panel">
    <div className="crm2-drawer-head">
      <div><span className="crm2-kicker">MY PROFILE</span><h3>{member.displayName}</h3><p>{member.role}</p></div>
      <button onClick={close} aria-label="Close profile">×</button>
    </div>
    <div className="crm2-form-grid">
      <label>Name<input autoFocus value={name} onChange={event => setName(event.target.value)} /></label>
      <label>Email<input type="email" value={email} onChange={event => setEmail(event.target.value)} /></label>
      <label>Mobile<input value={mobile} onChange={event => setMobile(event.target.value)} placeholder="Optional" /></label>
      <label>Role<input value={member.role} readOnly /></label>
    </div>
    <div className="crm2-drawer-actions">
      <button onClick={close}>Cancel</button>
      <button className="crm2-primary" disabled={busy || saving || !name.trim() || !email.trim()} onClick={() => void save()}>
        {saving ? 'Saving...' : 'Save profile'}
      </button>
    </div>
  </section>
}
