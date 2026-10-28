import { StrictMode } from 'react'
import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { ToastProvider } from './ToastProvider'
import { useNotify } from './toastContext'
import type { SuccessMessage } from './toastContext'
import { LoadingState, EmptyState, ErrorState } from './QueryState'

let lateNotify: (message: SuccessMessage) => void
function Actions() {
  const notify = useNotify()
  return <><button onClick={() => notify('Rating updated.')}>Save</button>
    <button onClick={() => notify('Note updated.')}>Save note</button>
    <button onClick={() => { lateNotify = notify }}>Start pending save</button></>
}
function content(scope = 'owner') {
  return <StrictMode><ToastProvider key={scope}><Actions /></ToastProvider></StrictMode>
}
beforeEach(() => vi.useFakeTimers())
afterEach(() => { cleanup(); vi.useRealTimers() })
function advance(ms: number) { act(() => { vi.advanceTimersByTime(ms) }) }

it('announces once, does not take focus, and dismisses after five seconds', () => {
  render(content())
  const save = screen.getByText('Save')
  save.focus(); fireEvent.click(save)
  expect(save).toHaveFocus()
  expect(screen.getAllByText('Rating updated.')).toHaveLength(1)
  expect(screen.getByText('Rating updated.').closest('[aria-live]')).toHaveAttribute('aria-live', 'polite')
  advance(4999); expect(screen.getByText('Rating updated.')).toBeInTheDocument()
  advance(1); expect(screen.queryByText('Rating updated.')).not.toBeInTheDocument()
})

it('pauses the remaining timeout over the whole notification and while focused', () => {
  render(content())
  fireEvent.click(screen.getByText('Save'))
  advance(2000)
  const card = screen.getByText('Rating updated.').parentElement!.parentElement!
  fireEvent.mouseEnter(card); advance(8000)
  const dismiss = screen.getByLabelText('Dismiss notification')
  act(() => dismiss.focus())
  fireEvent.mouseLeave(card); advance(8000)
  expect(dismiss).toHaveFocus()
  act(() => screen.getByText('Save').focus())
  advance(2999); expect(screen.getByText('Rating updated.')).toBeInTheDocument()
  advance(1); expect(screen.queryByText('Rating updated.')).not.toBeInTheDocument()
})

it('lets the user dismiss and gives a replacement success its own five seconds', () => {
  render(content())
  fireEvent.click(screen.getByText('Save')); advance(4000)
  fireEvent.click(screen.getByText('Save note')); advance(1000)
  expect(screen.queryByText('Rating updated.')).not.toBeInTheDocument()
  expect(screen.getByText('Note updated.')).toBeInTheDocument()
  advance(3999); expect(screen.getByText('Note updated.')).toBeInTheDocument()
  fireEvent.click(screen.getByLabelText('Dismiss notification'))
  expect(screen.queryByText('Note updated.')).not.toBeInTheDocument()
  fireEvent.click(screen.getByText('Save')); advance(5000)
  expect(screen.queryByText('Rating updated.')).not.toBeInTheDocument()
})

it('preserves keyboard focus and timeout pausing when a newer success replaces the message', () => {
  render(content())
  fireEvent.click(screen.getByText('Save'))
  const dismiss = screen.getByLabelText('Dismiss notification')
  act(() => dismiss.focus())
  fireEvent.click(screen.getByText('Save note')); advance(6000)
  expect(dismiss).toHaveFocus()
  expect(screen.getByText('Note updated.')).toBeInTheDocument()
  act(() => screen.getByText('Save').focus()); advance(5000)
  expect(screen.queryByText('Note updated.')).not.toBeInTheDocument()
})

it.each(['anonymous', 'another-owner'])('clears notifications and ignores ended-session callbacks on %s', nextScope => {
  const view = render(content())
  fireEvent.click(screen.getByText('Save'))
  fireEvent.click(screen.getByText('Start pending save'))
  const endedSession = lateNotify
  view.rerender(content(nextScope))
  expect(screen.queryByText('Rating updated.')).not.toBeInTheDocument()
  act(() => endedSession('Note updated.'))
  expect(screen.queryByText('Note updated.')).not.toBeInTheDocument()
  view.rerender(content())
  act(() => endedSession('Note updated.'))
  expect(screen.queryByText('Note updated.')).not.toBeInTheDocument()
  fireEvent.click(screen.getByText('Save'))
  expect(screen.getByText('Rating updated.')).toBeInTheDocument()
})

it.each(['cards', 'details', 'rows', 'dashboard', 'compact'] as const)('has one loading announcement and no exposed skeleton controls for %s', layout => {
  const { container } = render(<LoadingState message="Loading games…" layout={layout} />)
  expect(screen.getAllByRole('status')).toHaveLength(1)
  expect(screen.getByRole('status')).toHaveTextContent('Loading games…')
  expect(screen.queryByRole('button')).not.toBeInTheDocument()
  const skeleton = container.querySelector('[aria-hidden="true"]')
  expect(Boolean(skeleton)).toBe(layout !== 'compact')
  for (const animated of container.querySelectorAll('[class*="animate-pulse"]')) {
    expect(animated).toHaveClass('motion-safe:animate-pulse')
  }
})

it('keeps error recovery inline and gives empty states an existing action', () => {
  const retry = vi.fn()
  const view = render(<ErrorState message="Could not load games." retry={retry} />)
  expect(screen.getByRole('alert')).toHaveTextContent('Could not load games.')
  fireEvent.click(screen.getByRole('button', { name: 'Try again' })); expect(retry).toHaveBeenCalledOnce()
  view.rerender(<ErrorState message="Could not load games." retry={retry} retrying />)
  expect(screen.getByRole('button')).toBeDisabled()
  view.rerender(<EmptyState title="Your backlog is empty." description="Add a backlog game."><a href="/games">Discover games</a></EmptyState>)
  expect(screen.getByRole('link', { name: 'Discover games' })).toHaveAttribute('href', '/games')
  expect(screen.queryByRole('alert')).not.toBeInTheDocument()
})
