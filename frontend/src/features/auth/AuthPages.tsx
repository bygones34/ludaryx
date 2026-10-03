import { useState } from 'react'
import type { FormEvent, ReactNode } from 'react'
import { Link, useLocation, useNavigate } from 'react-router'
import { Button } from '@/components/ui/button'
import { useAuth } from '@/features/auth/useAuth'
import { ApiError } from '@/lib/apiClient'

function AuthFrame({ title, description, children }: {
  title: string
  description: string
  children: ReactNode
}) {
  return <main className="flex min-h-screen items-center justify-center bg-background px-5 py-10">
    <section className="w-full max-w-md space-y-6 rounded-xl border bg-card p-8 text-card-foreground shadow-sm">
      <div className="space-y-2">
        <p className="text-sm font-semibold text-muted-foreground">Ludaryx</p>
        <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
        <p className="text-sm text-muted-foreground">{description}</p>
      </div>
      {children}
    </section>
  </main>
}

const inputClass = 'w-full rounded-md border bg-background px-3 py-2 text-sm outline-none focus-visible:ring-2 focus-visible:ring-ring'

export function LoginPage() {
  const { login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const registered = (location.state as { registered?: boolean } | null)?.registered
  const from = (location.state as { from?: string } | null)?.from ?? '/'

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError('')
    setBusy(true)
    try {
      await login(email, password)
      navigate(from, { replace: true })
    } catch (caught) {
      setError(caught instanceof ApiError ? caught.message : 'Could not connect to the API. Please try again.')
    } finally {
      setBusy(false)
    }
  }

  return <AuthFrame title="Welcome back" description="Sign in to your private game library.">
    {registered && <p role="status" className="text-sm">Account created. Sign in to continue.</p>}
    <form className="space-y-4" onSubmit={submit}>
      <div className="space-y-1.5">
        <label htmlFor="login-email" className="text-sm font-medium">Email</label>
        <input id="login-email" className={inputClass} type="email" autoComplete="email" required
          value={email} onChange={event => setEmail(event.target.value)} />
      </div>
      <div className="space-y-1.5">
        <label htmlFor="login-password" className="text-sm font-medium">Password</label>
        <input id="login-password" className={inputClass} type="password" autoComplete="current-password" required
          value={password} onChange={event => setPassword(event.target.value)} />
      </div>
      {error && <p role="alert" className="text-sm text-destructive">{error}</p>}
      <Button className="w-full" type="submit" disabled={busy}>
        {busy ? 'Signing in…' : 'Sign in'}
      </Button>
    </form>
    <p className="text-center text-sm text-muted-foreground">
      New to Ludaryx? <Link className="font-medium text-foreground underline" to="/register">Create an account</Link>
    </p>
  </AuthFrame>
}

export function RegisterPage() {
  const { register } = useAuth()
  const navigate = useNavigate()
  const [username, setUsername] = useState('')
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({})
  const [busy, setBusy] = useState(false)

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setError('')
    setFieldErrors({})
    setBusy(true)
    try {
      await register(username, email, password)
      navigate('/login', { replace: true, state: { registered: true } })
    } catch (caught) {
      if (caught instanceof ApiError) {
        setError(caught.message)
        setFieldErrors(caught.errors)
      } else {
        setError('Could not connect to the API. Please try again.')
      }
    } finally {
      setBusy(false)
    }
  }

  return <AuthFrame title="Create your account" description="Start your private game library.">
    <form className="space-y-4" onSubmit={submit}>
      <div className="space-y-1.5">
        <label htmlFor="register-username" className="text-sm font-medium">Username</label>
        <input id="register-username" className={inputClass} autoComplete="username" required minLength={3} maxLength={30}
          value={username} onChange={event => setUsername(event.target.value)} />
        {fieldErrors.username && <p className="text-sm text-destructive">{fieldErrors.username.join(' ')}</p>}
      </div>
      <div className="space-y-1.5">
        <label htmlFor="register-email" className="text-sm font-medium">Email</label>
        <input id="register-email" className={inputClass} type="email" autoComplete="email" required
          value={email} onChange={event => setEmail(event.target.value)} />
        {fieldErrors.email && <p className="text-sm text-destructive">{fieldErrors.email.join(' ')}</p>}
      </div>
      <div className="space-y-1.5">
        <label htmlFor="register-password" className="text-sm font-medium">Password</label>
        <input id="register-password" className={inputClass} type="password" autoComplete="new-password" required minLength={15}
          aria-describedby="password-help" value={password} onChange={event => setPassword(event.target.value)} />
        <p id="password-help" className="text-xs text-muted-foreground">At least 15 characters. Spaces and long passphrases are welcome.</p>
        {fieldErrors.password && <p className="text-sm text-destructive">{fieldErrors.password.join(' ')}</p>}
      </div>
      {error && <p role="alert" className="text-sm text-destructive">{error}</p>}
      <Button className="w-full" type="submit" disabled={busy}>
        {busy ? 'Creating account…' : 'Create account'}
      </Button>
    </form>
    <p className="text-center text-sm text-muted-foreground">
      Already have an account? <Link className="font-medium text-foreground underline" to="/login">Sign in</Link>
    </p>
  </AuthFrame>
}
