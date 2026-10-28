import { StrictMode } from 'react'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router'
import { afterEach, expect, it, vi } from 'vitest'
import App from '@/App'
import { AuthContext } from '@/features/auth/authContext'
import type { AuthContextValue } from '@/features/auth/authContext'
import { ApiError } from '@/lib/apiClient'
import { libraryKey } from '@/features/library/libraryQueries'

const clients: QueryClient[] = []
function setup(path = '/library', overrides: Partial<AuthContextValue> = {}) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  clients.push(client)
  vi.stubGlobal('fetch', vi.fn().mockImplementation(() => Promise.resolve(new Response(JSON.stringify({
    items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0, availablePlatforms: [],
  }), { status: 200 }))))
  render(<StrictMode><QueryClientProvider client={client}><MemoryRouter initialEntries={[path]}>
    <AuthContext.Provider value={{ status: 'authenticated', user: {
      id: 'owner', username: 'player', displayName: 'Player', email: 'player@example.com', createdAt: '2026-01-01',
    }, login: vi.fn(), register: vi.fn(), logout: vi.fn(), ...overrides }}><App /></AuthContext.Provider>
  </MemoryRouter></QueryClientProvider></StrictMode>)
  return client
}
afterEach(() => { cleanup(); clients.splice(0).forEach(client => client.clear()); vi.unstubAllGlobals(); vi.restoreAllMocks(); localStorage.clear() })

it('offers a skip link to a focusable main region and focuses it on pathname navigation', async () => {
  setup()
  const main = screen.getByRole('main', { name: 'Main content' })
  expect(main).toHaveFocus()
  const skip = screen.getByRole('link', { name: 'Skip to main content' })
  expect(skip).toHaveAttribute('href', '#main-content')
  skip.focus(); fireEvent.click(skip); expect(main).toHaveFocus()
  fireEvent.click(screen.getByRole('link', { name: 'Discover' }))
  expect(await screen.findByRole('heading', { name: 'Find your next game' })).toBeInTheDocument()
  expect(main).toHaveFocus()
})

it('does not steal focus for filter URLs, query refresh, theme changes, or text edits', async () => {
  const client = setup()
  await screen.findByText('Your library is empty.')
  const status = screen.getByLabelText('Status filter')
  status.focus(); fireEvent.change(status, { target: { value: 'Playing' } })
  await screen.findByText('No games match your library filters.')
  expect(status).toHaveFocus()
  await act(async () => { await client.invalidateQueries({ queryKey: libraryKey('owner') }) })
  expect(status).toHaveFocus()
  const search = screen.getByLabelText('Search your library')
  search.focus(); fireEvent.change(search, { target: { value: 'Alpha' } })
  await waitFor(() => expect(screen.queryByText('Loading your library…')).not.toBeInTheDocument())
  expect(search).toHaveFocus()
  const theme = screen.getByLabelText('Theme')
  theme.focus(); fireEvent.change(theme, { target: { value: 'dark' } })
  expect(theme).toHaveFocus()
})

it('preserves main focus across an initial asynchronous route query', async () => {
  let finish!: (response: Response) => void
  setup('/games')
  vi.mocked(fetch).mockImplementation(() => new Promise<Response>(resolve => { finish = resolve }))
  fireEvent.click(screen.getByRole('link', { name: 'My library' }))
  expect(screen.getByRole('main')).toHaveFocus()
  await waitFor(() => expect(finish).toBeDefined())
  await act(async () => finish(new Response(JSON.stringify({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0, availablePlatforms: [] }))))
  await screen.findByText('Your library is empty.')
  expect(screen.getByRole('main')).toHaveFocus()
})

it('moves focus between public auth pages without resetting it during typing', async () => {
  setup('/login', { status: 'anonymous', user: null })
  expect(screen.getByRole('main')).toHaveFocus()
  fireEvent.click(screen.getByRole('link', { name: 'Create an account' }))
  expect(screen.getByRole('main')).toHaveFocus()
  const username = await screen.findByLabelText('Username')
  username.focus(); fireEvent.change(username, { target: { value: 'player' } })
  expect(username).toHaveFocus()
})

it('connects registration field errors and password help and focuses the first rejected field', async () => {
  setup('/register', { status: 'anonymous', user: null, register: vi.fn().mockRejectedValue(new ApiError(400, 'Check your fields.', {
    username: ['Username is unavailable.'], email: ['Email is unavailable.'], password: ['Choose another passphrase.'],
  })) })
  fireEvent.change(screen.getByLabelText('Username'), { target: { value: 'player' } })
  fireEvent.change(screen.getByLabelText('Email'), { target: { value: 'player@example.com' } })
  fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'test passphrase for validation' } })
  fireEvent.click(screen.getByRole('button', { name: 'Create account' }))
  await screen.findByRole('alert')
  expect(screen.getByLabelText('Username')).toHaveFocus()
  expect(screen.getByLabelText('Username')).toHaveAccessibleDescription('Username is unavailable.')
  expect(screen.getByLabelText('Email')).toHaveAccessibleDescription('Email is unavailable.')
  expect(screen.getByLabelText('Password')).toHaveAccessibleDescription(/At least 15 characters.*Choose another passphrase\./)
  for (const name of ['Username', 'Email', 'Password']) expect(screen.getByLabelText(name)).toHaveAttribute('aria-invalid', 'true')
})

it('focuses a general registration failure without falsely marking fields invalid', async () => {
  setup('/register', { status: 'anonymous', user: null, register: vi.fn().mockRejectedValue(new Error('Unavailable')) })
  fireEvent.change(screen.getByLabelText('Username'), { target: { value: 'player' } })
  fireEvent.change(screen.getByLabelText('Email'), { target: { value: 'player@example.com' } })
  fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'test passphrase for validation' } })
  fireEvent.click(screen.getByRole('button', { name: 'Create account' }))
  expect(await screen.findByRole('alert')).toHaveFocus()
  expect(screen.getByLabelText('Username')).toHaveAttribute('aria-invalid', 'false')
})

it('connects login failure to both credentials and focuses the recovery message', async () => {
  setup('/login', { status: 'anonymous', user: null, login: vi.fn().mockRejectedValue(new ApiError(401, 'Invalid email or password.')) })
  fireEvent.change(screen.getByLabelText('Email'), { target: { value: 'player@example.com' } })
  fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'test passphrase' } })
  fireEvent.click(screen.getByRole('button', { name: 'Sign in' }))
  expect(await screen.findByRole('alert')).toHaveFocus()
  expect(screen.getByLabelText('Email')).toHaveAccessibleDescription('Invalid email or password.')
  expect(screen.getByLabelText('Password')).toHaveAccessibleDescription('Invalid email or password.')
})

it('identifies invalid search criteria without moving focus away from typing', () => {
  setup('/games?q=x')
  const search = screen.getByLabelText('Search games')
  expect(search).toHaveAttribute('aria-invalid', 'true')
  expect(search).toHaveAccessibleDescription('Enter 2–100 characters. Up to 20 matches are shown.')
  search.focus(); fireEvent.change(search, { target: { value: 'Alpha' } })
  expect(search).toHaveAttribute('aria-invalid', 'false')
  expect(search).toHaveFocus()
})

it('connects invalid library URLs to their filter group and retains reset recovery', async () => {
  setup('/library?status=Invalid')
  expect(screen.getByRole('group', { name: 'Library filters' })).toHaveAccessibleDescription('Invalid library filters. Reset filters to continue.')
  fireEvent.click(screen.getByRole('button', { name: 'Reset filters' }))
  await screen.findByText('Your library is empty.')
  expect(screen.getByRole('group', { name: 'Library filters' })).not.toHaveAttribute('aria-describedby')
})
