import { gameStatuses, priorities, statusLabel } from './libraryApi'
import type { GameStatus, Priority } from './libraryApi'

export const libraryInputClass = 'w-full rounded-lg border bg-background px-3 py-2 text-sm outline-none focus-visible:ring-2 focus-visible:ring-ring'

export function LibraryStatusFields({ prefix, status, priority, disabled, onStatus, onPriority }: {
  prefix: string; status: GameStatus; priority: Priority | null; disabled: boolean
  onStatus: (status: GameStatus) => void; onPriority: (priority: Priority | null) => void
}) {
  return <div className="grid max-w-xl gap-4 sm:grid-cols-2">
    <div className="space-y-1.5">
      <label htmlFor={`${prefix}-status`} className="text-sm font-medium">Status</label>
      <select id={`${prefix}-status`} className={libraryInputClass} value={status} disabled={disabled}
        onChange={event => onStatus(event.target.value as GameStatus)}>
        {gameStatuses.map(value => <option key={value} value={value}>{statusLabel(value)}</option>)}
      </select>
    </div>
    {status === 'Backlog' && <div className="space-y-1.5">
      <label htmlFor={`${prefix}-priority`} className="text-sm font-medium">Backlog priority</label>
      <select id={`${prefix}-priority`} className={libraryInputClass} value={priority ?? ''} disabled={disabled}
        onChange={event => onPriority((event.target.value || null) as Priority | null)}>
        <option value="">No priority</option>
        {priorities.map(value => <option key={value}>{value}</option>)}
      </select>
    </div>}
  </div>
}
