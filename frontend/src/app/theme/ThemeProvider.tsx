import { useEffect, useLayoutEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { ThemeContext } from './ThemeContext'
import { parseTheme, prefersDark, readTheme, themeStorageKey } from './theme'
import type { ThemePreference } from './theme'

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [preference, setPreference] = useState<ThemePreference>(readTheme)
  const [systemDark, setSystemDark] = useState(prefersDark)
  const dark = preference === 'dark' || (preference === 'system' && systemDark)

  useLayoutEffect(() => {
    document.documentElement.classList.toggle('dark', dark)
    document.documentElement.style.colorScheme = dark ? 'dark' : 'light'
  }, [dark])

  useEffect(() => {
    let media: MediaQueryList
    try { media = window.matchMedia('(prefers-color-scheme: dark)') }
    catch { return }
    const changed = () => setSystemDark(media.matches)
    changed()
    media.addEventListener('change', changed)
    return () => media.removeEventListener('change', changed)
  }, [])

  useEffect(() => {
    function changed(event: StorageEvent) {
      if (event.key !== themeStorageKey && event.key !== null) return
      if (event.storageArea !== null) {
        try { if (event.storageArea !== window.localStorage) return }
        catch { return }
      }
      setPreference(parseTheme(event.key === null ? null : event.newValue))
    }
    window.addEventListener('storage', changed)
    return () => window.removeEventListener('storage', changed)
  }, [])

  function choose(value: ThemePreference) {
    setPreference(value)
    try { window.localStorage.setItem(themeStorageKey, value) }
    catch { /* The in-memory selection still works when persistence is unavailable. */ }
  }

  return <ThemeContext.Provider value={{ preference, resolved: dark ? 'dark' : 'light', setPreference: choose }}>{children}</ThemeContext.Provider>
}
