import { StrictMode } from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import App from '@/App'
import { AuthContext } from '@/features/auth/authContext'
import { clearAccessToken } from '@/lib/apiClient'
import type { LibraryItem } from '@/features/library/libraryApi'
import { refreshLibrary } from '@/features/library/libraryQueries'
import { dashboardKey } from './dashboardApi'
import type { DashboardResponse } from './dashboardApi'

const id = '11111111-1111-1111-1111-111111111111'
const secondId = '22222222-2222-2222-2222-222222222222'
const empty: DashboardResponse = {
  statistics: { totalGames: 0, playing: 0, completed: 0, backlog: 0, onHold: 0, dropped: 0, averageRating: null },
  currentlyPlaying: [], upNext: [],
}
const data: DashboardResponse = {
  statistics: { totalGames: 20, playing: 7, completed: 4, backlog: 6, onHold: 2, dropped: 1, averageRating: 8.3 },
  currentlyPlaying: [
    { userGameId: id, igdbId: 119133, name: 'Elden Ring', coverUrl: 'https://example.com/cover.jpg', rating: 9 },
    { userGameId: secondId, igdbId: 1942, name: 'The Witcher', coverUrl: null, rating: null },
  ],
  upNext: [
    { userGameId: secondId, igdbId: 1942, name: 'Next first', coverUrl: null, priority: 'Low', order: 3 },
    { userGameId: id, igdbId: 119133, name: 'Next second', coverUrl: null, priority: null, order: 7 },
  ],
}
const detail: LibraryItem = {
  id, game: { id: secondId, igdbId: 119133, name: 'Elden Ring', slug: 'elden-ring', summary: null,
    coverUrl: null, releaseDate: null, genres: [], platforms: [] },
  status: 'Playing', priority: null, backlogOrder: null, rating: 8, personalNote: null,
  addedAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z', startedAt: '2026-01-01T00:00:00Z', completedAt: null,
}
const clients: QueryClient[] = []
function json(body: unknown, status = 200) { return new Response(JSON.stringify(body), { status }) }
function tree(client: QueryClient, owner = 'owner', path = '/', anonymous = false) {
  return <QueryClientProvider client={client}><MemoryRouter initialEntries={[path]}>
    <AuthContext.Provider value={{ status: anonymous ? 'anonymous' : 'authenticated',
      user: anonymous ? null : { id: owner, username: owner, displayName: owner, email: `${owner}@example.com`, createdAt: detail.addedAt },
      login: vi.fn(), register: vi.fn(), logout: vi.fn(),
    }}><App /></AuthContext.Provider>
  </MemoryRouter></QueryClientProvider>
}
function setup(options: { owner?: string; anonymous?: boolean; path?: string; strict?: boolean; client?: QueryClient } = {}) {
  const client = options.client ?? new QueryClient({ defaultOptions: { queries: { retry: false, staleTime: Infinity } } })
  clients.push(client)
  const content = tree(client, options.owner, options.path, options.anonymous)
  return { client, ...render(options.strict ? <StrictMode>{content}</StrictMode> : content) }
}
function statistic(label: string) { return screen.getByText(label, { selector: 'dt' }).nextElementSibling }
beforeEach(() => clearAccessToken())
afterEach(() => { cleanup(); clients.splice(0).forEach(client => client.clear()); vi.restoreAllMocks(); vi.unstubAllGlobals() })

it('protects the dashboard without fetching private data', async () => {
  const fetchMock = vi.fn(); vi.stubGlobal('fetch', fetchMock)
  setup({ anonymous: true })
  expect(await screen.findByRole('heading', { name: 'Welcome back' })).toBeInTheDocument()
  expect(fetchMock).not.toHaveBeenCalled()
  expect(screen.queryByLabelText('Library statistics')).not.toBeInTheDocument()
})

it('renders full statistics, server list order, stored positions and existing navigation from one request', async () => {
  const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(json(data))); vi.stubGlobal('fetch', fetchMock)
  const { client } = setup()
  expect(screen.getByRole('status')).toHaveTextContent('Loading your dashboard')
  expect(screen.queryByLabelText('Library statistics')).not.toBeInTheDocument()
  await screen.findByLabelText('Library statistics')
  for (const [label, value] of Object.entries({ 'Total games': '20', Playing: '7', Completed: '4', Backlog: '6', 'On hold': '2', Dropped: '1', 'Average rating': '8.3/10' }))
    expect(statistic(label)).toHaveTextContent(value)
  expect(within(screen.getByRole('list', { name: 'Currently playing games' })).getAllByRole('link').map(link => link.textContent)).toEqual(['Elden Ring', 'The Witcher'])
  expect(within(screen.getByRole('list', { name: 'Up next games' })).getAllByRole('link').map(link => link.textContent)).toEqual(['Next first', 'Next second'])
  expect(screen.getByText('Position 3 · Low priority')).toBeInTheDocument()
  expect(screen.getByText('Position 7 · No priority')).toBeInTheDocument()
  expect(screen.getByRole('link', { name: 'Elden Ring' })).toHaveAttribute('href', `/library/${id}`)
  expect(screen.getByRole('link', { name: 'View playing games' })).toHaveAttribute('href', '/library?status=Playing')
  expect(screen.getByRole('link', { name: 'View full backlog' })).toHaveAttribute('href', '/backlog')
  expect(screen.getByRole('link', { name: 'Dashboard' })).toHaveAttribute('aria-current', 'page')
  expect(client.getQueryData(dashboardKey('owner'))).toEqual(data)
  expect(fetchMock).toHaveBeenCalledTimes(1)
  expect(fetchMock).toHaveBeenCalledWith(expect.stringContaining('/dashboard'), expect.objectContaining({ signal: expect.any(AbortSignal) }))
  fireEvent.error(screen.getByRole('img', { name: 'Elden Ring cover' }))
  expect(screen.queryByRole('img')).not.toBeInTheDocument()
  expect(screen.getAllByText('Cover unavailable')).toHaveLength(4)
})

it.each([null, 8])('renders absent or integer mean %s with correct formatting and independent empty lists', async averageRating => {
  vi.stubGlobal('fetch', vi.fn().mockImplementation(() => Promise.resolve(json({ ...empty,
    statistics: { ...empty.statistics, totalGames: 1, completed: 1, averageRating },
  }))))
  setup()
  await screen.findByLabelText('Library statistics')
  expect(statistic('Average rating')).toHaveTextContent(averageRating === null ? 'Not rated' : '8.0/10')
  expect(screen.getByText('No games currently playing.')).toBeInTheDocument()
  expect(screen.getByText('Your backlog is empty.')).toBeInTheDocument()
  expect(screen.queryByText('Your library starts here')).not.toBeInTheDocument()
})

it('shows the empty-library invitation only after a successful empty response', async () => {
  vi.stubGlobal('fetch', vi.fn().mockImplementation(() => Promise.resolve(json(empty))))
  setup()
  expect(await screen.findByText('Your library starts here')).toBeInTheDocument()
  expect(statistic('Total games')).toHaveTextContent('0')
  expect(screen.getByRole('link', { name: 'Explore games' })).toHaveAttribute('href', '/games')
})

it.each(['playing', 'backlog'])('shows the %s preview independently when the other preview is empty', async present => {
  vi.stubGlobal('fetch', vi.fn().mockImplementation(() => Promise.resolve(json({ ...data,
    currentlyPlaying: present === 'playing' ? data.currentlyPlaying : [], upNext: present === 'backlog' ? data.upNext : [],
  }))))
  setup()
  await screen.findByLabelText('Library statistics')
  expect(Boolean(screen.queryByRole('list', { name: 'Currently playing games' }))).toBe(present === 'playing')
  expect(Boolean(screen.queryByRole('list', { name: 'Up next games' }))).toBe(present === 'backlog')
  expect(screen.getByText(present === 'playing' ? 'Your backlog is empty.' : 'No games currently playing.')).toBeInTheDocument()
})

it('keeps an initial failure distinct from an empty library and supports retry', async () => {
  const fetchMock = vi.fn().mockImplementationOnce(() => Promise.resolve(json({}, 503)))
    .mockImplementation(() => Promise.resolve(json(empty)))
  vi.stubGlobal('fetch', fetchMock); setup()
  expect(await screen.findByRole('alert')).toHaveTextContent('Could not load your dashboard')
  expect(screen.queryByLabelText('Library statistics')).not.toBeInTheDocument()
  expect(screen.queryByText('Your library starts here')).not.toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Try again' }))
  expect(await screen.findByText('Your library starts here')).toBeInTheDocument()
  expect(screen.queryByRole('alert')).not.toBeInTheDocument()
})

it('labels retained data after a failed background refresh and recovers through retry', async () => {
  let fail = false
  vi.stubGlobal('fetch', vi.fn().mockImplementation(() => Promise.resolve(fail ? json({}, 503) : json(data))))
  const { client } = setup()
  await screen.findByLabelText('Library statistics')
  fail = true
  await act(async () => { await client.invalidateQueries({ queryKey: dashboardKey('owner') }) })
  expect(await screen.findByRole('alert')).toHaveTextContent('The last saved summary is shown')
  expect(statistic('Total games')).toHaveTextContent('20')
  fail = false
  fireEvent.click(screen.getByRole('button', { name: 'Try again' }))
  await waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument())
})

it('uses a different owner key without displaying the previous owners summary', async () => {
  let finish!: (response: Response) => void
  const fetchMock = vi.fn().mockImplementationOnce(() => Promise.resolve(json(data)))
    .mockImplementationOnce(() => new Promise<Response>(resolve => { finish = resolve }))
  vi.stubGlobal('fetch', fetchMock)
  const { client, rerender } = setup()
  await screen.findByRole('link', { name: 'Elden Ring' })
  rerender(tree(client, 'other'))
  expect(screen.queryByRole('link', { name: 'Elden Ring' })).not.toBeInTheDocument()
  expect(screen.queryByLabelText('Library statistics')).not.toBeInTheDocument()
  await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2))
  await act(async () => finish(json(empty)))
  expect(await screen.findByText('Your library starts here')).toBeInTheDocument()
  expect(client.getQueryData(dashboardKey('other'))).toEqual(empty)
})

it('cancels a pending private read and ignores its late response after session cache cleanup', async () => {
  let finish!: (response: Response) => void
  let signal!: AbortSignal
  vi.stubGlobal('fetch', vi.fn().mockImplementation((_url: string, init: RequestInit) => {
    signal = init.signal as AbortSignal
    return new Promise<Response>(resolve => { finish = resolve })
  }))
  const { client, unmount } = setup()
  await waitFor(() => expect(signal).toBeDefined())
  unmount(); client.clear()
  expect(signal.aborted).toBe(true)
  await act(async () => finish(json(data)))
  expect(client.getQueryData(dashboardKey('owner'))).toBeUndefined()
})

it('passes cancellation through StrictMode remount and unmount', async () => {
  const signals: AbortSignal[] = []
  vi.stubGlobal('fetch', vi.fn().mockImplementation((_url: string, init: RequestInit) => {
    const signal = init.signal as AbortSignal
    signals.push(signal)
    return new Promise<Response>((_resolve, reject) => {
      if (signal.aborted) reject(signal.reason)
      else signal.addEventListener('abort', () => reject(signal.reason), { once: true })
    })
  }))
  const { unmount } = setup({ strict: true })
  await waitFor(() => expect(signals).toHaveLength(2))
  expect(signals[0].aborted).toBe(true)
  expect(signals[1].aborted).toBe(false)
  unmount()
  expect(signals.every(signal => signal.aborted)).toBe(true)
})

it('refreshes a previously fresh dashboard after saving a rating and navigating back', async () => {
  let saved = detail
  let reads = 0
  vi.stubGlobal('fetch', vi.fn().mockImplementation((url: string, init: RequestInit) => {
    if (url.endsWith('/dashboard')) {
      reads++
      return Promise.resolve(json({ ...data, statistics: { ...data.statistics, averageRating: saved.rating } }))
    }
    if (init.method === 'PATCH') saved = { ...saved, rating: 9, updatedAt: '2026-02-01T00:00:00Z' }
    return Promise.resolve(json(saved))
  }))
  setup()
  await screen.findByLabelText('Library statistics')
  expect(statistic('Average rating')).toHaveTextContent('8.0/10')
  fireEvent.click(screen.getByRole('link', { name: 'Elden Ring' }))
  fireEvent.change(await screen.findByLabelText('Your rating'), { target: { value: '9' } })
  fireEvent.click(screen.getByRole('button', { name: 'Save rating' }))
  await screen.findByText('Rating saved.')
  fireEvent.click(screen.getByRole('link', { name: 'Dashboard' }))
  await waitFor(() => expect(statistic('Average rating')).toHaveTextContent('9.0/10'))
  expect(reads).toBe(2)
})

it('invalidates only the affected owners existing dashboard cache and never recreates cleared entries', async () => {
  const client = new QueryClient(); clients.push(client)
  client.setQueryData(dashboardKey('owner'), data)
  client.setQueryData(dashboardKey('other'), empty)
  await refreshLibrary(client, 'owner', 119133)
  expect(client.getQueryState(dashboardKey('owner'))?.isInvalidated).toBe(true)
  expect(client.getQueryState(dashboardKey('other'))?.isInvalidated).toBe(false)
  client.clear()
  await refreshLibrary(client, 'owner', 119133)
  expect(client.getQueryCache().getAll()).toHaveLength(0)
})
