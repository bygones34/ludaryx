import { useEffect, useState } from 'react'

export function useMediaQuery(query: string) {
  const [matches, setMatches] = useState(() => {
    try { return window.matchMedia(query).matches }
    catch { return false }
  })
  useEffect(() => {
    let media: MediaQueryList
    try { media = window.matchMedia(query) }
    catch { return }
    const update = () => setMatches(media.matches)
    update()
    media.addEventListener('change', update)
    return () => media.removeEventListener('change', update)
  }, [query])
  return matches
}
