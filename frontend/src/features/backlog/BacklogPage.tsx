import { useEffect, useRef, useState } from 'react'
import { useIsMutating, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { DragDropProvider } from '@dnd-kit/react'
import { isSortable, useSortable } from '@dnd-kit/react/sortable'
import { ArrowDown, ArrowUp, GripVertical } from 'lucide-react'
import { Link } from 'react-router'
import { Button } from '@/components/ui/button'
import { useAuth } from '@/features/auth/useAuth'
import { GameCover } from '@/features/games/GameCover'
import { dashboardKey } from '@/features/dashboard/dashboardApi'
import { priorities } from '@/features/library/libraryApi'
import type { Priority } from '@/features/library/libraryApi'
import { libraryItemKey, libraryKey, refreshLibrary } from '@/features/library/libraryQueries'
import { libraryInputClass } from '@/features/library/LibraryStatusFields'
import { ApiError } from '@/lib/apiClient'
import { backlogKey, getBacklog, reorderBacklog, updateBacklogPriority } from './backlogApi'
import type { BacklogItem, BacklogResponse } from './backlogApi'

export function BacklogPage() {
  const userId = useAuth().user?.id
  const backlog = useQuery({
    queryKey: backlogKey(userId), queryFn: ({ signal }) => getBacklog(signal), enabled: Boolean(userId), retry: false,
    refetchOnWindowFocus: false,
  })
  return <section className="space-y-6">
    <div className="space-y-2">
      <h1 className="text-3xl font-semibold tracking-tight">Backlog</h1>
      <p className="text-muted-foreground">Choose what to play next. Order and priority changes save automatically.</p>
      <Link to="/games" className="text-sm underline underline-offset-4">Discover more games</Link>
    </div>
    {backlog.isPending && <p role="status">Loading your backlog…</p>}
    {backlog.isError && !backlog.data && <div role="alert" className="space-y-3">
      <p>Could not load your backlog. Please try again.</p>
      <Button variant="outline" onClick={() => { void backlog.refetch() }}>Try again</Button>
    </div>}
    {backlog.data && <BacklogBoard key={userId} userId={userId} data={backlog.data}
      refreshing={backlog.isFetching} fetchFailed={backlog.isError} reload={() => backlog.refetch()} />}
  </section>
}

type Change = { kind: 'order'; items: BacklogItem[] } | { kind: 'priority'; item: BacklogItem; value: Priority | null }

function BacklogBoard({ userId, data, refreshing, fetchFailed, reload }: {
  userId: string | undefined; data: BacklogResponse; refreshing: boolean; fetchFailed: boolean
  reload: () => Promise<{ isSuccess: boolean }>
}) {
  const client = useQueryClient()
  const active = useRef(false)
  const writing = useRef(false)
  const board = useRef<HTMLDivElement>(null)
  const focusAfterSave = useRef<{ id: string; control: string } | null>(null)
  useEffect(() => { active.current = true; return () => { active.current = false } }, [])
  const [draft, setDraft] = useState<BacklogItem[] | null>(null)
  const [dragSnapshot, setDragSnapshot] = useState<BacklogItem[] | null>(null)
  const [priorityDraft, setPriorityDraft] = useState<{ id: string; value: Priority | null } | null>(null)
  const [retry, setRetry] = useState<Change | null>(null)
  const [error, setError] = useState('')
  const [notice, setNotice] = useState('')
  const [needsReload, setNeedsReload] = useState(false)
  const pendingWrites = useIsMutating({ mutationKey: ['backlog-write', userId] })
    + useIsMutating({ mutationKey: ['library-write', userId] })
  const items = draft ?? dragSnapshot ?? data.items
  const blocked = pendingWrites > 0 || refreshing || fetchFailed || needsReload

  function restoreFocus() {
    requestAnimationFrame(() => {
      const wanted = focusAfterSave.current
      if (!active.current || document.activeElement !== document.body || !wanted) return
      const row = Array.from(board.current?.querySelectorAll<HTMLElement>('[data-backlog-id]') ?? [])
        .find(element => element.dataset.backlogId === wanted.id)
      const target = Array.from(row?.querySelectorAll<HTMLElement>('[data-backlog-control]') ?? [])
        .find(element => element.dataset.backlogControl === wanted.control)
      const fallback = row?.querySelector<HTMLElement>('[data-backlog-control="handle"]')
      const destination = target && !target.hasAttribute('disabled') ? target : fallback
      if (destination && !destination.hasAttribute('disabled')) destination.focus()
    })
  }

  async function refresh() {
    if (writing.current || dragSnapshot) return
    setNeedsReload(true); setRetry(null); setDraft(null); setPriorityDraft(null)
    const result = await reload()
    if (active.current) {
      setNeedsReload(!result.isSuccess)
      if (result.isSuccess) { setNotice('Backlog reloaded.'); setError('') }
    }
  }
  const save = useMutation({
    mutationKey: ['backlog-write', userId],
    mutationFn: async (change: Change) => change.kind === 'order'
      ? { kind: 'order' as const, response: await reorderBacklog(change.items) }
      : { kind: 'priority' as const, response: await updateBacklogPriority(change.item.id, change.value) },
    onSuccess: async (saved, change) => {
      if (active.current) {
        setDraft(null); setPriorityDraft(null); setRetry(null); setError(''); setNotice(saved.kind === 'order' ? 'Backlog order saved.' : 'Priority saved.')
        if (saved.kind === 'order') client.setQueryData(backlogKey(userId), saved.response)
        else client.setQueryData(libraryItemKey(userId, saved.response.id), saved.response)
      }
      // Invalidation never recreates private cache entries after logout. It also
      // refreshes a new view if the user navigated away while a write was pending.
      if (change.kind === 'priority') await refreshLibrary(client, userId, change.item.game.igdbId)
      else {
        await client.invalidateQueries({ queryKey: dashboardKey(userId) })
        await client.invalidateQueries({ queryKey: libraryKey(userId) })
        if (!active.current) await client.invalidateQueries({ queryKey: backlogKey(userId) })
      }
    },
    onError: async (caught, change) => {
      if (!active.current) { await client.invalidateQueries({ queryKey: backlogKey(userId) }); return }
      setDraft(null); setPriorityDraft(null); setNotice('')
      if (caught instanceof ApiError && [400, 404, 409].includes(caught.status)) {
        setRetry(null); setNeedsReload(true)
        setError('The backlog changed or this change could not be applied. Reloading the saved backlog; please try again after it loads.')
        const result = await reload()
        if (active.current) {
          setNeedsReload(!result.isSuccess)
          setError(result.isSuccess ? 'The change was not applied. The saved backlog was reloaded; please try again.'
            : 'Could not reload the changed backlog. Reload it before making further changes.')
        }
      } else {
        setRetry(change)
        setError('Could not confirm the change was saved. The saved list is shown. Reload or retry saving.')
      }
    },
    onSettled: () => {
      writing.current = false
      restoreFocus()
    },
  })
  function submit(change: Change) {
    if (blocked || writing.current) return
    writing.current = true
    const focused = document.activeElement
    if (focused instanceof HTMLElement && board.current?.contains(focused)) {
      const id = focused.closest<HTMLElement>('[data-backlog-id]')?.dataset.backlogId
      const control = focused.dataset.backlogControl
      focusAfterSave.current = id && control ? { id, control } : null
    } else if (!dragSnapshot) focusAfterSave.current = null
    setRetry(null); setError(''); setNotice('Saving changes…')
    if (change.kind === 'order') setDraft(change.items)
    else setPriorityDraft({ id: change.item.id, value: change.value })
    save.mutate(change)
  }
  function move(from: number, to: number) {
    if (blocked || writing.current || from === to || from < 0 || to < 0 || from >= items.length || to >= items.length) return
    const next = [...items]
    const [moved] = next.splice(from, 1)
    next.splice(to, 0, moved)
    submit({ kind: 'order', items: next })
  }
  return <div ref={board} className="space-y-4">
    <p id="backlog-help" className="text-sm text-muted-foreground">Drag a game's handle, or use its Move up and Move down buttons. On a handle, use Space/Enter to pick up or drop, arrow keys to move, and Escape to cancel.</p>
    <div className="flex flex-wrap items-center gap-3">
      <p>{data.items.length} games</p>
      <Button variant="outline" disabled={pendingWrites > 0 || refreshing || Boolean(dragSnapshot)} onClick={() => { void refresh() }}>Reload backlog</Button>
    </div>
    {notice && <p role="status" aria-live="polite">{notice}</p>}
    {refreshing && <p role="status">Refreshing backlog…</p>}
    {(error || fetchFailed) && <div role="alert" className="space-y-3">
      <p>{error || 'Could not refresh the saved backlog. Reload it before making changes.'}</p>
      {retry && <Button variant="outline" disabled={blocked} onClick={() => submit(retry)}>Retry saving</Button>}
    </div>}
    {items.length === 0 && <div className="space-y-2 rounded-xl bg-muted p-6">
      <p>Your backlog is empty.</p><p>Add a game with Backlog status, or move a library game back to Backlog.</p>
      <Link to="/library" className="text-sm underline underline-offset-4">Open your library</Link>
    </div>}
    <DragDropProvider onBeforeDragStart={event => { if (blocked || writing.current) event.preventDefault() }}
      onDragStart={event => {
        setDragSnapshot(items); setNotice('')
        const source = event.operation.source
        if (source) focusAfterSave.current = { id: String(source.id), control: 'handle' }
      }}
      onDragEnd={event => {
        setDragSnapshot(null)
        if (event.canceled) { restoreFocus(); return }
        const source = event.operation.source
        if (isSortable(source)) move(source.initialIndex, source.index)
      }}>
      <ol aria-label="Backlog games" className="space-y-3">
        {items.map((item, index) => <BacklogRow key={item.id} item={item} index={index} count={items.length}
          sortingDisabled={blocked || items.length < 2} controlsDisabled={blocked || Boolean(dragSnapshot)}
          priority={priorityDraft?.id === item.id ? priorityDraft.value : item.priority}
          onMove={to => move(index, to)} onPriority={value => submit({ kind: 'priority', item, value })} />)}
      </ol>
    </DragDropProvider>
  </div>
}

function BacklogRow({ item, index, count, sortingDisabled, controlsDisabled, priority, onMove, onPriority }: {
  item: BacklogItem; index: number; count: number; sortingDisabled: boolean; controlsDisabled: boolean; priority: Priority | null
  onMove: (index: number) => void; onPriority: (priority: Priority | null) => void
}) {
  const { ref, handleRef, isDragging } = useSortable({ id: item.id, index, disabled: sortingDisabled })
  return <li ref={ref} data-backlog-id={item.id} className={`flex flex-wrap items-center gap-4 rounded-xl border bg-card p-4 ${isDragging ? 'opacity-70' : ''}`}>
    <Button ref={handleRef} variant="outline" className="touch-none cursor-grab" disabled={sortingDisabled}
      data-backlog-control="handle" aria-label={`Reorder ${item.game.name}`} aria-describedby="backlog-help"><GripVertical aria-hidden="true" /></Button>
    <span className="w-6 text-center font-medium" aria-label={`Position ${index + 1}`}>{index + 1}</span>
    <div className="w-12 shrink-0"><GameCover name={item.game.name} url={item.game.coverUrl} /></div>
    <Link to={`/library/${item.id}`} state={{ from: '/backlog' }} className="min-w-32 flex-1 font-semibold underline underline-offset-4">{item.game.name}</Link>
    <div className="w-40 space-y-1">
      <label htmlFor={`priority-${item.id}`} className="text-sm">Priority for {item.game.name}</label>
      <select id={`priority-${item.id}`} data-backlog-control="priority" className={libraryInputClass} value={priority ?? ''} disabled={controlsDisabled}
        onChange={event => onPriority((event.target.value || null) as Priority | null)}>
        <option value="">No priority</option>{priorities.map(value => <option key={value}>{value}</option>)}
      </select>
    </div>
    <div className="flex gap-2">
      <Button variant="outline" data-backlog-control="up" disabled={controlsDisabled || index === 0} aria-label={`Move ${item.game.name} up`} onClick={() => onMove(index - 1)}><ArrowUp aria-hidden="true" /></Button>
      <Button variant="outline" data-backlog-control="down" disabled={controlsDisabled || index === count - 1} aria-label={`Move ${item.game.name} down`} onClick={() => onMove(index + 1)}><ArrowDown aria-hidden="true" /></Button>
    </div>
  </li>
}
