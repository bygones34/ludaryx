import { apiRequest } from '@/lib/apiClient'
import type { LibraryItem, Priority } from '@/features/library/libraryApi'

export interface BacklogItem {
  id: string
  order: number
  priority: Priority | null
  game: { igdbId: number; name: string; coverUrl: string | null }
}
export interface BacklogResponse { items: BacklogItem[] }

export const backlogKey = (userId: string | undefined) => ['backlog', userId] as const

export function getBacklog(signal: AbortSignal) {
  return apiRequest<BacklogResponse>('/backlog', { signal })
}
export function reorderBacklog(items: BacklogItem[]) {
  return apiRequest<BacklogResponse>('/backlog/order', {
    method: 'PUT', body: JSON.stringify({ items: items.map((item, index) => ({ userGameId: item.id, order: index + 1 })) }),
  })
}
export function updateBacklogPriority(id: string, priority: Priority | null) {
  return apiRequest<LibraryItem>(`/library/${id}`, { method: 'PATCH', body: JSON.stringify({ priority }) })
}
