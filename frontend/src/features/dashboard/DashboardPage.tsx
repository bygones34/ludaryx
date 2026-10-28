import type { ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { LoadingState, EmptyState, ErrorState } from '@/components/feedback/QueryState'
import { useAuth } from '@/features/auth/useAuth'
import { GameCover } from '@/features/games/GameCover'
import { AccentCard } from '@/components/AccentCard'
import { dashboardKey, getDashboard } from './dashboardApi'
import type { DashboardGame, DashboardResponse } from './dashboardApi'

export function DashboardPage() {
  const { user } = useAuth()
  const dashboard = useQuery({
    queryKey: dashboardKey(user?.id), queryFn: ({ signal }) => getDashboard(signal),
    enabled: Boolean(user?.id), retry: false,
  })
  return <section className="space-y-8">
    <div className="space-y-2">
      <p className="text-sm font-medium text-muted-foreground">Your games. Your journey.</p>
      <h1 className="text-3xl font-semibold tracking-tight">Dashboard</h1>
      <p className="text-muted-foreground">Welcome, {user?.displayName}. Here is your gaming journey so far.</p>
    </div>
    {dashboard.isPending && <LoadingState message="Loading your dashboard…" layout="dashboard" />}
    {dashboard.isError && <ErrorState message={dashboard.data ? 'Could not refresh your dashboard. The last saved summary is shown.'
        : 'Could not load your dashboard. Please try again.'} retry={() => { void dashboard.refetch() }} retrying={dashboard.isFetching} />}
    {dashboard.data && <>
      {dashboard.isFetching && <p role="status">Refreshing dashboard…</p>}
      <Statistics statistics={dashboard.data.statistics} />
      {dashboard.data.statistics.totalGames === 0 && <div className="space-y-3 rounded-xl border bg-card p-6">
        <h2 className="text-xl font-semibold">Your library starts here</h2>
        <p className="text-muted-foreground">Discover a game and add it to your library to begin tracking your journey.</p>
        <Link to="/games" className="text-sm font-medium underline underline-offset-4">Explore games</Link>
      </div>}
      <div className="grid min-w-0 grid-cols-1 items-start gap-8 lg:grid-cols-2">
        <section aria-labelledby="currently-playing-heading" className="min-w-0 space-y-4">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <h2 id="currently-playing-heading" className="text-xl font-semibold">Currently Playing</h2>
            <Link to="/library?status=Playing" className="text-sm underline underline-offset-4">View playing games</Link>
          </div>
          <p className="text-sm text-muted-foreground">Up to five games, ordered by when first started, newest first.</p>
          {dashboard.data.currentlyPlaying.length === 0 ? <EmptyState title="No games currently playing." description="Open a library game and choose Playing to track it here." />
            : <ul aria-label="Currently playing games" className="space-y-3">
              {dashboard.data.currentlyPlaying.map(game => <li key={game.userGameId}>
                <GameSummary game={game}><p className="text-sm text-muted-foreground">{game.rating === null ? 'Not rated' : `${game.rating}/10`}</p></GameSummary>
              </li>)}
            </ul>}
        </section>
        <section aria-labelledby="up-next-heading" className="min-w-0 space-y-4">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <h2 id="up-next-heading" className="text-xl font-semibold">Up Next</h2>
            <Link to="/backlog" className="text-sm underline underline-offset-4">View full backlog</Link>
          </div>
          <p className="text-sm text-muted-foreground">Up to five games in your saved backlog order.</p>
          {dashboard.data.upNext.length === 0 ? <EmptyState title="Your backlog is empty." description="Add a game with Backlog status to plan what comes next." />
            : <ol aria-label="Up next games" className="space-y-3">
              {dashboard.data.upNext.map(game => <li key={game.userGameId}>
                <GameSummary game={game}>
                  <p className="text-sm text-muted-foreground">Position {game.order} · {game.priority === null ? 'No priority' : `${game.priority} priority`}</p>
                </GameSummary>
              </li>)}
            </ol>}
        </section>
      </div>
    </>}
  </section>
}

function Statistics({ statistics }: { statistics: DashboardResponse['statistics'] }) {
  const values = [
    ['Total games', statistics.totalGames], ['Playing', statistics.playing], ['Completed', statistics.completed],
    ['Backlog', statistics.backlog], ['On hold', statistics.onHold], ['Dropped', statistics.dropped],
    ['Average rating', statistics.averageRating === null ? 'Not rated' : `${statistics.averageRating.toFixed(1)}/10`],
  ] as const
  return <dl aria-label="Library statistics" className="grid grid-cols-2 gap-3 sm:grid-cols-3 xl:grid-cols-4">
    {values.map(([label, value]) => <AccentCard key={label} className="space-y-2 p-4 sm:p-5">
      <dt className="text-sm text-muted-foreground">{label}</dt>
      <dd className="text-2xl font-semibold tabular-nums">{value}</dd>
    </AccentCard>)}
  </dl>
}

function GameSummary({ game, children }: { game: DashboardGame; children: ReactNode }) {
  return <AccentCard className="flex items-center gap-4 p-4">
    <div className="w-16 shrink-0"><GameCover compact url={game.coverUrl} name={game.name} /></div>
    <div className="min-w-0 space-y-2">
      <Link to={`/library/${game.userGameId}`} className="font-semibold underline underline-offset-4">{game.name}</Link>
      {children}
    </div>
  </AccentCard>
}
