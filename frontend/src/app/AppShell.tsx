import { useState } from 'react'
import { Link, NavLink, Outlet } from 'react-router'
import { Button } from '@/components/ui/button'
import { useAuth } from '@/features/auth/useAuth'
import { ApiError } from '@/lib/apiClient'

export function AppShell() {
  const { logout } = useAuth()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  async function signOut() {
    setBusy(true)
    setError('')
    try { await logout() }
    catch (caught) { setError(caught instanceof ApiError ? caught.message : 'Could not sign out. Please try again.') }
    finally { setBusy(false) }
  }
  return <div className="min-h-screen bg-background text-foreground">
    <header className="border-b">
      <div className="mx-auto flex max-w-6xl flex-wrap items-center gap-6 px-6 py-4">
        <Link to="/" className="text-xl font-semibold tracking-tight">Ludaryx</Link>
        <nav aria-label="Main navigation" className="flex flex-1 flex-wrap gap-4 text-sm">
          <NavLink to="/" end className={({ isActive }) => isActive ? 'font-semibold underline underline-offset-4' : 'text-muted-foreground'}>Dashboard</NavLink>
          <NavLink to="/games" className={({ isActive }) => isActive ? 'font-semibold underline underline-offset-4' : 'text-muted-foreground'}>Discover</NavLink>
          <NavLink to="/library" className={({ isActive }) => isActive ? 'font-semibold underline underline-offset-4' : 'text-muted-foreground'}>My library</NavLink>
          <NavLink to="/backlog" className={({ isActive }) => isActive ? 'font-semibold underline underline-offset-4' : 'text-muted-foreground'}>Backlog</NavLink>
        </nav>
        <Button variant="outline" disabled={busy} onClick={signOut}>{busy ? 'Signing out…' : 'Sign out'}</Button>
      </div>
      {error && <p role="alert" className="mx-auto max-w-6xl px-6 pb-4 text-sm text-destructive">{error}</p>}
    </header>
    <main className="mx-auto max-w-6xl px-6 py-10"><Outlet /></main>
  </div>
}
