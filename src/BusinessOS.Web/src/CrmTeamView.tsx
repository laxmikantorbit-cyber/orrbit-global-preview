import { useState } from 'react'
import {
  changeCrmTeamRole,
  changeCrmTeamStatus,
  createCrmTeamMember,
  type CrmRole,
  type CrmTeamMember,
} from './crmApi'

type Props = {
  members: CrmTeamMember[]
  roles: CrmRole[]
  busy: boolean
  refresh: () => Promise<void>
  notify: (message: string) => void
  canManageTeam: boolean
}

export function CrmTeamView({ members, roles, busy, refresh, notify, canManageTeam }: Props) {
  const [showAdd, setShowAdd] = useState(false)
  const [name, setName] = useState('')
  const [email, setEmail] = useState('')
  const [mobile, setMobile] = useState('')
  const [role, setRole] = useState('SalesExecutive')

  async function run(action: () => Promise<unknown>, success: string) {
    try { await action(); notify(success); await refresh() }
    catch (error) { notify(error instanceof Error ? error.message : String(error)) }
  }

  async function addMember() {
    if (!name.trim() || !email.trim()) return
    await run(async () => {
      await createCrmTeamMember({ displayName: name.trim(), email: email.trim(), mobileNumber: mobile, role })
      setName(''); setEmail(''); setMobile(''); setShowAdd(false)
    }, 'CRM team member added')
  }

  const activeCount = members.filter((member) => member.active).length
  return (
    <section className="crm2-module-page crm2-team-page">
      <div className="crm2-module-head">
        <div><span className="crm2-kicker">TEAM & ACCESS</span><h2>CRM users & roles</h2><p>Assign sales work and control CRM capabilities by role.</p></div>
        {canManageTeam ? <button className="crm2-primary" onClick={() => setShowAdd(true)}>＋ Add user</button> : <span className="crm2-readonly-badge">Read only</span>}
      </div>
      <div className="crm2-summary-strip">
        <div><span>Total users</span><b>{members.length}</b></div>
        <div><span>Active users</span><b>{activeCount}</b></div>
        <div><span>Defined roles</span><b>{roles.length}</b></div>
      </div>
      <div className="crm2-team-list">
        {members.map((member) => (
          <article key={member.id} className={!member.active ? 'inactive' : ''}>
            <div className="crm2-team-person">
              <i>{member.displayName.slice(0, 1).toUpperCase()}</i>
              <span><strong>{member.displayName}</strong><small>{member.email}{member.mobileNumber ? ` · ${member.mobileNumber}` : ''}</small></span>
            </div>
            <select value={member.role} disabled={busy || !canManageTeam} onChange={(e) => void run(() => changeCrmTeamRole(member.id, e.target.value), 'CRM role updated')}>
              {roles.map((item) => <option key={item.role}>{item.role}</option>)}
            </select>
            <div className="crm2-permissions">
              {member.permissions.slice(0, 4).map((permission) => <span key={permission}>{permission.replace(/([A-Z])/g, ' $1').trim()}</span>)}
              {member.permissions.length > 4 ? <em>+{member.permissions.length - 4}</em> : null}
            </div>
            <button className={member.active ? 'crm2-user-active' : 'crm2-user-inactive'} disabled={busy || !canManageTeam} onClick={() => void run(() => changeCrmTeamStatus(member.id, !member.active), member.active ? 'CRM user deactivated' : 'CRM user activated')}>{member.active ? 'Active' : 'Inactive'}</button>
          </article>
        ))}
      </div>

      {showAdd && canManageTeam ? (
        <div className="crm2-inline-form">
          <div><span className="crm2-kicker">NEW CRM USER</span><h3>Add team member</h3></div>
          <div className="crm2-form-grid">
            <label>Name<input value={name} onChange={(e) => setName(e.target.value)} placeholder="Sales executive name" /></label>
            <label>Email<input type="email" value={email} onChange={(e) => setEmail(e.target.value)} placeholder="user@example.com" /></label>
            <label>Mobile<input value={mobile} onChange={(e) => setMobile(e.target.value)} placeholder="Optional" /></label>
            <label>Role<select value={role} onChange={(e) => setRole(e.target.value)}>{roles.map((item) => <option key={item.role}>{item.role}</option>)}</select></label>
          </div>
          <div className="crm2-drawer-actions"><button className="crm2-cancel" onClick={() => setShowAdd(false)}>Cancel</button><button className="crm2-primary" disabled={busy || !name.trim() || !email.trim()} onClick={() => void addMember()}>Create user</button></div>
        </div>
      ) : null}
    </section>
  )
}
