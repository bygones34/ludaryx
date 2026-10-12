import { useEffect, useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useLocation, useSearchParams } from 'react-router'
import { Button } from '@/components/ui/button'
import { useAuth } from '@/features/auth/useAuth'
import { GameCover } from '@/features/games/GameCover'
import { ApiError } from '@/lib/apiClient'
import { gameStatuses, getLibrary, librarySorts, priorities, statusLabel } from './libraryApi'
import type { LibraryPage as LibraryPageData } from './libraryApi'
import { libraryInputClass } from './LibraryStatusFields'
import { libraryKey } from './libraryQueries'

export function LibraryPage() {
  const userId = useAuth().user?.id
  const client = useQueryClient()
  const location = useLocation()
  const [params, setParams] = useSearchParams()
  const search = params.get('search') ?? ''
  const trimmed = search.trim()
  const [debounced, setDebounced] = useState(trimmed)
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(trimmed), 350)
    return () => clearTimeout(timer)
  }, [trimmed])
  const page = Number(params.get('page') ?? 1)
  const pageSize = Number(params.get('pageSize') ?? 20)
  const status = params.get('status') ?? ''
  const priority = params.get('priority') ?? ''
  const rating = params.get('rating') ?? ''
  const platform = params.get('platform') ?? ''
  const sort = params.get('sort') ?? 'addedAtDesc'
  const valid = Number.isSafeInteger(page) && page > 0 && Number.isSafeInteger(pageSize) && pageSize >= 1 && pageSize <= 100
    && (!status || (gameStatuses as readonly string[]).includes(status))
    && (!priority || (priorities as readonly string[]).includes(priority))
    && (!rating || (Number.isInteger(Number(rating)) && Number(rating) >= 1 && Number(rating) <= 10))
    && (!platform || (Number.isSafeInteger(Number(platform)) && Number(platform) > 0))
    && librarySorts.some(([value]) => value === sort) && trimmed.length <= 100
  const ready = valid && trimmed === debounced
  const request = new URLSearchParams({ page: String(page), pageSize: String(pageSize), sort })
  for (const [key, value] of Object.entries({ status, priority, rating, platform, search: trimmed })) {
    if (value) request.set(key, value)
  }
  const results = useQuery({
    queryKey: [...libraryKey(userId), 'list', request.toString()],
    queryFn: ({ signal }) => getLibrary(request, signal), enabled: ready, retry: false,
  })
  const platformOptions = results.data?.availablePlatforms ?? client.getQueriesData<LibraryPageData>({ queryKey: [...libraryKey(userId), 'list'] })
    .map(([, value]) => value?.availablePlatforms).find(value => value !== undefined) ?? []
  useEffect(() => {
    if (ready && results.isSuccess && results.data.totalPages > 0 && page > results.data.totalPages) {
      setParams(current => { const next = new URLSearchParams(current); next.set('page', String(results.data.totalPages)); return next }, { replace: true })
    }
  }, [page, ready, results.isSuccess, results.data?.totalPages, setParams])

  function filter(key: string, value: string) {
    setParams(current => {
      const next = new URLSearchParams(current)
      if (value) next.set(key, value); else next.delete(key)
      next.delete('page')
      return next
    }, { replace: true })
  }
  function movePage(nextPage: number) {
    setParams(current => { const next = new URLSearchParams(current); next.set('page', String(nextPage)); return next })
  }
  const notice = (location.state as { notice?: string } | null)?.notice
  return <section className="space-y-8">
    <div className="space-y-2">
      <h1 className="text-3xl font-semibold tracking-tight">My library</h1>
      <p className="text-muted-foreground">Your games, organized your way.</p>
      <Link to="/games" className="text-sm underline underline-offset-4">Discover more games</Link>
    </div>
    {notice && <p role="status">{notice}</p>}
    <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
      <div className="space-y-1.5 sm:col-span-2">
        <label htmlFor="library-search" className="text-sm font-medium">Search your library</label>
        <input id="library-search" type="search" className={libraryInputClass} value={search} maxLength={100}
          onChange={event => filter('search', event.target.value)} />
      </div>
      <FilterSelect label="Status filter" value={status} onChange={value => filter('status', value)}>
        <option value="">All statuses</option>{gameStatuses.map(value => <option key={value} value={value}>{statusLabel(value)}</option>)}
      </FilterSelect>
      <FilterSelect label="Platform filter" value={platform} onChange={value => filter('platform', value)}>
        <option value="">All platforms</option>
        {platform && !platformOptions.some(value => String(value.igdbId) === platform) && <option value={platform}>Unavailable platform</option>}
        {platformOptions.map(value => <option key={value.igdbId} value={value.igdbId}>{value.name}</option>)}
      </FilterSelect>
      <FilterSelect label="Rating filter" value={rating} onChange={value => filter('rating', value)}>
        <option value="">All ratings</option>{Array.from({ length: 10 }, (_, index) => index + 1).map(value => <option key={value}>{value}</option>)}
      </FilterSelect>
      <FilterSelect label="Priority filter" value={priority} onChange={value => filter('priority', value)}>
        <option value="">All priorities</option>{priorities.map(value => <option key={value}>{value}</option>)}
      </FilterSelect>
      <FilterSelect label="Sort by" value={sort} onChange={value => filter('sort', value)}>
        {librarySorts.map(([value, label]) => <option key={value} value={value}>{label}</option>)}
      </FilterSelect>
      <FilterSelect label="Games per page" value={String(pageSize)} onChange={value => filter('pageSize', value)}>
        {![10, 20, 50, 100].includes(pageSize) && valid && <option value={pageSize}>{pageSize}</option>}
        {[10, 20, 50, 100].map(value => <option key={value}>{value}</option>)}
      </FilterSelect>
    </div>
    <Button variant="outline" onClick={() => setParams({}, { replace: true })}>Reset filters</Button>
    {!valid && <p role="alert">Invalid library filters. Reset filters to continue.</p>}
    {valid && (!ready || results.isPending) && <p role="status">Loading your library…</p>}
    {ready && results.isError && <div role="alert" className="space-y-3 rounded-xl border p-6">
      <p>{results.error instanceof ApiError && results.error.status === 400 ? 'Please check your library filters.' : 'Could not load your library. Please try again.'}</p>
      <Button variant="outline" onClick={() => { void results.refetch() }}>Try again</Button>
    </div>}
    {ready && results.isSuccess && <>
      <p role="status" className="text-sm text-muted-foreground">{results.data.totalCount} games · Page {results.data.totalPages === 0 ? 0 : results.data.page} of {results.data.totalPages}</p>
      {results.data.items.length === 0
        ? <div className="space-y-2 rounded-xl bg-muted p-6"><p>No games match your library filters.</p><p className="text-sm text-muted-foreground">Reset filters or discover a game to add.</p></div>
        : <ul className="grid gap-6 sm:grid-cols-2 lg:grid-cols-4">
          {results.data.items.map(item => <li key={item.id}>
            <Link to={`/library/${item.id}`} state={{ from: `/library?${params.toString()}` }} className="block h-full space-y-3 rounded-xl border bg-card p-3 shadow-sm focus-visible:outline-2 focus-visible:outline-offset-4">
              <GameCover url={item.game.coverUrl} name={item.game.name} />
              <div className="space-y-1 px-1 pb-2">
                <h2 className="font-semibold">{item.game.name}</h2>
                <p className="text-sm">{statusLabel(item.status)}{item.priority ? ` · ${item.priority} priority` : ''}</p>
                <p className="text-sm text-muted-foreground">{item.rating === null ? 'Not rated' : `Rating: ${item.rating}/10`}</p>
              </div>
            </Link>
          </li>)}
        </ul>}
      <nav aria-label="Library pagination" className="flex gap-3">
        <Button variant="outline" disabled={page <= 1} onClick={() => movePage(page - 1)}>Previous page</Button>
        <Button variant="outline" disabled={page >= results.data.totalPages} onClick={() => movePage(page + 1)}>Next page</Button>
      </nav>
    </>}
  </section>
}

function FilterSelect({ label, value, onChange, children }: {
  label: string; value: string; onChange: (value: string) => void; children: React.ReactNode
}) {
  return <label className="space-y-1.5 text-sm font-medium">
    <span>{label}</span><select className={libraryInputClass} value={value} onChange={event => onChange(event.target.value)}>{children}</select>
  </label>
}
