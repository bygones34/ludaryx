import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { ThemeProvider } from '@/app/theme/ThemeProvider'
import { ThemeSelector } from '@/app/theme/ThemeSelector'
import { AccentCard } from './AccentCard'
import { AuthBackdrop } from './AuthBackdrop'

vi.mock('./Aurora', () => ({ default: ({ lightMode, dpr, layers }: { lightMode: boolean; dpr: number; layers: number }) =>
  <canvas data-testid="animated-aurora" data-light={lightMode} data-dpr={dpr} data-layers={layers} /> }))

const queries = new Map<string, MediaQueryList>()
const observe = vi.fn()
const disconnect = vi.fn()
function media(query: string) {
  if (!queries.has(query)) queries.set(query, Object.assign(new EventTarget(), { matches: false, media: query }) as MediaQueryList)
  return queries.get(query)!
}
function changeMotion(matches: boolean) {
  act(() => {
    for (const [query, value] of queries) if (query.includes('no-preference')) {
      Object.assign(value, { matches }); value.dispatchEvent(new Event('change'))
    }
  })
}
beforeEach(() => {
  queries.clear(); observe.mockClear(); disconnect.mockClear(); window.localStorage.clear()
  vi.stubGlobal('matchMedia', vi.fn(media))
  vi.stubGlobal('IntersectionObserver', class { observe = observe; disconnect = disconnect })
  vi.stubGlobal('requestAnimationFrame', vi.fn(() => 1))
  vi.stubGlobal('cancelAnimationFrame', vi.fn())
})
afterEach(() => {
  cleanup(); vi.restoreAllMocks(); vi.unstubAllGlobals(); window.localStorage.clear()
  document.documentElement.classList.remove('dark'); document.documentElement.style.colorScheme = ''
})

it('keeps spotlight content and links accessible with no motion listeners when effects are disabled', () => {
  render(<ThemeProvider><AccentCard><a href="/library/test">Open game</a></AccentCard></ThemeProvider>)
  expect(screen.getByRole('link', { name: 'Open game' })).toHaveAttribute('href', '/library/test')
  expect(screen.getByRole('link').getAttribute('tabindex')).toBeNull()
  expect(observe).not.toHaveBeenCalled()
  expect(requestAnimationFrame).not.toHaveBeenCalled()
})

it('enables and cleans up pointer effects on media changes without remounting focused content', () => {
  const { unmount } = render(<ThemeProvider><AccentCard><input aria-label="Draft" defaultValue="Draft text" /></AccentCard></ThemeProvider>)
  const draft = screen.getByLabelText('Draft')
  draft.focus()
  changeMotion(true)
  expect(observe).toHaveBeenCalledTimes(1)
  expect(requestAnimationFrame).toHaveBeenCalled()
  fireEvent.change(draft, { target: { value: 'Keep my draft' } })
  changeMotion(false)
  expect(disconnect).toHaveBeenCalledTimes(1)
  expect(cancelAnimationFrame).toHaveBeenCalled()
  expect(screen.getByLabelText('Draft')).toBe(draft)
  expect(draft).toHaveFocus()
  expect(draft).toHaveValue('Keep my draft')
  unmount()
})

it('adapts spotlight palette to the resolved theme while retaining semantic surfaces', () => {
  const { container } = render(<ThemeProvider><ThemeSelector /><AccentCard data-testid="accent">Card</AccentCard></ThemeProvider>)
  const card = screen.getByTestId('accent')
  expect(card.style.getPropertyValue('--spotlight-card-surface')).toBe('var(--card)')
  fireEvent.change(screen.getByLabelText('Theme'), { target: { value: 'dark' } })
  expect(container.querySelector('.dark')).not.toBeInTheDocument()
  expect(document.documentElement).toHaveClass('dark')
  expect(card.style.getPropertyValue('--spotlight-card-border')).toBe('var(--border)')
})

it('retains a decorative static background when animation is unavailable or not requested', () => {
  const { container } = render(<ThemeProvider><AuthBackdrop /><input aria-label="Email" /></ThemeProvider>)
  expect(container.querySelector('.auth-backdrop')).toHaveAttribute('aria-hidden', 'true')
  expect(container.querySelector('.auth-backdrop')).toHaveClass('pointer-events-none')
  expect(screen.queryByTestId('animated-aurora')).not.toBeInTheDocument()
  expect(screen.getByLabelText('Email')).toBeEnabled()
})

it('loads capped Aurora only when media allows it and removes it when preference changes', async () => {
  render(<ThemeProvider><ThemeSelector /><AuthBackdrop /></ThemeProvider>)
  changeMotion(true)
  const canvas = await screen.findByTestId('animated-aurora')
  expect(canvas).toHaveAttribute('data-dpr', '1')
  expect(canvas).toHaveAttribute('data-layers', '1')
  expect(canvas).toHaveAttribute('data-light', 'true')
  fireEvent.change(screen.getByLabelText('Theme'), { target: { value: 'dark' } })
  expect(canvas).toHaveAttribute('data-light', 'false')
  changeMotion(false)
  expect(screen.queryByTestId('animated-aurora')).not.toBeInTheDocument()
})

it('uses static fallback when observers are unavailable', () => {
  vi.stubGlobal('IntersectionObserver', undefined)
  render(<ThemeProvider><AuthBackdrop /><AccentCard>Still usable</AccentCard></ThemeProvider>)
  changeMotion(true)
  expect(screen.queryByTestId('animated-aurora')).not.toBeInTheDocument()
  expect(screen.getByText('Still usable')).toBeInTheDocument()
})

it.each(['null', 'throw'])('keeps Aurora failure harmless when WebGL context returns %s', async failure => {
  const { default: Aurora } = await vi.importActual<typeof import('./Aurora')>('./Aurora')
  vi.spyOn(HTMLCanvasElement.prototype, 'getContext').mockImplementation(() => {
    if (failure === 'throw') throw new Error('WebGL disabled')
    return null
  })
  const { container } = render(<><Aurora /><button>Sign in still works</button></>)
  expect(container.querySelector('canvas')).toBeInTheDocument()
  expect(screen.getByRole('button')).toBeEnabled()
})
