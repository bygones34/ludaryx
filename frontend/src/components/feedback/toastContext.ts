import { createContext, useContext } from 'react'

// Fixed messages keep account details and personal note text out of notifications.
export type SuccessMessage = 'Game added to your library.' | 'Game removed from your library.'
  | 'Library status updated.' | 'Rating updated.' | 'Note updated.' | 'Backlog updated.' | 'Priority updated.'
export const ToastContext = createContext<(message: SuccessMessage) => void>(() => {})
export function useNotify() { return useContext(ToastContext) }
