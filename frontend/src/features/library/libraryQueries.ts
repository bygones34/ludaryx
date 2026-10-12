import type { QueryClient } from '@tanstack/react-query'

export const libraryKey = (userId: string | undefined) => ['library', userId] as const
export const libraryItemKey = (userId: string | undefined, id: string) => [...libraryKey(userId), 'details', id] as const
export const gameDetailsKey = (userId: string | undefined, igdbId: number) => ['games', 'details', userId, igdbId] as const

export function refreshLibrary(client: QueryClient, userId: string | undefined, igdbId: number) {
  return Promise.all([
    client.invalidateQueries({ queryKey: libraryKey(userId) }),
    client.invalidateQueries({ queryKey: gameDetailsKey(userId, igdbId) }),
  ])
}
