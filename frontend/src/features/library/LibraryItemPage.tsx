import { useRef, useState } from 'react'
import { useIsMutating, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useLocation, useNavigate, useParams } from 'react-router'
import { Button } from '@/components/ui/button'
import { useAuth } from '@/features/auth/useAuth'
import { GameCover } from '@/features/games/GameCover'
import type { GameDetails } from '@/features/games/gamesApi'
import { ApiError } from '@/lib/apiClient'
import { deleteLibraryItem, getLibraryItem, statusLabel, updateLibraryItem } from './libraryApi'
import type { GameStatus, LibraryItem, Priority } from './libraryApi'
import { LibraryStatusFields } from './LibraryStatusFields'
import { gameDetailsKey, libraryItemKey, refreshLibrary } from './libraryQueries'

export function LibraryItemPage() {
  const userId = useAuth().user?.id
  const { id = '' } = useParams()
  const [savedId, setSavedId] = useState<string | null>(null)
  const location = useLocation()
  const from = (location.state as { from?: string } | null)?.from
  const backUrl = from && (from === '/backlog' || from === '/library' || from.startsWith('/library?')) ? from : '/library'
  const valid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id)
  const details = useQuery({
    queryKey: libraryItemKey(userId, id), queryFn: ({ signal }) => getLibraryItem(id, signal), enabled: valid, retry: false,
  })
  return <section className="space-y-8">
    <Link to={backUrl} className="text-sm underline underline-offset-4">{backUrl === '/backlog' ? 'Back to backlog' : 'Back to library'}</Link>
    {!valid && <p role="alert">Invalid library item. Choose a game from your library.</p>}
    {valid && details.isPending && <p role="status">Loading library item…</p>}
    {valid && details.isError && <div role="alert" className="space-y-3">
      <p>{details.error instanceof ApiError && details.error.status === 404 ? 'This item could not be found in your library.' : 'Could not load this library item. Please try again.'}</p>
      <Button variant="outline" onClick={() => { void details.refetch() }}>Try again</Button>
    </div>}
    {valid && details.isSuccess && <div className="grid items-start gap-8 md:grid-cols-[240px_1fr]">
      <GameCover url={details.data.game.coverUrl} name={details.data.game.name} />
      <div className="space-y-6">
        <h1 className="text-3xl font-semibold tracking-tight">{details.data.game.name}</h1>
        {savedId === id && <p role="status">Changes saved.</p>}
        <StatusEditor key={`${details.data.id}:${details.data.updatedAt}`} item={details.data}
          onSaved={() => setSavedId(id)} onEdit={() => setSavedId(null)} />
        <dl className="grid gap-4 sm:grid-cols-2">
          <Field label="Release date" value={details.data.game.releaseDate ?? 'Unavailable'} />
          <Field label="Genres" value={details.data.game.genres.map(value => value.name).join(' · ') || 'Unavailable'} />
          <Field label="Platforms" value={details.data.game.platforms.map(value => value.name).join(' · ') || 'Unavailable'} />
          <Field label="Rating" value={details.data.rating === null ? 'Not rated' : `${details.data.rating}/10`} />
          <Field label="Added" value={dateLabel(details.data.addedAt)} />
          <Field label="Started" value={dateLabel(details.data.startedAt)} />
          <Field label="Completed" value={dateLabel(details.data.completedAt)} />
        </dl>
        <section className="space-y-2"><h2 className="text-xl font-semibold">About this game</h2><p className="whitespace-pre-line text-muted-foreground">{details.data.game.summary ?? 'No summary available.'}</p></section>
        <section className="space-y-2"><h2 className="text-xl font-semibold">Personal note</h2><p className="whitespace-pre-line text-muted-foreground">{details.data.personalNote ?? 'No personal note yet.'}</p></section>
        <RemoveItem item={details.data} backUrl={backUrl} />
      </div>
    </div>}
  </section>
}

function StatusEditor({ item, onSaved, onEdit }: { item: LibraryItem; onSaved: () => void; onEdit: () => void }) {
  const userId = useAuth().user?.id
  const client = useQueryClient()
  const [status, setStatus] = useState<GameStatus>(item.status)
  const [priority, setPriority] = useState<Priority | null>(item.priority)
  const busy = useIsMutating({ mutationKey: ['library-write', userId, item.id] }) > 0
  const update = useMutation({
    mutationKey: ['library-write', userId, item.id],
    mutationFn: () => updateLibraryItem(item.id, status, priority),
    onSuccess: async saved => {
      onSaved()
      client.setQueryData(libraryItemKey(userId, item.id), saved)
      client.setQueryData<GameDetails>(gameDetailsKey(userId, item.game.igdbId), cached => cached
        ? { ...cached, library: { isAdded: true, userGameId: item.id, status: saved.status } } : cached)
      await refreshLibrary(client, userId, item.game.igdbId)
    },
  })
  return <section className="space-y-4 rounded-xl border p-4">
    <h2 className="text-lg font-semibold">Library status</h2>
    <LibraryStatusFields prefix="edit" status={status} priority={priority} disabled={busy}
      onStatus={value => { setStatus(value); if (value !== 'Backlog') setPriority(null); update.reset(); onEdit() }}
      onPriority={value => { setPriority(value); update.reset(); onEdit() }} />
    <Button disabled={busy} onClick={() => update.mutate()}>{update.isPending ? 'Saving…' : 'Save changes'}</Button>
    {update.isError && <p role="alert" className="text-sm text-destructive">Could not save changes. Please try again.</p>}
    <p className="text-sm text-muted-foreground">Current status: {statusLabel(item.status)}</p>
  </section>
}

function RemoveItem({ item, backUrl }: { item: LibraryItem; backUrl: string }) {
  const userId = useAuth().user?.id
  const client = useQueryClient()
  const navigate = useNavigate()
  const dialog = useRef<HTMLDialogElement>(null)
  const busy = useIsMutating({ mutationKey: ['library-write', userId, item.id] }) > 0
  const remove = useMutation({
    mutationKey: ['library-write', userId, item.id], mutationFn: () => deleteLibraryItem(item.id),
    onSuccess: async () => {
      dialog.current?.close()
      client.setQueryData<GameDetails>(gameDetailsKey(userId, item.game.igdbId), cached => cached
        ? { ...cached, library: { isAdded: false, userGameId: null, status: null } } : cached)
      navigate(backUrl, { replace: true, state: { notice: `${item.game.name} was removed from your library.` } })
      client.removeQueries({ queryKey: libraryItemKey(userId, item.id), exact: true })
      await refreshLibrary(client, userId, item.game.igdbId)
    },
  })
  return <section>
    <Button variant="destructive" disabled={busy} onClick={() => { remove.reset(); dialog.current?.showModal() }}>Remove from library</Button>
    <dialog ref={dialog} aria-labelledby="remove-title" aria-describedby="remove-description"
      className="m-auto w-[calc(100%_-_2rem)] max-w-md space-y-4 rounded-xl border bg-card p-6 text-card-foreground shadow-xl backdrop:bg-black/50"
      onCancel={event => { if (remove.isPending) event.preventDefault() }}>
      <h2 id="remove-title" className="text-xl font-semibold">Remove {item.game.name}?</h2>
      <p id="remove-description" className="text-sm text-muted-foreground">This removes its status, rating, and personal note from your library. You can add the game again later.</p>
      {remove.isError && <p role="alert" className="text-sm text-destructive">Could not remove this game. Please try again.</p>}
      <div className="flex gap-3">
        <Button variant="outline" disabled={remove.isPending} onClick={() => dialog.current?.close()}>Cancel</Button>
        <Button variant="destructive" disabled={remove.isPending} onClick={() => remove.mutate()}>{remove.isPending ? 'Removing…' : 'Confirm removal'}</Button>
      </div>
    </dialog>
  </section>
}

function Field({ label, value }: { label: string; value: string }) {
  return <div><dt className="text-sm font-medium text-muted-foreground">{label}</dt><dd>{value}</dd></div>
}
function dateLabel(value: string | null) {
  return value ? new Date(value).toLocaleDateString('en', { year: 'numeric', month: 'short', day: 'numeric' }) : 'Not recorded'
}
