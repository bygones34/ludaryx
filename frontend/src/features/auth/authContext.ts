import { createContext } from 'react'

export type AuthUser = {
  id: string
  username: string
  email: string
  displayName: string
  createdAt: string
}

export type AuthContextValue = {
  status: 'initializing' | 'anonymous' | 'authenticated'
  user: AuthUser | null
  login: (email: string, password: string) => Promise<void>
  register: (username: string, email: string, password: string) => Promise<void>
  logout: () => Promise<void>
}

export const AuthContext = createContext<AuthContextValue | null>(null)
