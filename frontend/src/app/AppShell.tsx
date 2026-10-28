import { useState } from 'react'
import { Link, NavLink, Outlet } from 'react-router'
import { Button } from '@/components/ui/button'
import { useAuth } from '@/features/auth/useAuth'
import { ApiError } from '@/lib/apiClient'
import { ThemeSelector } from '@/app/theme/ThemeSelector'
import { Gamepad2 } from 'lucide-react'
import { useMainFocus } from './useMainFocus'

export function AppShell() {
  const main = useMainFocus()
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
    <a href="#main-content" className="fixed left-4 top-4 z-[100] -translate-y-24 rounded-lg bg-primary px-4 py-3 font-medium text-primary-foreground focus:translate-y-0"
      onClick={event => { event.preventDefault(); main.current?.focus() }}>Skip to main content</a>
    <header className="border-b bg-card">
      <div className="mx-auto flex max-w-6xl flex-wrap items-center gap-4 px-4 py-4 sm:px-6">
        <Link to="/" className="inline-flex min-h-11 items-center gap-2 text-xl font-semibold tracking-tight"><Gamepad2 aria-hidden="true" className="text-primary" />Ludaryx</Link>
        <nav aria-label="Main navigation" className="order-3 flex w-full flex-wrap gap-1 text-sm lg:order-none lg:w-auto lg:flex-1">
          <NavLink to="/" end className={navigationClass}>Dashboard</NavLink>
          <NavLink to="/games" className={navigationClass}>Discover</NavLink>
          <NavLink to="/library" className={navigationClass}>My library</NavLink>
          <NavLink to="/backlog" className={navigationClass}>Backlog</NavLink>
        </nav>
        <div className="ml-auto flex flex-wrap items-center gap-3">
        <ThemeSelector />
        <Button variant="outline" disabled={busy} onClick={signOut}>{busy ? 'Signing out…' : 'Sign out'}</Button>
        </div>
      </div>
      {error && <p role="alert" className="mx-auto max-w-6xl px-6 pb-4 text-sm text-destructive">{error}</p>}
    </header>
    <main ref={main} id="main-content" tabIndex={-1} aria-label="Main content" className="mx-auto min-w-0 max-w-6xl px-4 py-6 [overflow-wrap:anywhere] sm:px-6 sm:py-10"><Outlet /></main>
  </div>
}

function navigationClass({ isActive }: { isActive: boolean }) {
  return `inline-flex min-h-11 items-center rounded-lg px-3 font-medium outline-none focus-visible:ring-2 focus-visible:ring-ring ${isActive ? 'bg-accent text-accent-foreground' : 'text-muted-foreground hover:bg-muted hover:text-foreground'}`
}
