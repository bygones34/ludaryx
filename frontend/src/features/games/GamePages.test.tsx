import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import App from '@/App'
import { AuthContext } from '@/features/auth/authContext'
import { clearAccessToken } from '@/lib/apiClient'

const game = {
  igdbId: 119133, name: 'Elden Ring', slug: 'elden-ring', summary: 'An action RPG.',
  coverUrl: 'https://images.igdb.com/elden.jpg', releaseDate: '2022-02-25',
  genres: [{ igdbId: 12, name: 'Role-playing (RPG)' }],
  platforms: [{ igdbId: 6, name: 'PC (Microsoft Windows)', abbreviation: null }],
  library: { isAdded: false, userGameId: null, status: null },
}
const searchItem = { ...game, platforms: ['PC (Microsoft Windows)'] }
const clients: QueryClient[] = []

function json(body: object, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}

function renderPage(path = '/games', anonymous = false) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  clients.push(client)
  render(<QueryClientProvider client={client}>
    <MemoryRouter initialEntries={[path]}>
      <AuthContext.Provider value={{
        status: anonymous ? 'anonymous' : 'authenticated', user: null,
        login: vi.fn(), logout: vi.fn(), register: vi.fn(),
      }}><App /></AuthContext.Provider>
    </MemoryRouter>
  </QueryClientProvider>)
}

beforeEach(() => clearAccessToken())
afterEach(() => {
  cleanup()
  clients.splice(0).forEach(client => client.clear())
  vi.useRealTimers()
  vi.unstubAllGlobals()
})

it.each(['/games', '/games/119133'])('protects the discovery route %s', async path => {
  const fetchMock = vi.fn()
  vi.stubGlobal('fetch', fetchMock)
  renderPage(path, true)
  expect(await screen.findByRole('heading', { name: 'Welcome back' })).toBeInTheDocument()
  expect(fetchMock).not.toHaveBeenCalled()
})

it('waits 350 ms after the latest input and skips short queries', async () => {
  vi.useFakeTimers()
  const fetchMock = vi.fn().mockResolvedValue(json({ items: [] }))
  vi.stubGlobal('fetch', fetchMock)
  renderPage()
  const input = screen.getByLabelText('Search games')
  fireEvent.change(input, { target: { value: 'e' } })
  await act(async () => { await vi.advanceTimersByTimeAsync(350) })
  expect(fetchMock).not.toHaveBeenCalled()
  fireEvent.change(input, { target: { value: 'el' } })
  await act(async () => { await vi.advanceTimersByTimeAsync(200) })
  fireEvent.change(input, { target: { value: '  Elden Ring  ' } })
  await act(async () => { await vi.advanceTimersByTimeAsync(349) })
  expect(fetchMock).not.toHaveBeenCalled()
  await act(async () => { await vi.advanceTimersByTimeAsync(1) })
  expect(fetchMock).toHaveBeenCalledTimes(1)
  expect(fetchMock).toHaveBeenCalledWith(expect.stringContaining('/games/search?q=Elden%20Ring'),
    expect.objectContaining({ signal: expect.any(AbortSignal) }))
})

it('renders cards, opens details, and preserves search when returning', async () => {
  vi.stubGlobal('fetch', vi.fn().mockImplementation((url: string) => Promise.resolve(
    url.includes('/games/search') ? json({ items: [searchItem] }) : json(game))))
  renderPage('/games?q=Elden%20Ring')
  expect(screen.getByText('Searching games…')).toBeInTheDocument()
  fireEvent.click(await screen.findByRole('link', { name: /Elden Ring cover/ }))
  expect(await screen.findByRole('heading', { name: 'Elden Ring', level: 1 })).toBeInTheDocument()
  expect(screen.getByText('An action RPG.')).toBeInTheDocument()
  expect(screen.getByText('Role-playing (RPG)')).toBeInTheDocument()
  expect(screen.getByText('PC (Microsoft Windows)')).toBeInTheDocument()
  expect(screen.getByText('2022-02-25')).toBeInTheDocument()
  fireEvent.click(screen.getByRole('link', { name: 'Back to search' }))
  expect(screen.getByLabelText('Search games')).toHaveValue('Elden Ring')
})

it('shows an empty state when no games match', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ items: [] })))
  renderPage('/games?q=unknown')
  expect(await screen.findByText('No games found for “unknown”. Try another name.')).toBeInTheDocument()
})

it('shows an error and recovers through an explicit retry', async () => {
  const fetchMock = vi.fn()
    .mockResolvedValueOnce(json({ title: 'Provider unavailable' }, 503))
    .mockResolvedValueOnce(json({ items: [searchItem] }))
  vi.stubGlobal('fetch', fetchMock)
  renderPage('/games?q=elden')
  expect(await screen.findByRole('alert')).toHaveTextContent('Could not load games.')
  expect(fetchMock).toHaveBeenCalledTimes(1)
  fireEvent.click(screen.getByRole('button', { name: 'Try again' }))
  expect(await screen.findByRole('heading', { name: 'Elden Ring' })).toBeInTheDocument()
  expect(screen.queryByRole('alert')).not.toBeInTheDocument()
})

it('aborts an obsolete query and does not show its late results', async () => {
  let resolveOld: (response: Response) => void = () => {}
  let oldSignal: AbortSignal | undefined
  const fetchMock = vi.fn().mockImplementation((url: string, init: RequestInit) => {
    if (url.includes('q=elden')) {
      oldSignal = init.signal as AbortSignal
      return new Promise<Response>(resolve => { resolveOld = resolve })
    }
    return Promise.resolve(json({ items: [] }))
  })
  vi.stubGlobal('fetch', fetchMock)
  renderPage('/games?q=elden')
  await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(1))
  fireEvent.change(screen.getByLabelText('Search games'), { target: { value: 'Hades' } })
  expect(oldSignal?.aborted).toBe(true)
  await act(async () => { resolveOld(json({ items: [searchItem] })) })
  expect(await screen.findByText('No games found for “Hades”. Try another name.')).toBeInTheDocument()
  expect(screen.queryByRole('heading', { name: 'Elden Ring' })).not.toBeInTheDocument()
})

it('supports missing metadata and broken cover images', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({
    ...game, summary: null, releaseDate: null, genres: [], platforms: [],
  })))
  renderPage('/games/119133')
  expect(screen.getByText('Loading game details…')).toBeInTheDocument()
  const image = await screen.findByRole('img', { name: 'Elden Ring cover' })
  fireEvent.error(image)
  expect(screen.getByText('Cover unavailable')).toBeInTheDocument()
  expect(screen.getByText('No summary available.')).toBeInTheDocument()
  expect(screen.getByText('Release date unavailable')).toBeInTheDocument()
  expect(screen.getByText('Genres unavailable')).toBeInTheDocument()
  expect(screen.getByText('Platforms unavailable')).toBeInTheDocument()
})

it('shows a missing game message on 404', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ title: 'Game was not found.' }, 404)))
  renderPage('/games/999999')
  expect(await screen.findByRole('alert')).toHaveTextContent('This game could not be found.')
})

it('rejects an invalid ID without a request', () => {
  const fetchMock = vi.fn()
  vi.stubGlobal('fetch', fetchMock)
  renderPage('/games/not-an-id')
  expect(screen.getByRole('alert')).toHaveTextContent('Invalid game ID.')
  expect(fetchMock).not.toHaveBeenCalled()
})

it.each(['a'.repeat(101), 'el\n den'])('rejects invalid search URLs without requests', async query => {
  vi.useFakeTimers()
  const fetchMock = vi.fn()
  vi.stubGlobal('fetch', fetchMock)
  renderPage(`/games?q=${encodeURIComponent(query)}`)
  await act(async () => { await vi.advanceTimersByTimeAsync(350) })
  expect(fetchMock).not.toHaveBeenCalled()
  expect(screen.getByText('Enter a game name with 2–100 characters to start exploring.')).toBeInTheDocument()
})
