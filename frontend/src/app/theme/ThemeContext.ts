import { createContext, useContext } from 'react'
import type { ThemePreference } from './theme'

export const ThemeContext = createContext<{
  preference: ThemePreference
  resolved: 'light' | 'dark'
  setPreference: (value: ThemePreference) => void
} | null>(null)

export function useTheme() {
  const theme = useContext(ThemeContext)
  if (!theme) throw new Error('Theme controls require ThemeProvider.')
  return theme
}
