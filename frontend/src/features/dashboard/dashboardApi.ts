import { apiRequest } from '@/lib/apiClient'
import type { Priority } from '@/features/library/libraryApi'

export interface DashboardGame {
  userGameId: string
  igdbId: number
  name: string
  coverUrl: string | null
}
export interface DashboardResponse {
  statistics: {
    totalGames: number
    playing: number
    completed: number
    backlog: number
    onHold: number
    dropped: number
    averageRating: number | null
  }
  currentlyPlaying: (DashboardGame & { rating: number | null })[]
  upNext: (DashboardGame & { priority: Priority | null; order: number })[]
}

export const dashboardKey = (userId: string | undefined) => ['dashboard', userId] as const

export function getDashboard(signal: AbortSignal) {
  return apiRequest<DashboardResponse>('/dashboard', { signal })
}
