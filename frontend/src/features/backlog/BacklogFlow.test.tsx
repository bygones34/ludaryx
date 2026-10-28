import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import App from '@/App'
import { AuthContext } from '@/features/auth/authContext'
import { clearAccessToken } from '@/lib/apiClient'
import { dashboardKey } from '@/features/dashboard/dashboardApi'
import { libraryItemKey, libraryKey } from '@/features/library/libraryQueries'
import { backlogKey } from './backlogApi'
import type { BacklogItem } from './backlogApi'

const ids = ['11111111-1111-1111-1111-111111111111', '22222222-2222-2222-2222-222222222222', '33333333-3333-3333-3333-333333333333']
const initial: BacklogItem[] = ['Alpha', 'Beta', 'Gamma'].map((name, index) => ({
  id: ids[index], order: [1, 3, 7][index], priority: index === 0 ? 'High' : null,
  game: { igdbId: index + 101, name, coverUrl: null },
}))
const clients: QueryClient[] = []
function json(body: unknown, status = 200) { return new Response(JSON.stringify(body), { status }) }
function tree(client: QueryClient, owner: string, path: string, anonymous = false) {
  return <QueryClientProvider client={client}><MemoryRouter initialEntries={[path]}>
    <AuthContext.Provider value={{ status: anonymous ? 'anonymous' : 'authenticated',
      user: { id: owner, username: owner, displayName: owner, email: `${owner}@example.com`, createdAt: '2026-01-01' },
      login: vi.fn(), register: vi.fn(), logout: vi.fn(),
    }}><App /></AuthContext.Provider>
  </MemoryRouter></QueryClientProvider>
}
function renderPage(options: { client?: QueryClient; owner?: string; anonymous?: boolean } = {}) {
  const client = options.client ?? new QueryClient({ defaultOptions: { queries: { retry: false } } })
  clients.push(client)
  client.setQueryData(dashboardKey(options.owner ?? 'owner'), { cached: true })
  return { client, ...render(tree(client, options.owner ?? 'owner', '/backlog', options.anonymous)) }
}
function names() { return within(screen.getByRole('list', { name: 'Backlog games' })).getAllByRole('link').map(link => link.textContent) }
function detail(item: BacklogItem, priority = item.priority, status = 'Backlog') {
  return { ...item, game: { ...item.game, id: '44444444-4444-4444-4444-444444444444', slug: 'alpha', summary: 'Stored game.',
    releaseDate: null, genres: [], platforms: [] }, status, priority, backlogOrder: status === 'Backlog' ? item.order : null,
    rating: 9, personalNote: 'Keep this note.', addedAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-02T00:00:00Z', startedAt: null, completedAt: null }
}
beforeEach(() => clearAccessToken())
afterEach(() => {
  cleanup(); clients.splice(0).forEach(client => client.clear()); vi.restoreAllMocks(); vi.unstubAllGlobals()
})

it('protects the backlog route without loading private data', async () => {
  const fetchMock = vi.fn(); vi.stubGlobal('fetch', fetchMock)
  renderPage({ anonymous: true })
  expect(await screen.findByRole('heading', { name: 'Welcome back' })).toBeInTheDocument()
  expect(fetchMock).not.toHaveBeenCalled()
})

it('renders server order, priorities, accessible controls, and scoped cache', async () => {
  const fetchMock = vi.fn().mockResolvedValue(json({ items: initial })); vi.stubGlobal('fetch', fetchMock)
  const { client } = renderPage()
  expect(screen.getByText('Loading your backlog…')).toBeInTheDocument()
  await screen.findByRole('link', { name: 'Alpha' })
  expect(names()).toEqual(['Alpha', 'Beta', 'Gamma'])
  expect(screen.getByLabelText('Priority for Alpha')).toHaveValue('High')
  expect(screen.getByLabelText('Priority for Beta')).toHaveValue('')
  expect(screen.getByRole('button', { name: 'Move Alpha up' })).toBeDisabled()
  expect(screen.getByRole('button', { name: 'Move Gamma down' })).toBeDisabled()
  expect(screen.getByRole('button', { name: 'Reorder Beta' })).toBeEnabled()
  expect(client.getQueryData(backlogKey('owner'))).toEqual({ items: initial })
  expect(fetchMock).toHaveBeenCalledWith(expect.stringContaining('/backlog'), expect.objectContaining({ signal: expect.any(AbortSignal) }))
})

it('shows an empty backlog and recovers from a failed GET', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(json({ title: 'Unavailable' }, 503)).mockResolvedValueOnce(json({ items: [] }))
  vi.stubGlobal('fetch', fetchMock); renderPage()
  expect(await screen.findByRole('alert')).toHaveTextContent('Could not load your backlog')
  fireEvent.click(screen.getByRole('button', { name: 'Try again' }))
  expect(await screen.findByText('Your backlog is empty.')).toBeInTheDocument()
  expect(screen.queryByRole('button', { name: /Reorder / })).not.toBeInTheDocument()
})

it('sends the full contiguous order and uses the server reply to refresh list/detail caches', async () => {
  let sent: unknown
  const saved = [initial[2], initial[0], initial[1]].map((item, index) => ({ ...item, order: index + 1 }))
  vi.stubGlobal('fetch', vi.fn().mockImplementation((_url: string, init: RequestInit) => {
    if (init.method === 'PUT') { sent = JSON.parse(init.body as string); return Promise.resolve(json({ items: saved })) }
    return Promise.resolve(json({ items: initial }))
  }))
  const client = new QueryClient()
  client.setQueryData([...libraryKey('owner'), 'list', 'cached'], { items: [] })
  client.setQueryData(libraryItemKey('owner', ids[0]), detail(initial[0]))
  renderPage({ client })
  await screen.findByRole('link', { name: 'Alpha' })
  fireEvent.click(screen.getByRole('button', { name: 'Move Alpha down' }))
  expect(await screen.findByText('Backlog order saved.')).toBeInTheDocument()
  expect(sent).toEqual({ items: [{ userGameId: ids[1], order: 1 }, { userGameId: ids[0], order: 2 }, { userGameId: ids[2], order: 3 }] })
  expect(names()).toEqual(['Gamma', 'Alpha', 'Beta'])
  expect(client.getQueryState(libraryItemKey('owner', ids[0]))?.isInvalidated).toBe(true)
  expect(client.getQueryState([...libraryKey('owner'), 'list', 'cached'])?.isInvalidated).toBe(true)
  expect(client.getQueryState(dashboardKey('owner'))?.isInvalidated).toBe(true)
})

it('disables writes during a save and prevents duplicate submissions', async () => {
  let finish: (response: Response) => void = () => {}
  const fetchMock = vi.fn().mockImplementation((_url: string, init: RequestInit) => init.method === 'PUT'
    ? new Promise<Response>(resolve => { finish = resolve }) : Promise.resolve(json({ items: initial })))
  vi.stubGlobal('fetch', fetchMock); renderPage()
  await screen.findByRole('link', { name: 'Alpha' })
  fireEvent.click(screen.getByRole('button', { name: 'Move Alpha down' }))
  expect(names()).toEqual(['Beta', 'Alpha', 'Gamma'])
  await waitFor(() => expect(fetchMock.mock.calls.filter(([, init]) => init.method === 'PUT')).toHaveLength(1))
  expect(screen.getByLabelText('Priority for Alpha')).toBeDisabled()
  expect(screen.getByRole('button', { name: 'Move Alpha down' })).toBeDisabled()
  fireEvent.click(screen.getByRole('button', { name: 'Move Alpha down' }))
  expect(fetchMock.mock.calls.filter(([, init]) => init.method === 'PUT')).toHaveLength(1)
  await act(async () => { finish(json({ items: [initial[1], initial[0], initial[2]] })) })
  await waitFor(() => expect(screen.getByLabelText('Priority for Alpha')).toBeEnabled())
})

it('restores the displayed saved order after failure and retries the same intended full order', async () => {
  let attempts = 0
  const bodies: unknown[] = []
  vi.stubGlobal('fetch', vi.fn().mockImplementation((_url: string, init: RequestInit) => {
    if (init.method === 'PUT') {
      bodies.push(JSON.parse(init.body as string)); attempts += 1
      return Promise.resolve(attempts === 1 ? json({ title: 'Unavailable' }, 503) : json({ items: [initial[1], initial[0], initial[2]] }))
    }
    return Promise.resolve(json({ items: initial }))
  }))
  renderPage(); await screen.findByRole('link', { name: 'Alpha' })
  fireEvent.click(screen.getByRole('button', { name: 'Move Alpha down' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Could not confirm')
  expect(names()).toEqual(['Alpha', 'Beta', 'Gamma'])
  fireEvent.click(screen.getByRole('button', { name: 'Retry saving' }))
  await screen.findByText('Backlog order saved.')
  expect(bodies[0]).toEqual(bodies[1]); expect(names()).toEqual(['Beta', 'Alpha', 'Gamma'])
})

it.each([400, 404, 409])('reloads the complete list after %s without blindly retrying stale order', async status => {
  let changed = false
  const added: BacklogItem = { ...initial[2], id: '55555555-5555-5555-5555-555555555555', game: { ...initial[2].game, name: 'Delta' }, order: 8 }
  const fetchMock = vi.fn().mockImplementation((_url: string, init: RequestInit) => {
    if (init.method === 'PUT') { changed = true; return Promise.resolve(json({ title: 'Changed' }, status)) }
    return Promise.resolve(json({ items: changed ? [...initial, added] : initial }))
  })
  vi.stubGlobal('fetch', fetchMock); renderPage()
  await screen.findByRole('link', { name: 'Alpha' })
  fireEvent.click(screen.getByRole('button', { name: 'Move Alpha down' }))
  await screen.findByRole('link', { name: 'Delta' })
  await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('saved backlog was reloaded'))
  expect(screen.queryByRole('button', { name: 'Retry saving' })).not.toBeInTheDocument()
  expect(fetchMock.mock.calls.filter(([, init]) => init.method === 'PUT')).toHaveLength(1)
  expect(names()).toEqual(['Alpha', 'Beta', 'Gamma', 'Delta'])
})

it('blocks further writes when conflict recovery GET fails, until a successful manual reload', async () => {
  let gets = 0
  vi.stubGlobal('fetch', vi.fn().mockImplementation((_url: string, init: RequestInit) => {
    if (init.method === 'PUT') return Promise.resolve(json({}, 409))
    gets += 1; return Promise.resolve(gets === 2 ? json({}, 503) : json({ items: initial }))
  }))
  renderPage(); await screen.findByRole('link', { name: 'Alpha' })
  fireEvent.click(screen.getByRole('button', { name: 'Move Alpha down' }))
  await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('Could not reload'))
  expect(screen.getByRole('button', { name: 'Move Alpha down' })).toBeDisabled()
  fireEvent.click(screen.getByRole('button', { name: 'Reload backlog' }))
  await screen.findByText('Backlog reloaded.')
  expect(screen.getByRole('button', { name: 'Move Alpha down' })).toBeEnabled()
})

it.each(['Low', ''])('updates priority using only the existing PATCH priority field (%s)', async value => {
  let priority = initial[0].priority
  let payload: unknown
  vi.stubGlobal('fetch', vi.fn().mockImplementation((_url: string, init: RequestInit) => {
    if (init.method === 'PATCH') { payload = JSON.parse(init.body as string); priority = value === '' ? null : 'Low'; return Promise.resolve(json(detail(initial[0], priority))) }
    return Promise.resolve(json({ items: initial.map((item, index) => index ? item : { ...item, priority }) }))
  }))
  const { client } = renderPage()
  await screen.findByRole('link', { name: 'Alpha' })
  fireEvent.change(screen.getByLabelText('Priority for Alpha'), { target: { value } })
  await screen.findByText('Priority saved.')
  await waitFor(() => expect(screen.getByLabelText('Priority for Alpha')).toHaveValue(value))
  expect(payload).toEqual({ priority: value || null })
  expect(client.getQueryData(libraryItemKey('owner', ids[0]))).toMatchObject({ rating: 9, personalNote: 'Keep this note.' })
  expect(client.getQueryState(dashboardKey('owner'))?.isInvalidated).toBe(true)
})

it('restores priority after a failed PATCH and supports retry', async () => {
  const fetchMock = vi.fn().mockImplementation((_url: string, init: RequestInit) => Promise.resolve(
    init.method === 'PATCH' ? json({}, 503) : json({ items: initial })))
  vi.stubGlobal('fetch', fetchMock); renderPage()
  await screen.findByRole('link', { name: 'Alpha' })
  fireEvent.change(screen.getByLabelText('Priority for Alpha'), { target: { value: 'Low' } })
  await screen.findByRole('alert')
  expect(screen.getByLabelText('Priority for Alpha')).toHaveValue('High')
  expect(screen.getByLabelText('Priority for Alpha')).toHaveAccessibleDescription(/Could not confirm the change was saved/)
  expect(screen.getByRole('button', { name: 'Reorder Alpha' })).toHaveAccessibleDescription(/Drag a game's handle.*Could not confirm/)
  expect(screen.getByRole('button', { name: 'Retry saving' })).toBeEnabled()
})

it('does not expose another owners backlog cache or repopulate it after a late write', async () => {
  let finish: (response: Response) => void = () => {}
  const fetchMock = vi.fn().mockImplementation((_url: string, init: RequestInit) => init.method === 'PUT'
    ? new Promise<Response>(resolve => { finish = resolve }) : Promise.resolve(json({ items: initial })))
  vi.stubGlobal('fetch', fetchMock)
  const { client, unmount } = renderPage()
  await screen.findByRole('link', { name: 'Alpha' })
  fireEvent.click(screen.getByRole('button', { name: 'Move Alpha down' }))
  await waitFor(() => expect(fetchMock.mock.calls.filter(([, init]) => init.method === 'PUT')).toHaveLength(1))
  unmount(); client.clear()
  fetchMock.mockImplementation(() => Promise.resolve(json({ items: [] })))
  renderPage({ client, owner: 'other' })
  await screen.findByText('Your backlog is empty.')
  await act(async () => { finish(json({ items: initial })) })
  expect(client.getQueryData(backlogKey('owner'))).toBeUndefined()
  expect(client.getQueryData(dashboardKey('owner'))).toBeUndefined()
  expect(client.getQueriesData({ queryKey: libraryKey('owner') })).toEqual([])
  expect(screen.queryByRole('link', { name: 'Alpha' })).not.toBeInTheDocument()
})

it('returns from library details to backlog after leaving Backlog status', async () => {
  let status = 'Backlog'
  vi.stubGlobal('fetch', vi.fn().mockImplementation((url: string, init: RequestInit) => {
    if (init.method === 'PATCH') { status = 'Playing'; return Promise.resolve(json(detail(initial[0], null, status))) }
    return Promise.resolve(json(url.endsWith('/backlog') ? { items: status === 'Backlog' ? initial : initial.slice(1) } : detail(initial[0], status === 'Backlog' ? 'High' : null, status)))
  }))
  renderPage(); fireEvent.click(await screen.findByRole('link', { name: 'Alpha' }))
  expect(await screen.findByRole('link', { name: 'Back to backlog' })).toHaveAttribute('href', '/backlog')
  fireEvent.change(await screen.findByLabelText('Status'), { target: { value: 'Playing' } })
  fireEvent.click(screen.getByRole('button', { name: 'Save changes' }))
  await screen.findByText('Changes saved.')
  fireEvent.click(screen.getByRole('link', { name: 'Back to backlog' }))
  await screen.findByRole('link', { name: 'Beta' })
  await waitFor(() => expect(names()).toEqual(['Beta', 'Gamma']))
})
