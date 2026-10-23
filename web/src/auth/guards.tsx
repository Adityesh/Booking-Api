import { Link, Navigate, Outlet, useLocation, type Location } from 'react-router'
import { useAuth, type UserRole } from './authContext'

type FromState = { from?: Location } | null

// Shown while the session is being restored from the refresh-token cookie (one request, normally well under a second).
function RestoringSession() {
  return (
    <p role="status" className="text-muted-foreground p-6 text-center">
      Loading…
    </p>
  )
}

// Signed-out users go to /login, remembering where they were: after an expired session, signing in again continues on
// that page. Not after "Sign out": the next person to sign in starts at /.
export function RequireAuth() {
  const { isRestoring, user, lastSignOut } = useAuth()
  const location = useLocation()
  if (isRestoring) return <RestoringSession />
  if (!user) {
    const state: FromState = lastSignOut === 'manual' ? null : { from: location }
    return <Navigate to="/login" replace state={state} />
  }
  return <Outlet />
}

// UX only: the API's 403 is the real check. Rendered under <RequireAuth>, so `user` is set here.
export function RequireRole({ role }: { role: UserRole }) {
  const { user } = useAuth()
  if (user?.role !== role) {
    return (
      <div className="space-y-2">
        <h1 className="font-heading text-2xl font-semibold">Not allowed</h1>
        <p className="text-muted-foreground">You don't have access to this page.</p>
        <Link to="/" className="text-sm underline underline-offset-4">
          Back to the start page
        </Link>
      </div>
    )
  }
  return <Outlet />
}

// For /login and /register. Signing in re-renders this guard, which sends the user back to the page that bounced them
// to /login (or to /). The login screen therefore never navigates itself after a successful sign-in.
// While restoring, it waits too: otherwise a reload on /login would flash the form before redirecting.
export function RedirectIfAuthenticated() {
  const { isRestoring, user } = useAuth()
  const from = (useLocation().state as FromState)?.from
  if (isRestoring) return <RestoringSession />
  if (user) return <Navigate to={from ? `${from.pathname}${from.search}${from.hash}` : '/'} replace />
  return <Outlet />
}
