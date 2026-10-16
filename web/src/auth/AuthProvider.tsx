import { useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { onUnauthorized, setAccessToken } from '@/api/client'
import { AuthContext, type AuthResponse, type AuthUser, type SignOutReason } from './authContext'

// STUB: enough state for the route guards. Still to do: call signIn from the login/register screens, and a timer that
// signs out at user.expiresAt instead of waiting for the first request to fail with 401.
export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()
  const [user, setUser] = useState<AuthUser | null>(null)
  const [lastSignOut, setLastSignOut] = useState<SignOutReason>(null)

  // Clearing the cache on both sign-in and sign-out means one user's data is never shown to the next.
  const signIn = useCallback(
    (response: AuthResponse) => {
      queryClient.clear()
      setAccessToken(response.token)
      setUser({ username: response.username, role: response.role, expiresAt: response.expiresAt })
      setLastSignOut(null)
    },
    [queryClient],
  )

  // Both updates land in the same render, so <RequireAuth> sees the reason together with user = null.
  const endSession = useCallback(
    (reason: Exclude<SignOutReason, null>) => {
      setAccessToken(null)
      setUser(null)
      setLastSignOut(reason)
      queryClient.clear()
    },
    [queryClient],
  )

  const signOut = useCallback(() => endSession('manual'), [endSession])

  // A request that carried the token got a 401: the session is over. Dropping the user makes <RequireAuth> redirect
  // to /login, remembering the current page so the user can continue after signing in again.
  useEffect(() => onUnauthorized(() => endSession('expired')), [endSession])

  const value = useMemo(() => ({ user, lastSignOut, signIn, signOut }), [user, lastSignOut, signIn, signOut])
  return <AuthContext value={value}>{children}</AuthContext>
}
