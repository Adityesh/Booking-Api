import { createContext, useContext } from 'react'
import type { components } from '@/api/schema'

export type AuthResponse = components['schemas']['AuthResponseDto']
export type UserRole = components['schemas']['UserRole']

// What the UI knows about the signed-in user. It comes from the login/register response, never from decoding the JWT:
// the token stays opaque to the UI and only travels in the Authorization header.
export type AuthUser = {
  username: string
  role: UserRole
  expiresAt: string
}

// 'manual': the user pressed "Sign out". 'expired': a request with the token got a 401. null: never signed in yet
// (first visit, or a reload, since the token lives in memory).
export type SignOutReason = 'manual' | 'expired' | null

export type AuthContextValue = {
  user: AuthUser | null
  lastSignOut: SignOutReason
  signIn: (response: AuthResponse) => void
  signOut: () => void
}

export const AuthContext = createContext<AuthContextValue | null>(null)

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext)
  if (!context) throw new Error('useAuth must be used inside <AuthProvider>')
  return context
}
