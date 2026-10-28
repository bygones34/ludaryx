export type ThemePreference = 'system' | 'light' | 'dark'
export const themeStorageKey = 'ludaryx.theme'

export function parseTheme(value: string | null): ThemePreference {
  return value === 'light' || value === 'dark' ? value : 'system'
}

export function readTheme(): ThemePreference {
  try { return parseTheme(window.localStorage.getItem(themeStorageKey)) }
  catch { return 'system' }
}

export function prefersDark(): boolean {
  try { return window.matchMedia('(prefers-color-scheme: dark)').matches }
  catch { return false }
}
