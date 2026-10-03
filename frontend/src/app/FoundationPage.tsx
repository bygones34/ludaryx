import { Button } from '@/components/ui/button'
import { useState } from 'react'
import { useAuth } from '@/features/auth/useAuth'
import { ApiError } from '@/lib/apiClient'

export function FoundationPage() {
  const { user, logout } = useAuth()
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  async function signOut() {
    setError('')
    setBusy(true)
    try {
      await logout()
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Could not sign out. Please try again.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <main className="flex min-h-screen items-center justify-center bg-background px-6 py-12 text-foreground">
      <div className="w-full max-w-xl space-y-5 rounded-xl border bg-card p-8 text-card-foreground shadow-sm">
        <p className="text-sm font-medium text-muted-foreground">Ludaryx</p>
        <h1 className="text-3xl font-semibold tracking-tight">Welcome, {user?.displayName}.</h1>
        <p className="text-muted-foreground">
          The project foundation is ready. Game discovery and your private library are coming next.
        </p>
        <div className="flex flex-wrap items-center gap-3">
          <Button type="button" disabled>Explore games soon</Button>
          <Button type="button" variant="outline" disabled={busy} onClick={signOut}>
            {busy ? 'Signing out…' : 'Sign out'}
          </Button>
        </div>
        {error && <p role="alert" className="text-sm text-destructive">{error}</p>}
      </div>
    </main>
  )
}
