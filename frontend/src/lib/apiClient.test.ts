import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { apiRequest, clearAccessToken } from '@/lib/apiClient'

beforeEach(() => clearAccessToken())
afterEach(() => vi.unstubAllGlobals())

it('shares one refresh attempt across simultaneous unauthorized requests', async () => {
  let refreshCount = 0
  const fetchMock = vi.fn().mockImplementation((url: string, init: RequestInit) => {
    if (url.endsWith('/auth/refresh')) {
      refreshCount += 1
      return Promise.resolve(new Response(JSON.stringify({ accessToken: 'new-token' }), { status: 200 }))
    }
    const authorization = new Headers(init.headers).get('Authorization')
    if (!authorization) return Promise.resolve(new Response('{}', { status: 401 }))
    expect(authorization).toBe('Bearer new-token')
    return Promise.resolve(new Response(JSON.stringify({ id: 'user-id' }), { status: 200 }))
  })
  vi.stubGlobal('fetch', fetchMock)

  const results = await Promise.all([
    apiRequest<{ id: string }>('/users/me'),
    apiRequest<{ id: string }>('/users/me'),
  ])

  expect(results).toEqual([{ id: 'user-id' }, { id: 'user-id' }])
  expect(refreshCount).toBe(1)
})
