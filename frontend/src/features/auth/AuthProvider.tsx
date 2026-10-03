import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { AuthContext } from '@/features/auth/authContext'
import type { AuthContextValue, AuthUser } from '@/features/auth/authContext'
import {
  apiRequest,
  authRequest,
  clearAccessToken,
  loginRequest,
  logoutRequest,
  restoreAccessToken,
  setSessionLostHandler,
} from '@/lib/apiClient'

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()
  const [status, setStatus] = useState<AuthContextValue['status']>('initializing')
  const [user, setUser] = useState<AuthUser | null>(null)

  useEffect(() => {
    let active = true
    setSessionLostHandler(() => {
      queryClient.clear()
      setUser(null)
      setStatus('anonymous')
    })
    void (async () => {
      try {
        await restoreAccessToken()
        const currentUser = await apiRequest<AuthUser>('/users/me')
        if (active) {
          setUser(currentUser)
          setStatus('authenticated')
        }
      } catch {
        if (active) setStatus('anonymous')
      }
    })()
    return () => {
      active = false
      setSessionLostHandler(null)
    }
  }, [queryClient])

  async function login(email: string, password: string) {
    await loginRequest(email, password)
    try {
      const currentUser = await apiRequest<AuthUser>('/users/me')
      queryClient.clear()
      setUser(currentUser)
      setStatus('authenticated')
    } catch (error) {
      clearAccessToken()
      throw error
    }
  }

  async function register(username: string, email: string, password: string) {
    await authRequest('register', { username, email, password })
  }

  async function logout() {
    await logoutRequest()
    clearAccessToken()
    queryClient.clear()
    setUser(null)
    setStatus('anonymous')
  }

  return <AuthContext.Provider value={{ status, user, login, register, logout }}>
    {children}
  </AuthContext.Provider>
}
