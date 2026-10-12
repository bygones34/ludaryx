import { apiRequest } from '@/lib/apiClient'
import type { GameDetails } from '@/features/games/gamesApi'

export const gameStatuses = ['Backlog', 'Playing', 'Completed', 'OnHold', 'Dropped'] as const
export type GameStatus = typeof gameStatuses[number]
export const priorities = ['Low', 'Medium', 'High'] as const
export type Priority = typeof priorities[number]
export const librarySorts = [
  ['addedAtDesc', 'Newest added'], ['addedAtAsc', 'Oldest added'],
  ['nameAsc', 'Name: A–Z'], ['nameDesc', 'Name: Z–A'],
  ['ratingDesc', 'Highest rating'], ['ratingAsc', 'Lowest rating'],
  ['completedAtDesc', 'Recently completed'], ['completedAtAsc', 'Earliest completed'],
] as const
export const statusLabel = (status: string) => status === 'OnHold' ? 'On hold' : status

export interface LibraryGameSummary {
  id: string
  igdbId: number
  name: string
  coverUrl: string | null
}
export interface LibraryListItem {
  id: string
  game: LibraryGameSummary
  status: GameStatus
  rating: number | null
  priority: Priority | null
  addedAt: string
  completedAt: string | null
}
export interface AddedLibraryItem extends Omit<LibraryListItem, 'completedAt'> {
  backlogOrder: number | null
  personalNote: string | null
}
export interface LibraryItem extends AddedLibraryItem {
  game: LibraryGameSummary & Omit<GameDetails, 'library'>
  updatedAt: string
  startedAt: string | null
  completedAt: string | null
}
export interface LibraryPage {
  items: LibraryListItem[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
  availablePlatforms: { igdbId: number; name: string }[]
}

export function addLibraryGame(igdbId: number, status: GameStatus, priority: Priority | null) {
  return apiRequest<AddedLibraryItem>('/library', {
    method: 'POST', body: JSON.stringify({ igdbId, status, priority: status === 'Backlog' ? priority : null }),
  })
}
export function getLibrary(params: URLSearchParams, signal: AbortSignal) {
  return apiRequest<LibraryPage>(`/library?${params.toString()}`, { signal })
}
export function getLibraryItem(id: string, signal: AbortSignal) {
  return apiRequest<LibraryItem>(`/library/${id}`, { signal })
}
export function updateLibraryItem(id: string, status: GameStatus, priority: Priority | null) {
  return apiRequest<LibraryItem>(`/library/${id}`, {
    method: 'PATCH', body: JSON.stringify({ status, priority: status === 'Backlog' ? priority : null }),
  })
}
export function deleteLibraryItem(id: string) {
  return apiRequest<void>(`/library/${id}`, { method: 'DELETE' })
}
