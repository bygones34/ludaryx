import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { StrictMode } from 'react'
import { act, cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, expect, it, vi } from 'vitest'
import App from '@/App'
import { AuthContext } from '@/features/auth/authContext'
import { backlogKey } from '@/features/backlog/backlogApi'
import type { LibraryItem } from './libraryApi'
import { libraryItemKey, libraryKey } from './libraryQueries'

const id = '11111111-1111-1111-1111-111111111111'
const item: LibraryItem = {
  id, game: { id: '22222222-2222-2222-2222-222222222222', igdbId: 119133, name: 'Elden Ring', slug: 'elden-ring',
    summary: null, coverUrl: null, releaseDate: null, genres: [], platforms: [] },
  status: 'Backlog', priority: 'High', backlogOrder: 1, rating: 9, personalNote: 'Keep this note.',
  addedAt: '2026-01-01T00:00:00Z', updatedAt: '2026-01-01T00:00:00Z', startedAt: null, completedAt: null,
}
function json(body: object, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}
const clients: QueryClient[] = []
function setup(fetchMock: ReturnType<typeof vi.fn>) {
  vi.stubGlobal('fetch', fetchMock)
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  clients.push(client)
  const view = render(<StrictMode><QueryClientProvider client={client}><MemoryRouter initialEntries={[`/library/${id}`]}>
    <AuthContext.Provider value={{ status: 'authenticated',
      user: { id: 'owner', username: 'player', displayName: 'Player', email: 'player@example.com', createdAt: item.addedAt },
      login: vi.fn(), logout: vi.fn(), register: vi.fn(),
    }}><App /></AuthContext.Provider>
  </MemoryRouter></QueryClientProvider></StrictMode>)
  return { client, view }
}
afterEach(() => { cleanup(); clients.splice(0).forEach(client => client.clear()); vi.restoreAllMocks(); vi.unstubAllGlobals() })

it.each([1, 10, null])('saves only rating %s, uses the server response and invalidates related lists', async rating => {
  let saved = item
  const fetchMock = vi.fn().mockImplementation((_url: string, init: RequestInit) => {
    if (init?.method === 'PATCH') saved = { ...item, rating, updatedAt: '2026-02-01T00:00:00Z' }
    return Promise.resolve(json(saved))
  })
  const { client } = setup(fetchMock)
  client.setQueryData([...libraryKey('owner'), 'list'], { items: [] })
  client.setQueryData(backlogKey('owner'), { items: [] })
  const selector = await screen.findByLabelText('Your rating')
  expect(selector).toHaveValue('9')
  expect(screen.getByRole('button', { name: 'Save rating' })).toBeDisabled()
  expect(screen.getAllByRole('option').filter(option => option.parentElement === selector)).toHaveLength(11)
  fireEvent.change(selector, { target: { value: rating === null ? '' : String(rating) } })
  fireEvent.click(screen.getByRole('button', { name: 'Save rating' }))
  expect(await screen.findByText('Rating saved.')).toBeInTheDocument()
  const patches = fetchMock.mock.calls.filter(([, init]) => init?.method === 'PATCH')
  expect(patches).toHaveLength(1)
  expect(JSON.parse(patches[0][1].body)).toEqual({ rating })
  await waitFor(() => expect(client.getQueryData(libraryItemKey('owner', id))).toEqual(saved))
  expect(saved).toMatchObject({ status: 'Backlog', priority: 'High', personalNote: item.personalNote, startedAt: null, completedAt: null })
  await waitFor(() => expect(client.getQueryState([...libraryKey('owner'), 'list'])?.isInvalidated).toBe(true))
  expect(client.getQueryState(backlogKey('owner'))?.isInvalidated).toBe(true)
  expect(screen.getByLabelText('Your rating')).toHaveValue(rating === null ? '' : String(rating))
})

it('blocks repeated saves and competing status/removal actions while a rating is pending', async () => {
  let complete!: (response: Response) => void
  const fetchMock = vi.fn().mockImplementation((_url: string, init: RequestInit) => init?.method === 'PATCH'
    ? new Promise<Response>(resolve => { complete = resolve }) : Promise.resolve(json(item)))
  setup(fetchMock)
  fireEvent.change(await screen.findByLabelText('Your rating'), { target: { value: '1' } })
  const save = screen.getByRole('button', { name: 'Save rating' })
  fireEvent.click(save); fireEvent.click(save)
  await waitFor(() => expect(fetchMock.mock.calls.filter(([, init]) => init?.method === 'PATCH')).toHaveLength(1))
  expect(screen.getByLabelText('Your rating')).toBeDisabled()
  expect(screen.getByRole('button', { name: 'Save changes' })).toBeDisabled()
  expect(screen.getByRole('button', { name: 'Remove from library' })).toBeDisabled()
  await act(async () => complete(json({ ...item, rating: 1 })))
  expect(await screen.findByText('Rating saved.')).toBeInTheDocument()
})

it('keeps a failed selection for retry without replacing saved data', async () => {
  let attempts = 0
  const fetchMock = vi.fn().mockImplementation((_url: string, init: RequestInit) => {
    if (init?.method !== 'PATCH') return Promise.resolve(json(item))
    return Promise.resolve(++attempts === 1 ? json({ title: 'Unavailable' }, 503) : json({ ...item, rating: 1 }))
  })
  const { client } = setup(fetchMock)
  fireEvent.change(await screen.findByLabelText('Your rating'), { target: { value: '1' } })
  fireEvent.click(screen.getByRole('button', { name: 'Save rating' }))
  expect(await screen.findByRole('alert')).toHaveTextContent('Your selection is kept')
  expect(screen.getByLabelText('Your rating')).toHaveValue('1')
  expect(client.getQueryData<LibraryItem>(libraryItemKey('owner', id))?.rating).toBe(9)
  fireEvent.click(screen.getByRole('button', { name: 'Save rating' }))
  expect(await screen.findByText('Rating saved.')).toBeInTheDocument()
  expect(attempts).toBe(2)
})

it('preserves an unsaved rating when a status change refreshes the detail', async () => {
  let saved = item
  const fetchMock = vi.fn().mockImplementation((_url: string, init: RequestInit) => {
    if (init?.method === 'PATCH') saved = { ...item, status: 'Playing', priority: null, backlogOrder: null, updatedAt: '2026-02-01T00:00:00Z' }
    return Promise.resolve(json(saved))
  })
  setup(fetchMock)
  fireEvent.change(await screen.findByLabelText('Your rating'), { target: { value: '1' } })
  fireEvent.change(screen.getByLabelText('Status'), { target: { value: 'Playing' } })
  fireEvent.click(screen.getByRole('button', { name: 'Save changes' }))
  await waitFor(() => expect(screen.getByText('Current status: Playing')).toBeInTheDocument())
  expect(screen.getByLabelText('Your rating')).toHaveValue('1')
  expect(JSON.parse(fetchMock.mock.calls.find(([, init]) => init?.method === 'PATCH')![1].body)).toEqual({ status: 'Playing', priority: null })
})

it('adopts refreshed saved ratings only when the selector has no draft', async () => {
  const { client } = setup(vi.fn().mockImplementation(() => Promise.resolve(json(item))))
  await screen.findByLabelText('Your rating')
  await act(async () => { client.setQueryData(libraryItemKey('owner', id), { ...item, rating: 10 }) })
  await waitFor(() => expect(screen.getByLabelText('Your rating')).toHaveValue('10'))
  fireEvent.change(screen.getByLabelText('Your rating'), { target: { value: '1' } })
  await act(async () => { client.setQueryData(libraryItemKey('owner', id), { ...item, rating: 5 }) })
  await waitFor(() => expect(client.getQueryData<LibraryItem>(libraryItemKey('owner', id))?.rating).toBe(5))
  expect(screen.getByLabelText('Your rating')).toHaveValue('1')
})

it('does not recreate private cache entries when a pending save completes after unmount and logout cleanup', async () => {
  let complete!: (response: Response) => void
  const fetchMock = vi.fn().mockImplementation((_url: string, init: RequestInit) => init?.method === 'PATCH'
    ? new Promise<Response>(resolve => { complete = resolve }) : Promise.resolve(json(item)))
  const { client, view } = setup(fetchMock)
  fireEvent.change(await screen.findByLabelText('Your rating'), { target: { value: '1' } })
  fireEvent.click(screen.getByRole('button', { name: 'Save rating' }))
  await waitFor(() => expect(complete).toBeDefined())
  view.unmount(); client.clear()
  const invalidate = vi.spyOn(client, 'invalidateQueries')
  await act(async () => complete(json({ ...item, rating: 1 })))
  await waitFor(() => expect(invalidate).toHaveBeenCalled())
  expect(client.getQueryData(libraryItemKey('owner', id))).toBeUndefined()
  expect(client.getQueryCache().getAll()).toHaveLength(0)
})

it.each(['  First line\nSecond line: café 🎮\n  ', 'x'.repeat(998) + '🎮'])('saves note text unchanged using only personalNote', async note => {
  let saved = item
  const fetchMock = vi.fn().mockImplementation((_url: string, init: RequestInit) => {
    if (init?.method === 'PATCH') saved = { ...item, personalNote: note, updatedAt: '2026-02-01T00:00:00Z' }
    return Promise.resolve(json(saved))
  })
  const { client } = setup(fetchMock)
  client.setQueryData([...libraryKey('owner'), 'list'], { items: [] })
  fireEvent.change(await screen.findByLabelText('Your personal note'), { target: { value: note } })
  expect(screen.getByText(`${note.length}/1,000 characters`)).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: 'Save note' }))
  expect(await screen.findByText('Note saved.')).toBeInTheDocument()
  const patch = fetchMock.mock.calls.find(([, init]) => init?.method === 'PATCH')!
  expect(JSON.parse(patch[1].body)).toEqual({ personalNote: note })
  await waitFor(() => expect(client.getQueryData(libraryItemKey('owner', id))).toEqual(saved))
  expect(screen.getByLabelText('Your personal note')).toHaveValue(note)
  expect(saved.rating).toBe(9)
  await waitFor(() => expect(client.getQueryState([...libraryKey('owner'), 'list'])?.isInvalidated).toBe(true))
})

it.each(['Clear note', 'Save note'])('clears a note with null through %s', async action => {
  let saved = item
  const fetchMock = vi.fn().mockImplementation((_url: string, init: RequestInit) => {
    if (init?.method === 'PATCH') saved = { ...item, personalNote: null }
    return Promise.resolve(json(saved))
  })
  setup(fetchMock)
  await screen.findByLabelText('Your personal note')
  if (action === 'Save note') fireEvent.change(screen.getByLabelText('Your personal note'), { target: { value: '' } })
  fireEvent.click(screen.getByRole('button', { name: action }))
  expect(await screen.findByText('Note saved.')).toBeInTheDocument()
  expect(JSON.parse(fetchMock.mock.calls.find(([, init]) => init?.method === 'PATCH')![1].body)).toEqual({ personalNote: null })
  expect(screen.getByLabelText('Your personal note')).toHaveValue('')
  expect(screen.getByRole('button', { name: 'Clear note' })).toBeDisabled()
})

it('keeps oversized text visible and prevents saving it without silently truncating', async () => {
  const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(json(item)))
  setup(fetchMock)
  const note = 'x'.repeat(999) + '🎮'
  fireEvent.change(await screen.findByLabelText('Your personal note'), { target: { value: note } })
  expect(screen.getByLabelText('Your personal note')).toHaveValue(note)
  expect(screen.getByLabelText('Your personal note')).toHaveAttribute('aria-invalid', 'true')
  expect(screen.getByRole('alert')).toHaveTextContent('exceeds 1,000')
  expect(screen.getByRole('button', { name: 'Save note' })).toBeDisabled()
  expect(fetchMock.mock.calls.some(([, init]) => init?.method === 'PATCH')).toBe(false)
  fireEvent.click(screen.getByRole('button', { name: 'Discard note changes' }))
  expect(screen.getByLabelText('Your personal note')).toHaveValue(item.personalNote)
  expect(screen.queryByRole('alert')).not.toBeInTheDocument()
})

it('retains failed note text for retry and blocks conflicting writes while saving', async () => {
  let complete!: (response: Response) => void
  let saved = item
  const fetchMock = vi.fn().mockImplementation((_url: string, init: RequestInit) => init?.method === 'PATCH'
    ? new Promise<Response>(resolve => { complete = resolve }) : Promise.resolve(json(saved)))
  setup(fetchMock)
  fireEvent.change(await screen.findByLabelText('Your personal note'), { target: { value: 'Draft note' } })
  fireEvent.click(screen.getByRole('button', { name: 'Save note' }))
  await waitFor(() => expect(complete).toBeDefined())
  expect(screen.getByLabelText('Your personal note')).toBeDisabled()
  expect(screen.getByLabelText('Your rating')).toBeDisabled()
  expect(screen.getByRole('button', { name: 'Save changes' })).toBeDisabled()
  expect(screen.getByRole('button', { name: 'Remove from library' })).toBeDisabled()
  await act(async () => complete(json({ title: 'Unavailable' }, 503)))
  expect(await screen.findByRole('alert')).toHaveTextContent('Your text is kept')
  expect(screen.getByLabelText('Your personal note')).toHaveValue('Draft note')
  fireEvent.click(screen.getByRole('button', { name: 'Save note' }))
  await waitFor(() => expect(fetchMock.mock.calls.filter(([, init]) => init?.method === 'PATCH')).toHaveLength(2))
  saved = { ...item, personalNote: 'Draft note' }
  await act(async () => complete(json(saved)))
  expect(await screen.findByText('Note saved.')).toBeInTheDocument()
})

it('keeps an unsaved note across rating saves and background refresh failures', async () => {
  let saved = item
  let failRead = false
  const fetchMock = vi.fn().mockImplementation((_url: string, init: RequestInit) => {
    if (init?.method === 'PATCH') saved = { ...item, rating: 1, updatedAt: '2026-02-01T00:00:00Z' }
    return Promise.resolve(init?.method !== 'PATCH' && failRead ? json({ title: 'Unavailable' }, 503) : json(saved))
  })
  const { client } = setup(fetchMock)
  fireEvent.change(await screen.findByLabelText('Your personal note'), { target: { value: 'Unsaved text' } })
  fireEvent.change(screen.getByLabelText('Your rating'), { target: { value: '1' } })
  fireEvent.click(screen.getByRole('button', { name: 'Save rating' }))
  expect(await screen.findByText('Rating saved.')).toBeInTheDocument()
  expect(screen.getByLabelText('Your personal note')).toHaveValue('Unsaved text')
  failRead = true
  await act(async () => { await client.invalidateQueries({ queryKey: libraryItemKey('owner', id) }) })
  expect(await screen.findByRole('alert')).toHaveTextContent('Could not load')
  expect(screen.getByLabelText('Your personal note')).toHaveValue('Unsaved text')
  expect(JSON.parse(fetchMock.mock.calls.find(([, init]) => init?.method === 'PATCH')![1].body)).toEqual({ rating: 1 })
})

it('keeps a note draft across status changes and displays server-managed historical dates', async () => {
  let saved = item
  const fetchMock = vi.fn().mockImplementation((_url: string, init: RequestInit) => {
    if (init?.method === 'PATCH') saved = { ...item, status: 'Completed', priority: null, backlogOrder: null,
      startedAt: '2026-02-01T12:00:00Z', completedAt: '2026-02-02T12:00:00Z', updatedAt: '2026-02-02T12:00:00Z' }
    return Promise.resolve(json(saved))
  })
  setup(fetchMock)
  fireEvent.change(await screen.findByLabelText('Your personal note'), { target: { value: 'Unsaved note' } })
  fireEvent.change(screen.getByLabelText('Status'), { target: { value: 'Completed' } })
  fireEvent.click(screen.getByRole('button', { name: 'Save changes' }))
  await waitFor(() => expect(screen.getByText('Current status: Completed')).toBeInTheDocument())
  expect(screen.getByLabelText('Your personal note')).toHaveValue('Unsaved note')
  expect(screen.getByText('Feb 1, 2026')).toBeInTheDocument()
  expect(screen.getByText('Feb 2, 2026')).toBeInTheDocument()
  expect(screen.getByText(/dates cannot be edited/)).toBeInTheDocument()
  expect(screen.queryByLabelText('Started')).not.toBeInTheDocument()
})

it('does not recreate private note data when a save finishes after unmount', async () => {
  let complete!: (response: Response) => void
  const fetchMock = vi.fn().mockImplementation((_url: string, init: RequestInit) => init?.method === 'PATCH'
    ? new Promise<Response>(resolve => { complete = resolve }) : Promise.resolve(json(item)))
  const { client, view } = setup(fetchMock)
  fireEvent.change(await screen.findByLabelText('Your personal note'), { target: { value: 'Private draft' } })
  fireEvent.click(screen.getByRole('button', { name: 'Save note' }))
  await waitFor(() => expect(complete).toBeDefined())
  view.unmount(); client.clear()
  const invalidate = vi.spyOn(client, 'invalidateQueries')
  await act(async () => complete(json({ ...item, personalNote: 'Private draft' })))
  await waitFor(() => expect(invalidate).toHaveBeenCalled())
  expect(client.getQueryCache().getAll()).toHaveLength(0)
})
