import { useLayoutEffect, useRef } from 'react'
import { useLocation } from 'react-router'

export function useMainFocus() {
  const main = useRef<HTMLElement>(null)
  const { pathname } = useLocation()
  // Query-only navigation and data updates must leave the current control alone.
  useLayoutEffect(() => { main.current?.focus({ preventScroll: true }) }, [pathname])
  return main
}
