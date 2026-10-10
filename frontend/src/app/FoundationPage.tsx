import { Link } from 'react-router'
import { useAuth } from '@/features/auth/useAuth'

export function FoundationPage() {
  const { user } = useAuth()
  return <section className="max-w-2xl space-y-5 rounded-xl border bg-card p-8 text-card-foreground shadow-sm">
    <p className="text-sm font-medium text-muted-foreground">Your games. Your journey.</p>
    <h1 className="text-3xl font-semibold tracking-tight">Welcome, {user?.displayName}.</h1>
    <p className="text-muted-foreground">Find your next game. Explore covers, release dates, genres, and platforms.</p>
    <Link to="/games" className="inline-flex rounded-lg bg-primary px-4 py-2 text-sm font-medium text-primary-foreground focus-visible:outline-2 focus-visible:outline-offset-2">Explore games</Link>
  </section>
}
