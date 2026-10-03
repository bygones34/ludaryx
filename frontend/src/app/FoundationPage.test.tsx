import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { describe, expect, it } from 'vitest'
import App from '@/App'

describe('frontend foundation', () => {
  it('shows the landing page through the application providers', () => {
    const queryClient = new QueryClient()

    render(
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={['/']}>
          <App />
        </MemoryRouter>
      </QueryClientProvider>,
    )

    expect(screen.getByRole('heading', { name: 'Your games. Your journey.' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Explore games soon' })).toBeDisabled()
  })
})
