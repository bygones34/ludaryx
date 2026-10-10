import { apiRequest } from '@/lib/apiClient'

export interface GameSearchItem {
  igdbId: number
  name: string
  coverUrl: string | null
  releaseDate: string | null
  platforms: string[]
}

export interface GameDetails {
  igdbId: number
  name: string
  slug: string
  summary: string | null
  coverUrl: string | null
  releaseDate: string | null
  genres: { igdbId: number; name: string }[]
  platforms: { igdbId: number; name: string; abbreviation: string | null }[]
  library: { isAdded: boolean; userGameId: string | null; status: string | null }
}

export function searchGames(query: string, signal: AbortSignal) {
  return apiRequest<{ items: GameSearchItem[] }>(`/games/search?q=${encodeURIComponent(query)}`, { signal })
}
export function getGameDetails(id: number, signal: AbortSignal) {
  return apiRequest<GameDetails>(`/games/igdb/${id}`, { signal })
}
