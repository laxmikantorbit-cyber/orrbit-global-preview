import { useEffect, useMemo, useState } from 'react'
import {
  startCrmTaskTimer,
  stopCrmTaskTimer,
  type CrmTask,
  type CrmTaskTimer,
} from './crmApi'

type Props = {
  timers: CrmTaskTimer[]
  tasks: CrmTask[]
  currentUserId?: string
  canViewAll: boolean
  canViewTimesheets: boolean
  busy: boolean
  close: () => void
  refresh: () => Promise<void>
  openTimesheets: () => void
  notify: (message: string) => void
}

function formatDuration(seconds: number) {
  const safe = Math.max(0, Math.floor(seconds))
  const hours = Math.floor(safe / 3600)
  const minutes = Math.floor((safe % 3600) / 60)
  const secs = safe % 60
  return [hours, minutes, secs].map(value => String(value).padStart(2, '0')).join(':')
}

export function CrmTaskTimerPanel({
  timers,
  tasks,
  currentUserId,
  canViewAll,
  canViewTimesheets,
  busy,
  close,
  refresh,
  openTimesheets,
  notify,
}: Props) {
  const [taskId, setTaskId] = useState('')
  const [note, setNote] = useState('')
  const [now, setNow] = useState(() => Date.now())

  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(id)
  }, [])

  const availableTasks = useMemo(
    () => tasks.filter(task =>
      task.status === 'Open'
      && (canViewAll || task.assigneeUserId === currentUserId)),
    [canViewAll, currentUserId, tasks],
  )

  const ownTimers = useMemo(
    () => timers.filter(timer => timer.userId === currentUserId),
    [currentUserId, timers],
  )
  const running = ownTimers.filter(timer => timer.isRunning)
  const recent = ownTimers.filter(timer => !timer.isRunning).slice(0, 5)

  function elapsed(timer: CrmTaskTimer) {
    if (!timer.isRunning) return timer.durationSeconds
    const started = new Date(timer.startedAtUtc).getTime()
    return Math.max(timer.durationSeconds, Math.floor((now - started) / 1000))
  }

  async function start() {
    if (!taskId) {
      notify('Select an open task to start timer')
      return
    }
    try {
      await startCrmTaskTimer(taskId, note)
      setNote('')
      notify('Task timer started')
      await refresh()
    } catch (error) {
      notify(error instanceof Error ? error.message : String(error))
    }
  }

  async function stop(timerId: string) {
    try {
      await stopCrmTaskTimer(timerId)
      notify('Task timer stopped')
      await refresh()
    } catch (error) {
      notify(error instanceof Error ? error.message : String(error))
    }
  }

  return <section className="crm2-notification-panel crm2-timer-panel">
    <div className="crm2-drawer-head">
      <div><span className="crm2-kicker">TASK TIMER</span><h3>Track work time</h3></div>
      <button onClick={close} aria-label="Close timer">×</button>
    </div>

    <div className="crm2-form-grid">
      <label>Task
        <select value={taskId} onChange={event => setTaskId(event.target.value)}>
          <option value="">Select open task</option>
          {availableTasks.map(task => <option key={task.id} value={task.id}>{task.title}</option>)}
        </select>
      </label>
      <label>Note
        <input value={note} onChange={event => setNote(event.target.value)} placeholder="Optional work note" />
      </label>
    </div>
    <div className="crm2-drawer-actions">
      <button className="crm2-primary" disabled={busy || !taskId} onClick={() => void start()}>▶ Start timer</button>
      {canViewTimesheets ? <button onClick={openTimesheets}>View timesheets</button> : null}
    </div>

    <div className="crm-advanced-list">
      {running.map(timer => {
        const task = tasks.find(item => item.id === timer.taskId)
        return <article key={timer.id}>
          <div>
            <b>Running now</b>
            <strong>{task?.title || 'Task'}</strong>
            <small>{timer.note || 'No note'}</small>
          </div>
          <span>
            <strong>{formatDuration(elapsed(timer))}</strong>
            <button disabled={busy} onClick={() => void stop(timer.id)}>■ Stop</button>
          </span>
        </article>
      })}
      {running.length === 0 ? <p>No timer is running.</p> : null}
    </div>

    {recent.length > 0 ? <div className="crm-advanced-list">
      <small>Recent timers</small>
      {recent.map(timer => {
        const task = tasks.find(item => item.id === timer.taskId)
        return <article key={timer.id}>
          <div><strong>{task?.title || 'Task'}</strong><small>{new Date(timer.startedAtUtc).toLocaleString()}</small></div>
          <span><strong>{formatDuration(timer.durationSeconds)}</strong></span>
        </article>
      })}
    </div> : null}
  </section>
}
