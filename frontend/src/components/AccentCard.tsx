import type { CSSProperties, HTMLAttributes } from 'react'
import { useTheme } from '@/app/theme/ThemeContext'
import { useMediaQuery } from '@/hooks/useMediaQuery'
import SpotlightCard from './SpotlightCard'

export function AccentCard({ className = '', style, ...props }: HTMLAttributes<HTMLDivElement>) {
  const { resolved } = useTheme()
  const motion = useMediaQuery('(hover: hover) and (pointer: fine) and (prefers-reduced-motion: no-preference)')
  return <SpotlightCard {...props} enabled={motion && typeof IntersectionObserver === 'function'}
    theme={resolved} spotlightColor={resolved === 'dark' ? '#a78bfa' : '#7c3aed'}
    ambient={false} flare={false} proximity={0} intensity={0.2} borderGlow={0.45} smoothing={0.15}
    className={`min-w-0 rounded-xl border text-card-foreground ${className}`}
    style={{ '--spotlight-card-surface': 'var(--card)', '--spotlight-card-border': 'var(--border)',
      '--spotlight-card-shadow': 'none', ...style } as CSSProperties} />
}
