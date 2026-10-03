// Vite environment variables are public and must never contain credentials.
export const apiBaseUrl = import.meta.env.VITE_API_BASE_URL ??
  (import.meta.env.DEV ? 'https://localhost:7034/api/v1' : '/api/v1')

let accessToken: string | null = null
let refreshPromise: Promise<string> | null = null
let sessionGeneration = 0
let onSessionLost: (() => void) | null = null

export class ApiError extends Error {
  readonly status: number
  readonly errors: Record<string, string[]>

  constructor(
    status: number,
    message: string,
    errors: Record<string, string[]> = {},
  ) {
    super(message)
    this.status = status
    this.errors = errors
  }
}

export function setSessionLostHandler(handler: (() => void) | null) {
  onSessionLost = handler
}

export function clearAccessToken() {
  sessionGeneration += 1
  accessToken = null
}

async function responseError(response: Response): Promise<ApiError> {
  const body = await response.json().catch(() => null) as {
    title?: string
    errors?: Record<string, string[]>
  } | null
  return new ApiError(
    response.status,
    body?.title ?? `Request failed (${response.status}).`,
    body?.errors ?? {},
  )
}

async function refreshToken(): Promise<string> {
  if (!refreshPromise) {
    const generation = sessionGeneration
    refreshPromise = (async () => {
      const response = await fetch(`${apiBaseUrl}/auth/refresh`, {
        method: 'POST',
        credentials: 'include',
      })
      if (!response.ok) throw await responseError(response)
      const body = await response.json() as { accessToken: string }
      if (generation !== sessionGeneration) throw new Error('Session changed during refresh.')
      accessToken = body.accessToken
      return body.accessToken
    })().finally(() => { refreshPromise = null })
  }
  return refreshPromise
}

export async function restoreAccessToken(): Promise<void> {
  try {
    await refreshToken()
  } catch {
    clearAccessToken()
    throw new ApiError(401, 'Session could not be restored.')
  }
}

export async function apiRequest<T>(path: string, init: RequestInit = {}): Promise<T> {
  const sentToken = accessToken
  const send = (token: string | null) => {
    const headers = new Headers(init.headers)
    if (typeof init.body === 'string' && !headers.has('Content-Type')) {
      headers.set('Content-Type', 'application/json')
    }
    if (token) headers.set('Authorization', `Bearer ${token}`)
    return fetch(`${apiBaseUrl}${path}`, { ...init, headers })
  }

  let response = await send(sentToken)
  if (response.status === 401) {
    let token: string
    try {
      token = accessToken && accessToken !== sentToken
        ? accessToken
        : await refreshToken()
    } catch {
      clearAccessToken()
      onSessionLost?.()
      throw await responseError(response)
    }
    response = await send(token)
  }
  if (response.status === 401) {
    clearAccessToken()
    onSessionLost?.()
  }
  if (!response.ok) throw await responseError(response)
  if (response.status === 204) return undefined as T
  return response.json() as Promise<T>
}

export async function authRequest<T>(path: string, body: object): Promise<T> {
  const response = await fetch(`${apiBaseUrl}/auth/${path}`, {
    method: 'POST',
    credentials: 'include',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  if (!response.ok) throw await responseError(response)
  return response.json() as Promise<T>
}

export async function loginRequest(email: string, password: string): Promise<void> {
  const result = await authRequest<{ accessToken: string }>('login', { email, password })
  sessionGeneration += 1
  accessToken = result.accessToken
}

export async function logoutRequest(): Promise<void> {
  const response = await fetch(`${apiBaseUrl}/auth/logout`, {
    method: 'POST',
    credentials: 'include',
  })
  if (!response.ok) throw await responseError(response)
}
