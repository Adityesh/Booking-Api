import { useQueryClient } from '@tanstack/react-query'
import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { client, onRefreshed, onUnauthorized, refreshSession, setAccessToken } from '@/api/client'
import { AuthContext, type AuthResponse, type AuthUser, type SignOutReason } from './authContext'

// Refresh this long before the access token expires, so a request never goes out with a token about to lapse. The 401
// retry in the API client is the backstop (e.g. a laptop that slept through the timer).
const REFRESH_AHEAD_MS = 60_000

// Tabs of this browser share the refresh-token cookie, so signing out in one ends the session in all of them.
const CHANNEL_NAME = 'bookingapi-auth'
const SIGNED_OUT_MESSAGE = 'signed-out'

const toUser = (session: AuthResponse): AuthUser => ({
  username: session.username,
  role: session.role,
  expiresAt: session.expiresAt,
})

export function AuthProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()
  const [isRestoring, setIsRestoring] = useState(true)
  const [user, setUser] = useState<AuthUser | null>(null)
  const [lastSignOut, setLastSignOut] = useState<SignOutReason>(null)
  const userRef = useRef<AuthUser | null>(null)
  const channelRef = useRef<BroadcastChannel | null>(null)

  const applyUser = useCallback(
    (next: AuthUser | null) => {
      // A refresh can come back as a different user: another tab signed out and someone else signed in, so the shared
      // cookie now belongs to them. Never show the previous user's cached data.
      if (userRef.current?.username !== next?.username) queryClient.clear()
      userRef.current = next
      setUser(next)
    },
    [queryClient],
  )

  // Clearing the cache on both sign-in and sign-out means one user's data is never shown to the next.
  const signIn = useCallback(
    (response: AuthResponse) => {
      queryClient.clear()
      setAccessToken(response.token)
      applyUser(toUser(response))
      setLastSignOut(null)
    },
    [queryClient, applyUser],
  )

  // Both updates land in the same render, so <RequireAuth> sees the reason together with user = null.
  const endSession = useCallback(
    (reason: Exclude<SignOutReason, null>) => {
      setAccessToken(null)
      applyUser(null)
      setLastSignOut(reason)
      queryClient.clear()
    },
    [queryClient, applyUser],
  )

  const signOut = useCallback(async () => {
    try {
      // Revoke first: the cookie is HttpOnly, so only the server can delete it. Clearing the session locally without
      // this would leave a live cookie that silently signs the user back in on the next reload.
      await client.POST('/api/Auth/logout')
    } catch {
      // Network failure: sign out locally anyway. The cookie survives until it expires or the next successful logout.
    }
    endSession('manual')
    channelRef.current?.postMessage(SIGNED_OUT_MESSAGE)
  }, [endSession])

  // Every successful refresh (startup, timer, or a 401 retry in the API client) updates the user and its expiry.
  useEffect(() => onRefreshed((session) => applyUser(toUser(session))), [applyUser])

  // Refreshing failed after a 401: the session is over. Dropping the user makes <RequireAuth> redirect to /login,
  // remembering the current page so the user can continue after signing in again.
  useEffect(() => onUnauthorized(() => endSession('expired')), [endSession])

  // Startup: the access token was lost with the page, but the refresh-token cookie may still be valid.
  // Under StrictMode this effect runs twice; refreshSession() is single-flight, so it is still one request.
  useEffect(() => {
    let cancelled = false
    refreshSession()
      // null (no or dead cookie) and errors (API unreachable) both mean "not signed in" here: show the login page.
      .catch(() => null)
      .finally(() => {
        if (!cancelled) setIsRestoring(false)
      })
    return () => {
      cancelled = true
    }
  }, [])

  // Proactive refresh shortly before the access token expires. A new expiresAt re-arms the timer.
  const expiresAt = user?.expiresAt
  useEffect(() => {
    if (!expiresAt) return
    const delay = Math.max(0, Date.parse(expiresAt) - Date.now() - REFRESH_AHEAD_MS)
    const timer = setTimeout(() => {
      refreshSession()
        .then((session) => {
          if (!session) endSession('expired')
        })
        // Network error or 5xx: keep the session. If the token runs out first, the next request's 401 retries.
        .catch(() => {})
    }, delay)
    return () => clearTimeout(timer)
  }, [expiresAt, endSession])

  useEffect(() => {
    if (typeof BroadcastChannel === 'undefined') return
    const channel = new BroadcastChannel(CHANNEL_NAME)
    channel.onmessage = (event: MessageEvent) => {
      if (event.data === SIGNED_OUT_MESSAGE && userRef.current) endSession('manual')
    }
    channelRef.current = channel
    return () => {
      channel.close()
      channelRef.current = null
    }
  }, [endSession])

  const value = useMemo(
    () => ({ isRestoring, user, lastSignOut, signIn, signOut }),
    [isRestoring, user, lastSignOut, signIn, signOut],
  )
  return <AuthContext value={value}>{children}</AuthContext>
}
