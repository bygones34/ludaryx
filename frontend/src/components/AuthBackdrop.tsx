import { Component, lazy, Suspense } from 'react'
import type { ReactNode } from 'react'
import { useTheme } from '@/app/theme/ThemeContext'
import { useMediaQuery } from '@/hooks/useMediaQuery'

const Aurora = lazy(() => import('./Aurora'))

class DecorativeBoundary extends Component<{ children: ReactNode }, { failed: boolean }> {
  state = { failed: false }
  static getDerivedStateFromError() { return { failed: true } }
  render() { return this.state.failed ? null : this.props.children }
}

export function AuthBackdrop() {
  const { resolved } = useTheme()
  const animate = useMediaQuery('(min-width: 768px) and (hover: hover) and (pointer: fine) and (prefers-reduced-motion: no-preference)')
  return <div aria-hidden="true" className="auth-backdrop pointer-events-none absolute inset-0 overflow-hidden">
    {animate && typeof IntersectionObserver === 'function' && <DecorativeBoundary><Suspense fallback={null}>
      <Aurora colorStops={['#7c3aed', '#60a5fa', '#a78bfa']} speed={0.3} amplitude={0.7}
        layers={1} dpr={1} opacity={0.65} intro={false} mouseInteraction={false} lightMode={resolved === 'light'} />
    </Suspense></DecorativeBoundary>}
  </div>
}
