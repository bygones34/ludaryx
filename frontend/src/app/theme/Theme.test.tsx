import { StrictMode } from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import html from '../../../index.html?raw'
import App from '@/App'
import { AuthContext } from '@/features/auth/authContext'
import { AuthProvider } from '@/features/auth/AuthProvider'
import { clearAccessToken } from '@/lib/apiClient'
import type { AuthContextValue } from '@/features/auth/authContext'
import { ThemeProvider } from './ThemeProvider'
import { ThemeSelector } from './ThemeSelector'
import { themeStorageKey } from './theme'

let systemDark = false
let media: MediaQueryList
const clients: QueryClient[] = []
const startupScript = html.match(/<script id="theme-init">([\s\S]*?)<\/script>/)![1]
function boot() { new Function('window', 'document', startupScript)(window, document) }
function systemChanges(dark: boolean) {
  act(() => { systemDark = dark; media.dispatchEvent(new Event('change')) })
}
function expectTheme(dark: boolean) {
  expect(document.documentElement.classList.contains('dark')).toBe(dark)
  expect(document.documentElement.style.colorScheme).toBe(dark ? 'dark' : 'light')
}
function choose(value: string) { fireEvent.change(screen.getByLabelText('Theme'), { target: { value } }) }
function controls(strict = false) {
  const content = <ThemeProvider><ThemeSelector /></ThemeProvider>
  return render(strict ? <StrictMode>{content}</StrictMode> : content)
}
function app(path: string, status: AuthContextValue['status']) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  clients.push(client)
  return render(<QueryClientProvider client={client}><MemoryRouter initialEntries={[path]}>
    <AuthContext.Provider value={{ status, user: status === 'authenticated'
      ? { id: 'owner', username: 'player', displayName: 'Player', email: 'player@example.com', createdAt: '2026-01-01' } : null,
      login: vi.fn(), logout: vi.fn(), register: vi.fn(),
    }}><App /></AuthContext.Provider>
  </MemoryRouter></QueryClientProvider>)
}

beforeEach(() => {
  clearAccessToken()
  systemDark = false
  media = Object.assign(new EventTarget(), { media: '(prefers-color-scheme: dark)' }) as MediaQueryList
  Object.defineProperty(media, 'matches', { get: () => systemDark })
  vi.stubGlobal('matchMedia', vi.fn(() => media))
  window.localStorage.clear()
  document.documentElement.classList.remove('dark')
  document.documentElement.style.colorScheme = ''
})
afterEach(() => {
  cleanup(); clients.splice(0).forEach(client => client.clear())
  vi.restoreAllMocks(); vi.unstubAllGlobals()
  clearAccessToken()
  window.localStorage.clear()
  document.documentElement.classList.remove('dark')
  document.documentElement.style.colorScheme = ''
})

it.each([
  [null, false, false], [null, true, true], ['system', true, true],
  ['invalid', true, true], ['light', true, false], ['dark', false, true],
] as const)('applies stored %s with OS dark=%s before React and matches provider state', (stored, osDark, expected) => {
  systemDark = osDark
  if (stored !== null) window.localStorage.setItem(themeStorageKey, stored)
  boot()
  expectTheme(expected)
  controls()
  expectTheme(expected)
  expect(screen.getByLabelText('Theme')).toHaveValue(stored === 'light' || stored === 'dark' ? stored : 'system')
})

it('follows OS changes only in System and returns to the current OS preference', () => {
  controls()
  expectTheme(false)
  systemChanges(true)
  expectTheme(true)
  choose('light')
  systemChanges(false); systemChanges(true)
  expectTheme(false)
  choose('dark')
  systemChanges(false)
  expectTheme(true)
  choose('system')
  expectTheme(false)
  systemChanges(true)
  expectTheme(true)
})

it('saves only the selected enum and restores it after remount', () => {
  const { unmount } = controls()
  choose('dark')
  expect(window.localStorage.getItem(themeStorageKey)).toBe('dark')
  unmount()
  controls()
  expect(screen.getByLabelText('Theme')).toHaveValue('dark')
  expectTheme(true)
  choose('system')
  expect(window.localStorage.getItem(themeStorageKey)).toBe('system')
})

it('handles denied reads at startup and denied writes while retaining an in-memory selection', () => {
  systemDark = true
  vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => { throw new DOMException('Denied', 'SecurityError') })
  vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => { throw new DOMException('Denied', 'SecurityError') })
  expect(boot).not.toThrow()
  expectTheme(true)
  controls()
  expect(screen.getByLabelText('Theme')).toHaveValue('system')
  choose('light')
  expectTheme(false)
  expect(screen.getByLabelText('Theme')).toHaveValue('light')
})

it('starts with a readable fallback when matchMedia is unavailable', () => {
  vi.stubGlobal('matchMedia', undefined)
  expect(boot).not.toThrow()
  expectTheme(false)
  controls()
  choose('dark')
  expectTheme(true)
})

it('syncs cross-tab preferences, removal and invalid values without writing them back', () => {
  systemDark = true
  controls()
  const persist = vi.spyOn(Storage.prototype, 'setItem')
  for (const [newValue, expectedPreference, dark] of [
    ['light', 'light', false], ['dark', 'dark', true], [null, 'system', true], ['invalid', 'system', true],
  ] as const) {
    act(() => window.dispatchEvent(new StorageEvent('storage', { key: themeStorageKey, newValue, storageArea: window.localStorage })))
    expect(screen.getByLabelText('Theme')).toHaveValue(expectedPreference)
    expectTheme(dark)
  }
  choose('light')
  persist.mockClear()
  act(() => window.dispatchEvent(new StorageEvent('storage', { key: null, storageArea: window.localStorage })))
  expect(screen.getByLabelText('Theme')).toHaveValue('system')
  expectTheme(true)
  expect(persist).not.toHaveBeenCalled()
})

it('ignores unrelated keys and session-storage events', () => {
  controls()
  act(() => {
    window.dispatchEvent(new StorageEvent('storage', { key: 'other', newValue: 'dark', storageArea: window.localStorage }))
    window.dispatchEvent(new StorageEvent('storage', { key: themeStorageKey, newValue: 'dark', storageArea: window.sessionStorage }))
  })
  expect(screen.getByLabelText('Theme')).toHaveValue('system')
  expectTheme(false)
})

it('cleans up media/storage listeners under StrictMode without replacing unrelated root classes', () => {
  document.documentElement.classList.add('another-feature')
  const addMedia = vi.spyOn(media, 'addEventListener')
  const removeMedia = vi.spyOn(media, 'removeEventListener')
  const addWindow = vi.spyOn(window, 'addEventListener')
  const removeWindow = vi.spyOn(window, 'removeEventListener')
  const { unmount } = controls(true)
  choose('dark')
  expect(document.documentElement.classList.contains('another-feature')).toBe(true)
  unmount()
  expect(removeMedia.mock.calls).toHaveLength(addMedia.mock.calls.length)
  expect(removeWindow.mock.calls.filter(([event]) => event === 'storage'))
    .toHaveLength(addWindow.mock.calls.filter(([event]) => event === 'storage').length)
  document.documentElement.classList.remove('another-feature')
})

it.each([
  ['/login', 'anonymous'], ['/register', 'anonymous'], ['/games', 'authenticated'],
  ['/', 'initializing'], ['/login', 'initializing'],
] as const)('offers a labeled selector at %s while %s without making API calls', (path, status) => {
  const fetchMock = vi.fn(); vi.stubGlobal('fetch', fetchMock)
  app(path, status)
  expect(screen.getByRole('combobox', { name: 'Theme' })).toHaveValue('system')
  choose('dark')
  expectTheme(true)
  expect(fetchMock).not.toHaveBeenCalled()
})

it('changing theme preserves auth form drafts and remains selected after navigating between forms', () => {
  const fetchMock = vi.fn(); vi.stubGlobal('fetch', fetchMock)
  app('/login', 'anonymous')
  fireEvent.change(screen.getByLabelText('Email'), { target: { value: 'player@example.com' } })
  fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'Unsaved passphrase' } })
  choose('dark')
  expect(screen.getByLabelText('Email')).toHaveValue('player@example.com')
  expect(screen.getByLabelText('Password')).toHaveValue('Unsaved passphrase')
  fireEvent.click(screen.getByRole('link', { name: 'Create an account' }))
  expect(screen.getByRole('heading', { name: 'Create your account' })).toBeInTheDocument()
  expect(screen.getByLabelText('Theme')).toHaveValue('dark')
  expectTheme(true)
  expect(fetchMock).not.toHaveBeenCalled()
})

it('retains browser theme through real auth login/logout while private queries are cleared', async () => {
  const user = { id: 'owner', username: 'player', displayName: 'Player', email: 'player@example.com', createdAt: '2026-01-01' }
  const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status })
  vi.stubGlobal('fetch', vi.fn().mockImplementation((url: string) => {
    if (url.endsWith('/auth/refresh')) return Promise.resolve(json({}, 401))
    if (url.endsWith('/auth/login')) return Promise.resolve(json({ accessToken: 'theme-test-token' }))
    if (url.endsWith('/users/me')) return Promise.resolve(json(user))
    if (url.endsWith('/dashboard')) return Promise.resolve(json({
      statistics: { totalGames: 0, playing: 0, completed: 0, backlog: 0, onHold: 0, dropped: 0, averageRating: null },
      currentlyPlaying: [], upNext: [],
    }))
    if (url.endsWith('/auth/logout')) return Promise.resolve(new Response(null, { status: 204 }))
    throw new Error('Unexpected request')
  }))
  const client = new QueryClient(); clients.push(client)
  render(<QueryClientProvider client={client}><MemoryRouter initialEntries={['/login']}><AuthProvider><App /></AuthProvider></MemoryRouter></QueryClientProvider>)
  await screen.findByRole('heading', { name: 'Welcome back' })
  choose('dark')
  fireEvent.change(screen.getByLabelText('Email'), { target: { value: user.email } })
  fireEvent.change(screen.getByLabelText('Password'), { target: { value: 'A long test passphrase' } })
  fireEvent.click(screen.getByRole('button', { name: 'Sign in' }))
  await screen.findByText('Your library starts here')
  expectTheme(true)
  expect(screen.getByLabelText('Theme')).toHaveValue('dark')
  expect(client.getQueryCache().getAll().length).toBeGreaterThan(0)
  fireEvent.click(screen.getByRole('button', { name: 'Sign out' }))
  await screen.findByRole('heading', { name: 'Welcome back' })
  expect(client.getQueryCache().getAll()).toHaveLength(0)
  expect(window.localStorage.getItem(themeStorageKey)).toBe('dark')
  expect(screen.getByLabelText('Theme')).toHaveValue('dark')
  expectTheme(true)
})

it('does not refetch library data or discard an unsaved note when the theme changes', async () => {
  const id = '11111111-1111-1111-1111-111111111111'
  const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(new Response(JSON.stringify({
    id, game: { id, igdbId: 119133, name: 'Elden Ring', slug: 'elden-ring', summary: null,
      coverUrl: null, releaseDate: null, genres: [], platforms: [] },
    status: 'Playing', priority: null, backlogOrder: null, rating: 8, personalNote: 'Saved note',
    addedAt: '2026-01-01', updatedAt: '2026-01-01', startedAt: null, completedAt: null,
  }))))
  vi.stubGlobal('fetch', fetchMock)
  app(`/library/${id}`, 'authenticated')
  fireEvent.change(await screen.findByLabelText('Your personal note'), { target: { value: 'Unsaved draft' } })
  await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1))
  choose('dark'); choose('light')
  expect(screen.getByLabelText('Your personal note')).toHaveValue('Unsaved draft')
  expect(screen.getByRole('button', { name: 'Save note' })).toBeEnabled()
  expect(fetchMock).toHaveBeenCalledTimes(1)
})
