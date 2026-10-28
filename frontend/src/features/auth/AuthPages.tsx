import { useEffect, useRef, useState } from 'react'
import type { FormEvent, ReactNode } from 'react'
import { Link, useLocation, useNavigate } from 'react-router'
import { Button } from '@/components/ui/button'
import { useAuth } from '@/features/auth/useAuth'
import { ApiError } from '@/lib/apiClient'
import { ThemeSelector } from '@/app/theme/ThemeSelector'
import { AuthBackdrop } from '@/components/AuthBackdrop'
import { Gamepad2 } from 'lucide-react'
import { useMainFocus } from '@/app/useMainFocus'

function AuthFrame({ title, description, children }: {
  title: string
  description: string
  children: ReactNode
}) {
  const main = useMainFocus()
  return <main ref={main} tabIndex={-1} aria-label="Main content" className="relative isolate min-h-screen bg-background [overflow-wrap:anywhere]">
    <AuthBackdrop />
    <div className="relative mx-auto flex max-w-6xl flex-wrap items-center justify-between gap-4 px-4 py-6 sm:px-6">
      <Link to="/" className="inline-flex min-h-11 items-center gap-2 text-xl font-semibold tracking-tight"><Gamepad2 aria-hidden="true" className="text-primary" />Ludaryx</Link>
      <ThemeSelector />
    </div>
    <div className="relative mx-auto grid max-w-6xl items-center gap-10 px-4 pb-12 pt-4 sm:px-6 md:py-12 lg:min-h-[calc(100dvh-96px)] lg:grid-cols-[minmax(0,1fr)_minmax(0,448px)] lg:gap-16">
      <div className="min-w-0 space-y-5 lg:space-y-8">
        <p className="text-xs font-semibold tracking-[0.18em] text-primary">YOUR PERSONAL GAME LIBRARY</p>
        <p className="text-4xl font-semibold leading-tight tracking-tight sm:text-5xl lg:text-6xl">Your games.<br /><span className="text-primary">Your journey.</span></p>
        <p className="max-w-md text-base leading-relaxed text-muted-foreground">Keep the games you love close. Make room for your next adventure.</p>
      </div>
    <section className="min-w-0 w-full max-w-md space-y-6 rounded-2xl border bg-card p-5 text-card-foreground shadow-xl shadow-primary/5 sm:p-8">
      <div className="space-y-2">
        <h1 className="text-2xl font-semibold tracking-tight">{title}</h1>
        <p className="text-sm text-muted-foreground">{description}</p>
      </div>
      {children}
    </section>
    </div>
  </main>
}

const inputClass = 'min-h-11 min-w-0 w-full rounded-lg border border-input bg-background px-3 py-2 text-base outline-none focus-visible:ring-2 focus-visible:ring-ring sm:text-sm'

export function LoginPage() {
  const { login } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)
  const failure = useRef<HTMLParagraphElement>(null)
  useEffect(() => { if (error) failure.current?.focus() }, [error])
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
          aria-describedby={error ? 'login-error' : undefined}
          value={email} onChange={event => setEmail(event.target.value)} />
      </div>
      <div className="space-y-1.5">
        <label htmlFor="login-password" className="text-sm font-medium">Password</label>
        <input id="login-password" className={inputClass} type="password" autoComplete="current-password" required
          aria-describedby={error ? 'login-error' : undefined}
          value={password} onChange={event => setPassword(event.target.value)} />
      </div>
      {error && <p ref={failure} id="login-error" tabIndex={-1} role="alert" className="text-sm text-destructive">{error}</p>}
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
  const form = useRef<HTMLFormElement>(null)
  const failure = useRef<HTMLParagraphElement>(null)
  useEffect(() => {
    if (!error) return
    const invalid = form.current?.querySelector<HTMLInputElement>('[aria-invalid="true"]')
    if (invalid) invalid.focus()
    else failure.current?.focus()
  }, [error, fieldErrors])

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
    <form ref={form} className="space-y-4" onSubmit={submit}>
      <div className="space-y-1.5">
        <label htmlFor="register-username" className="text-sm font-medium">Username</label>
        <input id="register-username" className={inputClass} autoComplete="username" required minLength={3} maxLength={30}
          aria-invalid={Boolean(fieldErrors.username)} aria-describedby={fieldErrors.username ? 'username-error' : undefined}
          value={username} onChange={event => setUsername(event.target.value)} />
        {fieldErrors.username && <p id="username-error" className="text-sm text-destructive">{fieldErrors.username.join(' ')}</p>}
      </div>
      <div className="space-y-1.5">
        <label htmlFor="register-email" className="text-sm font-medium">Email</label>
        <input id="register-email" className={inputClass} type="email" autoComplete="email" required
          aria-invalid={Boolean(fieldErrors.email)} aria-describedby={fieldErrors.email ? 'email-error' : undefined}
          value={email} onChange={event => setEmail(event.target.value)} />
        {fieldErrors.email && <p id="email-error" className="text-sm text-destructive">{fieldErrors.email.join(' ')}</p>}
      </div>
      <div className="space-y-1.5">
        <label htmlFor="register-password" className="text-sm font-medium">Password</label>
        <input id="register-password" className={inputClass} type="password" autoComplete="new-password" required minLength={15}
          aria-invalid={Boolean(fieldErrors.password)} aria-describedby={`password-help${fieldErrors.password ? ' password-error' : ''}`} value={password} onChange={event => setPassword(event.target.value)} />
        <p id="password-help" className="text-xs text-muted-foreground">At least 15 characters. Spaces and long passphrases are welcome.</p>
        {fieldErrors.password && <p id="password-error" className="text-sm text-destructive">{fieldErrors.password.join(' ')}</p>}
      </div>
      {error && <p ref={failure} tabIndex={-1} role="alert" className="text-sm text-destructive">{error}</p>}
      <Button className="w-full" type="submit" disabled={busy}>
        {busy ? 'Creating account…' : 'Create account'}
      </Button>
    </form>
    <p className="text-center text-sm text-muted-foreground">
      Already have an account? <Link className="font-medium text-foreground underline" to="/login">Sign in</Link>
    </p>
  </AuthFrame>
}
