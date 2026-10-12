import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, cleanup, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import App from '@/App'
import { AuthContext } from '@/features/auth/authContext'
import { clearAccessToken } from '@/lib/apiClient'
import type { LibraryItem, LibraryPage } from './libraryApi'
import { gameDetailsKey, libraryItemKey, libraryKey } from './libraryQueries'

const itemId = '11111111-1111-1111-1111-111111111111'
const metadata = {
  igdbId: 119133, name: 'Elden Ring', slug: 'elden-ring', summary: 'An action RPG.', coverUrl: null,
  releaseDate: '2022-02-25', genres: [{ igdbId: 12, name: 'RPG' }], platforms: [{ igdbId: 6, name: 'PC', abbreviation: null }],
}
const item: LibraryItem = {
  id: itemId, game: { id: '22222222-2222-2222-2222-222222222222', ...metadata }, status: 'Backlog', priority: 'High',
  backlogOrder: 1, rating: 9, personalNote: 'Keep this note.', addedAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z', startedAt: null, completedAt: null,
}
function page(items = [item], overrides: Partial<LibraryPage> = {}): LibraryPage {
  return { items, page: 1, pageSize: 20, totalCount: items.length, totalPages: items.length ? 1 : 0,
    availablePlatforms: [{ igdbId: 6, name: 'PC' }], ...overrides }
}
function json(body: object, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}
const clients: QueryClient[] = []
const originalDialogs = {
  showModal: Object.getOwnPropertyDescriptor(HTMLDialogElement.prototype, 'showModal'),
  close: Object.getOwnPropertyDescriptor(HTMLDialogElement.prototype, 'close'),
}
function renderPage(path: string, options: { anonymous?: boolean; client?: QueryClient } = {}) {
  const client = options.client ?? new QueryClient({ defaultOptions: { queries: { retry: false } } })
  clients.push(client)
  render(<QueryClientProvider client={client}><MemoryRouter initialEntries={[path]}>
    <AuthContext.Provider value={{ status: options.anonymous ? 'anonymous' : 'authenticated',
      user: { id: 'owner', username: 'player', displayName: 'Player', email: 'player@example.com', createdAt: '2026-01-01T00:00:00Z' },
      login: vi.fn(), logout: vi.fn(), register: vi.fn(),
    }}><App /></AuthContext.Provider>
  </MemoryRouter></QueryClientProvider>)
  return client
}
beforeEach(() => {
  clearAccessToken()
  Object.defineProperty(HTMLDialogElement.prototype, 'showModal', { configurable: true, value: function (this: HTMLDialogElement) {
    this.setAttribute('open', ''); this.style.display = 'block'
  } })
  Object.defineProperty(HTMLDialogElement.prototype, 'close', { configurable: true, value: function (this: HTMLDialogElement) {
    this.removeAttribute('open'); this.style.display = 'none'
  } })
})
afterEach(() => {
  cleanup(); clients.splice(0).forEach(client => client.clear())
  vi.useRealTimers(); vi.restoreAllMocks(); vi.unstubAllGlobals()
  for (const [method, descriptor] of Object.entries(originalDialogs)) {
    if (descriptor) Object.defineProperty(HTMLDialogElement.prototype, method, descriptor)
    else Reflect.deleteProperty(HTMLDialogElement.prototype, method)
  }
})

it.each(['/library', `/library/${itemId}`])('protects %s', async path => {
  const fetchMock = vi.fn()
  vi.stubGlobal('fetch', fetchMock)
  renderPage(path, { anonymous: true })
  expect(await screen.findByRole('heading', { name: 'Welcome back' })).toBeInTheDocument()
  expect(fetchMock).not.toHaveBeenCalled()
})

it('adds a game with selected status and updates membership and library caches', async () => {
  let added = false
  const fetchMock = vi.fn().mockImplementation((_url: string, init: RequestInit) => {
    if (init?.method === 'POST') { added = true; return Promise.resolve(json({ ...item, status: 'Playing', priority: null }, 201)) }
    return Promise.resolve(json({ ...metadata, library: added
      ? { isAdded: true, userGameId: itemId, status: 'Playing' } : { isAdded: false, userGameId: null, status: null } }))
  })
  vi.stubGlobal('fetch', fetchMock)
  const client = new QueryClient()
  client.setQueryData([...libraryKey('owner'), 'list', 'cached'], page([]))
  renderPage('/games/119133', { client })
  fireEvent.change(await screen.findByLabelText('Status'), { target: { value: 'Playing' } })
  expect(screen.queryByLabelText('Backlog priority')).not.toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Add to Library' }))
  expect(await screen.findByText('In your library · Playing')).toBeInTheDocument()
  expect(screen.getByRole('link', { name: 'Open library item' })).toHaveAttribute('href', `/library/${itemId}`)
  const post = fetchMock.mock.calls.find(([, init]) => init?.method === 'POST')!
  expect(JSON.parse(post[1].body)).toEqual({ igdbId: 119133, status: 'Playing', priority: null })
  await waitFor(() => expect(client.getQueryState([...libraryKey('owner'), 'list', 'cached'])?.isInvalidated).toBe(true))
  expect(screen.queryByRole('button', { name: 'Add to Library' })).not.toBeInTheDocument()
})

it('handles a duplicate from another session by refreshing membership', async () => {
  let duplicate = false
  vi.stubGlobal('fetch', vi.fn().mockImplementation((_url: string, init: RequestInit) => {
    if (init?.method === 'POST') { duplicate = true; return Promise.resolve(json({ title: 'Already added' }, 409)) }
    return Promise.resolve(json({ ...metadata, library: duplicate
      ? { isAdded: true, userGameId: itemId, status: 'Backlog' } : { isAdded: false, userGameId: null, status: null } }))
  }))
  renderPage('/games/119133')
  fireEvent.click(await screen.findByRole('button', { name: 'Add to Library' }))
  expect(await screen.findByText('In your library · Backlog')).toBeInTheDocument()
})

it('shows an addition failure and allows retry without losing selections', async () => {
  const fetchMock = vi.fn().mockImplementation((_url: string, init: RequestInit) => Promise.resolve(init?.method === 'POST'
    ? json({ title: 'Unavailable' }, 503) : json({ ...metadata, library: { isAdded: false, userGameId: null, status: null } })))
  vi.stubGlobal('fetch', fetchMock)
  renderPage('/games/119133')
  fireEvent.change(await screen.findByLabelText('Backlog priority'), { target: { value: 'High' } })
  fireEvent.click(screen.getByRole('button', { name: 'Add to Library' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Could not add')
  expect(screen.getByLabelText('Backlog priority')).toHaveValue('High')
  fireEvent.click(screen.getByRole('button', { name: 'Add to Library' }))
  await waitFor(() => expect(fetchMock.mock.calls.filter(([, init]) => init?.method === 'POST')).toHaveLength(2))
})

it('renders named platform options and sends filters, sort, and page size', async () => {
  const fetchMock = vi.fn().mockResolvedValue(json(page()))
  vi.stubGlobal('fetch', fetchMock)
  renderPage('/library')
  expect(await screen.findByRole('heading', { name: 'Elden Ring' })).toBeInTheDocument()
  fireEvent.change(screen.getByLabelText('Status filter'), { target: { value: 'Backlog' } })
  fireEvent.change(screen.getByLabelText('Platform filter'), { target: { value: '6' } })
  fireEvent.change(screen.getByLabelText('Rating filter'), { target: { value: '9' } })
  fireEvent.change(screen.getByLabelText('Priority filter'), { target: { value: 'High' } })
  fireEvent.change(screen.getByLabelText('Sort by'), { target: { value: 'ratingDesc' } })
  fireEvent.change(screen.getByLabelText('Games per page'), { target: { value: '50' } })
  await waitFor(() => {
    const last = new URL(fetchMock.mock.calls.at(-1)![0])
    expect(Object.fromEntries(last.searchParams)).toMatchObject({ status: 'Backlog', platform: '6', rating: '9', priority: 'High', sort: 'ratingDesc', pageSize: '50', page: '1' })
  })
})

it('debounces library search and resets the page after a filter change', async () => {
  const fetchMock = vi.fn().mockResolvedValue(json(page([], { page: 2, totalCount: 40, totalPages: 2 })))
  vi.stubGlobal('fetch', fetchMock)
  renderPage('/library?page=2')
  await screen.findByText('40 games · Page 2 of 2')
  vi.useFakeTimers()
  const before = fetchMock.mock.calls.length
  fireEvent.change(screen.getByLabelText('Search your library'), { target: { value: '  Elden  ' } })
  await act(async () => { await vi.advanceTimersByTimeAsync(349) })
  expect(fetchMock).toHaveBeenCalledTimes(before)
  await act(async () => { await vi.advanceTimersByTimeAsync(1) })
  const request = new URL(fetchMock.mock.calls.at(-1)![0])
  expect(request.searchParams.get('search')).toBe('Elden')
  expect(request.searchParams.get('page')).toBe('1')
})

it('changes pages and preserves filters when opening and returning from an item', async () => {
  const fetchMock = vi.fn().mockImplementation((url: string) => {
    const request = new URL(url)
    return Promise.resolve(request.pathname.endsWith(itemId) ? json(item)
      : json(page([item], { page: Number(request.searchParams.get('page')), totalCount: 40, totalPages: 2 })))
  })
  vi.stubGlobal('fetch', fetchMock)
  renderPage('/library?status=Backlog')
  await screen.findByText('40 games · Page 1 of 2')
  fireEvent.click(screen.getByRole('button', { name: 'Next page' }))
  await screen.findByText('40 games · Page 2 of 2')
  fireEvent.click(screen.getByRole('link', { name: /Elden Ring/ }))
  await screen.findByRole('heading', { name: 'Elden Ring', level: 1 })
  fireEvent.click(screen.getByRole('link', { name: 'Back to library' }))
  expect(screen.getByLabelText('Status filter')).toHaveValue('Backlog')
  expect(await screen.findByText('40 games · Page 2 of 2')).toBeInTheDocument()
})

it('moves to the final valid page after the library shrinks', async () => {
  const fetchMock = vi.fn().mockImplementation((url: string) => {
    const current = Number(new URL(url).searchParams.get('page'))
    return Promise.resolve(json(page(current === 2 ? [] : [item], { page: current, totalCount: 20, totalPages: 1 })))
  })
  vi.stubGlobal('fetch', fetchMock)
  renderPage('/library?page=2')
  expect(await screen.findByText('20 games · Page 1 of 1')).toBeInTheDocument()
})

it('supports empty results and recovers from a list failure through retry', async () => {
  const fetchMock = vi.fn().mockResolvedValueOnce(json({ title: 'Unavailable' }, 503)).mockResolvedValue(json(page([])))
  vi.stubGlobal('fetch', fetchMock)
  renderPage('/library')
  expect(await screen.findByRole('alert')).toHaveTextContent('Could not load your library')
  fireEvent.click(screen.getByRole('button', { name: 'Try again' }))
  expect(await screen.findByText('No games match your library filters.')).toBeInTheDocument()
})

it('rejects invalid URL filters and recovers through reset', async () => {
  const fetchMock = vi.fn().mockResolvedValue(json(page()))
  vi.stubGlobal('fetch', fetchMock)
  renderPage('/library?page=0')
  expect(screen.getByRole('alert')).toHaveTextContent('Invalid library filters')
  expect(fetchMock).not.toHaveBeenCalled()
  fireEvent.click(screen.getByRole('button', { name: 'Reset filters' }))
  expect(await screen.findByRole('heading', { name: 'Elden Ring' })).toBeInTheDocument()
})

it('does not show another owners cached items or platform options', async () => {
  const client = new QueryClient()
  client.setQueryData([...libraryKey('other'), 'list', 'cached'], page([{ ...item, game: { ...item.game, name: 'Private game' } }], { availablePlatforms: [{ igdbId: 999, name: 'Private platform' }] }))
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json(page())))
  renderPage('/library', { client })
  await screen.findByRole('heading', { name: 'Elden Ring' })
  expect(screen.queryByText('Private game')).not.toBeInTheDocument()
  expect(screen.queryByRole('option', { name: 'Private platform' })).not.toBeInTheDocument()
})

it('saves only status and priority and refreshes item, list, and membership', async () => {
  let saved = item
  const fetchMock = vi.fn().mockImplementation((_url: string, init: RequestInit) => {
    if (init?.method === 'PATCH') {
      saved = { ...item, status: 'Playing', priority: null, backlogOrder: null, updatedAt: '2026-10-12T00:00:00Z' }
    }
    return Promise.resolve(json(saved))
  })
  vi.stubGlobal('fetch', fetchMock)
  const client = new QueryClient()
  client.setQueryData([...libraryKey('owner'), 'list', 'cached'], page())
  client.setQueryData(gameDetailsKey('owner', 119133), { ...metadata, library: { isAdded: true, userGameId: itemId, status: 'Backlog' } })
  renderPage(`/library/${itemId}`, { client })
  fireEvent.change(await screen.findByLabelText('Status'), { target: { value: 'Playing' } })
  fireEvent.click(screen.getByRole('button', { name: 'Save changes' }))
  expect(await screen.findByText('Changes saved.')).toBeInTheDocument()
  const update = fetchMock.mock.calls.find(([, init]) => init?.method === 'PATCH')!
  expect(JSON.parse(update[1].body)).toEqual({ status: 'Playing', priority: null })
  expect(screen.getByText('Keep this note.')).toBeInTheDocument()
  expect(client.getQueryData<{ library: { status: string } }>(gameDetailsKey('owner', 119133))?.library.status).toBe('Playing')
  expect(client.getQueryState([...libraryKey('owner'), 'list', 'cached'])?.isInvalidated).toBe(true)
})

it('keeps the editable form available after a failed update', async () => {
  vi.stubGlobal('fetch', vi.fn().mockImplementation((_url: string, init: RequestInit) => Promise.resolve(
    init?.method === 'PATCH' ? json({ title: 'Unavailable' }, 503) : json(item))))
  renderPage(`/library/${itemId}`)
  fireEvent.click(await screen.findByRole('button', { name: 'Save changes' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Could not save')
  expect(screen.getByLabelText('Status')).toHaveValue('Backlog')
})

it('requires removal confirmation and cancel does not send DELETE', async () => {
  const fetchMock = vi.fn().mockResolvedValue(json(item))
  vi.stubGlobal('fetch', fetchMock)
  renderPage(`/library/${itemId}`)
  fireEvent.click(await screen.findByRole('button', { name: 'Remove from library' }))
  const dialog = screen.getByRole('dialog', { name: 'Remove Elden Ring?' })
  fireEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }))
  expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  expect(fetchMock.mock.calls.some(([, init]) => init?.method === 'DELETE')).toBe(false)
})

it('recovers from a removal failure and clears cached membership after confirmed deletion', async () => {
  let attempts = 0
  const fetchMock = vi.fn().mockImplementation((url: string, init: RequestInit) => {
    if (init?.method === 'DELETE') {
      attempts++
      return Promise.resolve(attempts === 1 ? json({ title: 'Unavailable' }, 503) : new Response(null, { status: 204 }))
    }
    return Promise.resolve(json(url.endsWith(itemId) ? item : page([])))
  })
  vi.stubGlobal('fetch', fetchMock)
  const client = new QueryClient()
  client.setQueryData(gameDetailsKey('owner', 119133), { ...metadata, library: { isAdded: true, userGameId: itemId, status: 'Backlog' } })
  renderPage(`/library/${itemId}`, { client })
  fireEvent.click(await screen.findByRole('button', { name: 'Remove from library' }))
  fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Confirm removal' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Could not remove')
  fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Confirm removal' }))
  expect(await screen.findByText('Elden Ring was removed from your library.')).toBeInTheDocument()
  expect(await screen.findByText('No games match your library filters.')).toBeInTheDocument()
  expect(client.getQueryData(libraryItemKey('owner', itemId))).toBeUndefined()
  expect(client.getQueryData<{ library: { isAdded: boolean } }>(gameDetailsKey('owner', 119133))?.library.isAdded).toBe(false)
})

it('shows a missing owned item message on 404', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(json({ title: 'Not found' }, 404)))
  renderPage(`/library/${itemId}`)
  expect(await screen.findByRole('alert')).toHaveTextContent('This item could not be found in your library.')
})
