import { useId } from 'react'
import { useTheme } from './ThemeContext'
import type { ThemePreference } from './theme'

export function ThemeSelector() {
  const id = useId()
  const { preference, setPreference } = useTheme()
  return <div className="flex items-center gap-2 text-sm">
    <label htmlFor={id} className="font-medium">Theme</label>
    <select id={id} value={preference} onChange={event => setPreference(event.target.value as ThemePreference)}
      className="min-h-11 rounded-lg border border-input bg-background px-3 py-2 text-foreground outline-none focus-visible:ring-2 focus-visible:ring-ring">
      <option value="system">System</option>
      <option value="light">Light</option>
      <option value="dark">Dark</option>
    </select>
  </div>
}
