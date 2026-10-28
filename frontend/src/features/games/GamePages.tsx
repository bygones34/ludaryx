import { useEffect, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useLocation, useParams, useSearchParams } from 'react-router'
import { LoadingState, EmptyState, ErrorState } from '@/components/feedback/QueryState'
import { ApiError } from '@/lib/apiClient'
import { GameCover } from './GameCover'
import { getGameDetails, searchGames } from './gamesApi'
import { useAuth } from '@/features/auth/useAuth'
import { AddToLibrary } from '@/features/library/AddToLibrary'
import { gameDetailsKey } from '@/features/library/libraryQueries'

function QueryError({ error, retry }: { error: Error; retry: () => void }) {
  const message = error instanceof ApiError && error.status === 404
    ? 'This game could not be found.'
    : error instanceof ApiError && error.status === 400
      ? 'Please check your search or game ID.'
      : 'Could not load games. Please try again.'
  return <ErrorState message={message} retry={retry} />
}

export function GameSearchPage() {
  const [params, setParams] = useSearchParams()
  const input = params.get('q') ?? ''
  const query = input.trim()
  const [debounced, setDebounced] = useState('')
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(query), 350)
    return () => clearTimeout(timer)
  }, [query])
  const hasControlCharacter = Array.from(query).some(character => {
    const code = character.charCodeAt(0)
    return code < 32 || (code >= 127 && code <= 159)
  })
  const valid = query.length >= 2 && query.length <= 100 && !hasControlCharacter
  const ready = valid && query === debounced
  const results = useQuery({
    queryKey: ['games', 'search', query],
    queryFn: ({ signal }) => searchGames(query, signal),
    enabled: ready,
    retry: false,
  })
  return <section className="space-y-8">
    <div className="space-y-2">
      <p className="text-sm text-muted-foreground">Discover</p>
      <h1 className="text-3xl font-semibold tracking-tight">Find your next game</h1>
      <p className="text-muted-foreground">Search the IGDB catalog and explore the details.</p>
    </div>
    <div className="max-w-2xl space-y-2">
      <label htmlFor="game-search" className="text-sm font-medium">Search games</label>
      <input id="game-search" type="search" value={input} maxLength={100} autoComplete="off"
        aria-invalid={query.length > 0 && !valid} aria-describedby="search-help" className="w-full rounded-lg border border-input bg-background px-4 py-3 outline-none focus-visible:ring-2 focus-visible:ring-ring"
        placeholder="Try Elden Ring, Hades, or The Witcher"
        onChange={event => setParams(event.target.value ? { q: event.target.value } : {}, { replace: true })} />
      <p id="search-help" className="text-sm text-muted-foreground">Enter 2–100 characters. Up to 20 matches are shown.</p>
    </div>
    {!valid && <EmptyState title="Enter a game name with 2–100 characters to start exploring." />}
    {valid && (!ready || results.isPending) && <LoadingState message="Searching games…" />}
    {ready && results.isError && <QueryError error={results.error} retry={() => { void results.refetch() }} />}
    {ready && results.data && <>
      {results.isFetching && <p role="status">Refreshing games…</p>}
      <p role="status" className="text-sm text-muted-foreground">
        {results.data.items.length === 0 ? `No games found for “${query}”. Try another name.` : `${results.data.items.length} games found for “${query}”.`}
      </p>
      <ul className="grid grid-cols-1 gap-6 min-[480px]:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
        {results.data.items.map(game => <li key={game.igdbId} className="min-w-0">
          <Link to={`/games/${game.igdbId}`} state={{ search: input }} className="block h-full space-y-3 rounded-xl border bg-card p-3 shadow-sm transition-shadow hover:shadow-md focus-visible:outline-2 focus-visible:outline-offset-4">
            <GameCover url={game.coverUrl} name={game.name} />
            <div className="space-y-1 px-1 pb-2">
              <h2 className="font-semibold">{game.name}</h2>
              <p className="text-sm text-muted-foreground">{game.releaseDate ?? 'Release date unavailable'}</p>
              <p className="text-sm text-muted-foreground">{game.platforms.join(' · ') || 'Platforms unavailable'}</p>
            </div>
          </Link>
        </li>)}
      </ul>
    </>}
  </section>
}

export function GameDetailsPage() {
  const userId = useAuth().user?.id
  const { igdbId } = useParams()
  const location = useLocation()
  const previousSearch = (location.state as { search?: string } | null)?.search
  const backUrl = previousSearch ? `/games?q=${encodeURIComponent(previousSearch)}` : '/games'
  const id = Number(igdbId)
  const valid = /^\d+$/.test(igdbId ?? '') && Number.isSafeInteger(id) && id > 0
  const details = useQuery({
    queryKey: gameDetailsKey(userId, id),
    queryFn: ({ signal }) => getGameDetails(id, signal),
    enabled: valid,
    retry: false,
  })
  return <section className="space-y-8">
    <Link to={backUrl} className="text-sm underline underline-offset-4">Back to search</Link>
    {!valid && <p role="alert">Invalid game ID. Choose a game from search results.</p>}
    {valid && details.isPending && <LoadingState message="Loading game details…" layout="details" />}
    {valid && details.isError && <QueryError error={details.error} retry={() => { void details.refetch() }} />}
    {valid && details.data && details.isFetching && <p role="status">Refreshing game details…</p>}
    {valid && details.data && <div className="grid items-start gap-6 md:grid-cols-[200px_minmax(0,1fr)] lg:grid-cols-[240px_minmax(0,1fr)] lg:gap-8">
      <div className="w-40 max-w-full sm:w-48 md:w-full"><GameCover url={details.data.coverUrl} name={details.data.name} /></div>
      <div className="min-w-0 space-y-6">
        <h1 className="text-3xl font-semibold tracking-tight">{details.data.name}</h1>
        <AddToLibrary key={details.data.igdbId} game={details.data} />
        <dl className="space-y-4">
          <div><dt className="text-sm font-medium text-muted-foreground">Release date</dt><dd>{details.data.releaseDate ?? 'Release date unavailable'}</dd></div>
          <div><dt className="text-sm font-medium text-muted-foreground">Genres</dt><dd>{details.data.genres.map(genre => genre.name).join(' · ') || 'Genres unavailable'}</dd></div>
          <div><dt className="text-sm font-medium text-muted-foreground">Platforms</dt><dd>{details.data.platforms.map(platform => platform.name).join(' · ') || 'Platforms unavailable'}</dd></div>
        </dl>
        <div className="space-y-2">
          <h2 className="text-xl font-semibold">About this game</h2>
          <p className="whitespace-pre-line leading-relaxed text-muted-foreground">{details.data.summary ?? 'No summary available.'}</p>
        </div>
      </div>
    </div>}
  </section>
}
