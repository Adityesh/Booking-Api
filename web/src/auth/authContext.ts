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

// 'manual': the user pressed "Sign out" (in this tab or another one). 'expired': the session could not be refreshed
// (the refresh token expired or was revoked). null: not signed out in this page's lifetime (first visit, or a reload
// with no session to restore).
export type SignOutReason = 'manual' | 'expired' | null

export type AuthContextValue = {
  // True only while the app starts and tries to restore the session from the refresh-token cookie. The guards wait for
  // it: deciding "signed out" before it settles would bounce a signed-in user to /login on every reload.
  isRestoring: boolean
  user: AuthUser | null
  lastSignOut: SignOutReason
  signIn: (response: AuthResponse) => void
  // Revokes the refresh token on the server, then clears the session locally (also in this browser's other tabs).
  signOut: () => Promise<void>
}

export const AuthContext = createContext<AuthContextValue | null>(null)

export function useAuth(): AuthContextValue {
  const context = useContext(AuthContext)
  if (!context) throw new Error('useAuth must be used inside <AuthProvider>')
  return context
}
