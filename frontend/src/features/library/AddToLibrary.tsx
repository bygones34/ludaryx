import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router'
import { useNotify } from '@/components/feedback/toastContext'
import { Button } from '@/components/ui/button'
import { useAuth } from '@/features/auth/useAuth'
import { ApiError } from '@/lib/apiClient'
import type { GameDetails } from '@/features/games/gamesApi'
import { addLibraryGame, statusLabel } from './libraryApi'
import type { GameStatus, Priority } from './libraryApi'
import { gameDetailsKey, refreshLibrary } from './libraryQueries'
import { LibraryStatusFields } from './LibraryStatusFields'

export function AddToLibrary({ game }: { game: GameDetails }) {
  const userId = useAuth().user?.id
  const client = useQueryClient()
  const notify = useNotify()
  const [status, setStatus] = useState<GameStatus>('Backlog')
  const [priority, setPriority] = useState<Priority | null>(null)
  const addition = useMutation({
    mutationFn: () => addLibraryGame(game.igdbId, status, priority),
    onSuccess: async item => {
      notify('Game added to your library.')
      client.setQueryData<GameDetails>(gameDetailsKey(userId, game.igdbId), cached => cached
        ? { ...cached, library: { isAdded: true, userGameId: item.id, status: item.status } } : cached)
      await refreshLibrary(client, userId, game.igdbId)
    },
    onError: error => {
      if (error instanceof ApiError && error.status === 409) void refreshLibrary(client, userId, game.igdbId)
    },
  })
  if (game.library.isAdded) return <section className="space-y-2 rounded-xl border p-4">
    <p>In your library{game.library.status ? ` · ${statusLabel(game.library.status)}` : ''}</p>
    {game.library.userGameId && <Link to={`/library/${game.library.userGameId}`} className="text-sm underline underline-offset-4">Open library item</Link>}
  </section>
  return <section className="space-y-4 rounded-xl border p-4">
    <h2 className="text-lg font-semibold">Add to your library</h2>
    <LibraryStatusFields prefix="add" status={status} priority={priority} disabled={addition.isPending}
      errorId={addition.isError ? 'addition-error' : undefined}
      onStatus={value => { setStatus(value); if (value !== 'Backlog') setPriority(null); addition.reset() }}
      onPriority={value => { setPriority(value); addition.reset() }} />
    <Button disabled={addition.isPending} onClick={() => addition.mutate()}>{addition.isPending ? 'Adding…' : 'Add to Library'}</Button>
    {addition.isError && <p id="addition-error" role="alert" className="text-sm text-destructive">{addition.error instanceof ApiError && addition.error.status === 409
      ? 'This game is already in your library.' : 'Could not add this game. Please try again.'}</p>}
  </section>
}
