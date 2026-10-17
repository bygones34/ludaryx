import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from '@/App'
import { AuthProvider } from '@/features/auth/AuthProvider'
import { clearAccessToken } from '@/lib/apiClient'

const user = {
  id: 'user-id', username: 'player', email: 'player@example.com',
  displayName: 'player', createdAt: '2026-10-03T00:00:00Z',
}
const emptyDashboard = {
  statistics: { totalGames: 0, playing: 0, completed: 0, backlog: 0, onHold: 0, dropped: 0, averageRating: null },
  currentlyPlaying: [], upNext: [],
}

function json(body: object, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

function renderApp(path = '/') {
  const client = new QueryClient()
  render(<QueryClientProvider client={client}>
    <MemoryRouter initialEntries={[path]}>
      <AuthProvider><App /></AuthProvider>
    </MemoryRouter>
  </QueryClientProvider>)
  return client
}

beforeEach(() => clearAccessToken())
afterEach(() => vi.unstubAllGlobals())

describe('authentication navigation', () => {
  it('sends anonymous visitors to sign in without showing private content', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ title: 'Invalid session' }, 401)))
    renderApp()
    expect(screen.getByText('Restoring your session…')).toBeInTheDocument()
    expect(await screen.findByRole('heading', { name: 'Welcome back' })).toBeInTheDocument()
    expect(screen.queryByText('Explore games soon')).not.toBeInTheDocument()
  })

  it('registers, signs in, and signs out', async () => {
    const fetchMock = vi.fn().mockImplementation((url: string) => {
      if (url.endsWith('/auth/refresh')) return Promise.resolve(json({ title: 'Invalid session' }, 401))
      if (url.endsWith('/auth/register')) return Promise.resolve(json({ id: 'user-id', username: 'player', email: user.email }, 201))
      if (url.endsWith('/auth/login')) return Promise.resolve(json({ accessToken: 'access-token', expiresIn: 900, user }))
      if (url.endsWith('/users/me')) return Promise.resolve(json(user))
      if (url.endsWith('/dashboard')) return Promise.resolve(json(emptyDashboard))
      if (url.endsWith('/auth/logout')) return Promise.resolve(new Response(null, { status: 204 }))
      throw new Error(`Unexpected URL: ${url}`)
    })
    vi.stubGlobal('fetch', fetchMock)
    const client = renderApp('/register')

    fireEvent.change(await screen.findByLabelText('Username'), { target: { value: 'player' } })
    fireEvent.change(screen.getByLabelText('Email'), { target: { value: user.email } })
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'a long passphrase with spaces' } })
    fireEvent.click(screen.getByRole('button', { name: 'Create account' }))
    expect(await screen.findByText('Account created. Sign in to continue.')).toBeInTheDocument()

    fireEvent.change(screen.getByLabelText('Email'), { target: { value: user.email } })
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'a long passphrase with spaces' } })
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }))
    expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument()
    expect(await screen.findByText('Your library starts here')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Sign out' }))
    expect(await screen.findByRole('heading', { name: 'Welcome back' })).toBeInTheDocument()
    expect(client.getQueryCache().getAll()).toHaveLength(0)
    expect(fetchMock).toHaveBeenCalledWith(expect.stringContaining('/auth/logout'),
      expect.objectContaining({ credentials: 'include' }))
  })

  it('restores a protected page through the refresh cookie', async () => {
    const fetchMock = vi.fn().mockImplementation((url: string, init: RequestInit) => {
      if (url.endsWith('/auth/refresh')) {
        expect(init.credentials).toBe('include')
        return Promise.resolve(json({ accessToken: 'restored-token', expiresIn: 900 }))
      }
      if (url.endsWith('/users/me')) {
        expect(new Headers(init.headers).get('Authorization')).toBe('Bearer restored-token')
        return Promise.resolve(json(user))
      }
      if (url.endsWith('/dashboard')) {
        expect(new Headers(init.headers).get('Authorization')).toBe('Bearer restored-token')
        return Promise.resolve(json(emptyDashboard))
      }
      throw new Error(`Unexpected URL: ${url}`)
    })
    vi.stubGlobal('fetch', fetchMock)
    renderApp()
    expect(await screen.findByRole('heading', { name: 'Dashboard' })).toBeInTheDocument()
    expect(await screen.findByText('Your library starts here')).toBeInTheDocument()
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(3))
  })

  it('keeps the login form available after a rejected password', async () => {
    vi.stubGlobal('fetch', vi.fn().mockImplementation((url: string) => {
      if (url.endsWith('/auth/refresh')) return Promise.resolve(json({ title: 'Invalid session' }, 401))
      if (url.endsWith('/auth/login')) return Promise.resolve(json({ title: 'Invalid email or password.' }, 401))
      throw new Error(`Unexpected URL: ${url}`)
    }))
    renderApp('/login')
    fireEvent.change(await screen.findByLabelText('Email'), { target: { value: user.email } })
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'wrong password' } })
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Invalid email or password.')
    expect(screen.getByRole('heading', { name: 'Welcome back' })).toBeInTheDocument()
  })

  it('returns to the requested protected location after sign-in instead of the default dashboard', async () => {
    const fetchMock = vi.fn().mockImplementation((url: string) => {
      if (url.endsWith('/auth/refresh')) return Promise.resolve(json({}, 401))
      if (url.endsWith('/auth/login')) return Promise.resolve(json({ accessToken: 'access-token' }))
      if (url.endsWith('/users/me')) return Promise.resolve(json(user))
      if (url.includes('/library?')) return Promise.resolve(json({ items: [], page: 1, pageSize: 20,
        totalCount: 0, totalPages: 0, availablePlatforms: [] }))
      throw new Error(`Unexpected URL: ${url}`)
    })
    vi.stubGlobal('fetch', fetchMock)
    renderApp('/library?status=Completed')
    fireEvent.change(await screen.findByLabelText('Email'), { target: { value: user.email } })
    fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'a long passphrase with spaces' } })
    fireEvent.click(screen.getByRole('button', { name: 'Sign in' }))
    expect(await screen.findByRole('heading', { name: 'My library' })).toBeInTheDocument()
    expect(screen.getByLabelText('Status filter')).toHaveValue('Completed')
    expect(await screen.findByText('No games match your library filters.')).toBeInTheDocument()
    expect(fetchMock.mock.calls.some(([url]) => url.endsWith('/dashboard'))).toBe(false)
  })
})
