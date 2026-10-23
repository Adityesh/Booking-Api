import createFetchClient, { type Middleware } from 'openapi-fetch'
import type { components, paths } from './schema'

type AuthResponse = components['schemas']['AuthResponseDto']

// ---------- errors ----------

export type ApiErrorKind =
  | 'validation' // 400 with ProblemDetails field errors
  | 'badRequest' // 400 without field errors
  | 'unauthorized' // 401: no/expired token, or wrong credentials on login
  | 'forbidden' // 403: authenticated, but not allowed (e.g. not an admin)
  | 'notFound' // 404: also "exists but not yours" — the API never distinguishes the two
  | 'conflict' // 409
  | 'network' // the request never got a response
  | 'unexpected' // anything else, e.g. 500

// The ONE error type screens see. Every non-2xx response and every network failure becomes an ApiError, so components
// never parse response bodies or content types themselves.
export class ApiError extends Error {
  readonly status: number | undefined
  readonly kind: ApiErrorKind
  readonly fieldErrors: Readonly<Record<string, readonly string[]>>

  constructor(
    kind: ApiErrorKind,
    message: string,
    status?: number,
    fieldErrors: Record<string, string[]> = {},
  ) {
    super(message)
    this.name = 'ApiError'
    this.kind = kind
    this.status = status
    this.fieldErrors = fieldErrors
  }
}

// Makes ApiError the error type of every useQuery/useMutation, so `error.kind` is typed without casts.
// (openapi-react-query was dropped for this reason: it types errors as the contract's raw bodies, string |
// ValidationProblemDetails, or `never` when none are declared, which is not what errorMiddleware throws.)
declare module '@tanstack/react-query' {
  interface Register {
    defaultError: ApiError
  }
}

const fallbackMessages: Record<ApiErrorKind, string> = {
  validation: 'Some fields are invalid.',
  badRequest: 'The request was invalid.',
  unauthorized: 'Your session has expired. Please sign in again.',
  forbidden: 'You do not have permission to do that.',
  notFound: 'Not found.',
  conflict: 'That conflicts with the current state.',
  network: 'Could not reach the server. Check your connection and try again.',
  unexpected: 'Something went wrong. Please try again.',
}

function kindForStatus(status: number): ApiErrorKind {
  switch (status) {
    case 400:
      return 'badRequest'
    case 401:
      return 'unauthorized'
    case 403:
      return 'forbidden'
    case 404:
      return 'notFound'
    case 409:
      return 'conflict'
    default:
      return 'unexpected'
  }
}

type ProblemDetails = { title?: string; detail?: string; errors?: Record<string, string[]> }

// The API's error bodies come in three shapes (all declared in the OpenAPI document):
//   - application/problem+json  → ProblemDetails, with `errors` on validation failures
//   - text/plain                → a message string ("Resource not found.")
//   - empty                     → JwtBearer 401 and authorization 403 (only a WWW-Authenticate header)
export async function toApiError(response: Response): Promise<ApiError> {
  const status = response.status
  const contentType = response.headers.get('Content-Type') ?? ''
  const body = await response.text()

  if (body && contentType.includes('json')) {
    try {
      const problem = JSON.parse(body) as ProblemDetails
      const errors = problem.errors ?? {}
      if (status === 400 && Object.keys(errors).length > 0) {
        return new ApiError('validation', problem.title ?? fallbackMessages.validation, status, errors)
      }
      const kind = kindForStatus(status)
      return new ApiError(kind, problem.detail ?? problem.title ?? fallbackMessages[kind], status)
    } catch {
      // Not valid JSON despite the header: fall through and treat it as text.
    }
  }

  const kind = kindForStatus(status)
  // Only show text bodies the API wrote on purpose (404/409/login 401), never a 5xx page.
  const message = body && kind !== 'unexpected' ? body : fallbackMessages[kind]
  return new ApiError(kind, message, status)
}

// The messages for one form field from a 400 validation error. ProblemDetails keys are the DTO's C# property names
// ("Username") while request bodies are camelCase ("username"), so the lookup ignores case.
export function fieldErrors(error: ApiError | null, field: string): readonly string[] {
  if (error?.kind !== 'validation') return []
  const key = Object.keys(error.fieldErrors).find((k) => k.toLowerCase() === field.toLowerCase())
  return key ? error.fieldErrors[key] : []
}

// ---------- access token (in memory only, never localStorage) ----------

// The access token lives only in this variable. What survives a reload is the refresh-token cookie (HttpOnly, so no
// script can read it): on startup, and whenever the access token expires, refreshSession() trades it for a new one.
let accessToken: string | null = null
const unauthorizedListeners = new Set<() => void>()
const refreshedListeners = new Set<(session: AuthResponse) => void>()

export function setAccessToken(token: string | null): void {
  accessToken = token
}

// Called when the session is over: a request got a 401 and refreshing failed too. Returns an unsubscribe function.
export function onUnauthorized(listener: () => void): () => void {
  unauthorizedListeners.add(listener)
  return () => unauthorizedListeners.delete(listener)
}

// Called after every successful refresh, whoever triggered it (startup, the expiry timer, or a 401 retry).
export function onRefreshed(listener: (session: AuthResponse) => void): () => void {
  refreshedListeners.add(listener)
  return () => refreshedListeners.delete(listener)
}

function endSession(): void {
  accessToken = null
  unauthorizedListeners.forEach((listener) => listener())
}

// ---------- refresh ----------

let inFlightRefresh: Promise<AuthResponse | null> | null = null

// Trades the refresh-token cookie for a new access token (and a rotated cookie).
//   resolves to the new session  → refreshed
//   resolves to null             → the API said 401: no cookie, or it was expired/revoked. The session is over.
//   rejects with an ApiError     → network failure or 5xx. The session may still be fine; callers keep it.
// Single-flight within the tab: concurrent callers share one request. Across tabs, a Web Lock serialises refreshes, so
// a second tab only sends its request after the first tab's rotated cookie is in the jar. Two tabs sending the SAME
// cookie at once is what the server's grace window exists for; this keeps it from happening in the first place.
export function refreshSession(): Promise<AuthResponse | null> {
  inFlightRefresh ??= withRefreshLock(requestRefresh).finally(() => {
    inFlightRefresh = null
  })
  return inFlightRefresh
}

function withRefreshLock<T>(refresh: () => Promise<T>): Promise<T> {
  // Missing in very old browsers and in jsdom; the single-flight above still covers one tab.
  if (typeof navigator !== 'undefined' && navigator.locks) {
    return navigator.locks.request('bookingapi-refresh', refresh)
  }
  return refresh()
}

async function requestRefresh(): Promise<AuthResponse | null> {
  let response: Response
  try {
    // Plain fetch, not `client`: the refresh must never run through the 401-retry middleware below. The cookie is sent
    // automatically (same origin, and its Path=/api/Auth matches); the body is empty on purpose.
    response = await globalThis.fetch('/api/Auth/refresh', { method: 'POST' })
  } catch {
    throw new ApiError('network', fallbackMessages.network)
  }
  if (response.status === 401) return null
  if (!response.ok) throw await toApiError(response)

  const session = (await response.json()) as AuthResponse
  accessToken = session.token
  refreshedListeners.forEach((listener) => listener(session))
  return session
}

// ---------- middleware ----------

// A copy of every request that carries a token, taken before fetch consumes its body, so a 401 can be replayed.
const replayable = new WeakMap<Request, Request>()

const authMiddleware: Middleware = {
  onRequest({ request }) {
    if (!accessToken) return
    request.headers.set('Authorization', `Bearer ${accessToken}`)
    replayable.set(request, request.clone())
  },
}

// The access token expired (or was rejected): get a fresh one and send the request once more.
// Returns null when there is no way to continue the session.
async function replayWithFreshToken(request: Request): Promise<Response | null> {
  const copy = replayable.get(request)
  if (!copy) return null
  const sentToken = request.headers.get('Authorization')?.slice('Bearer '.length)
  // Several requests can fail with the same expired token. Once one of them has refreshed, the rest just resend with
  // the new token instead of rotating the cookie again.
  const token = accessToken && accessToken !== sentToken ? accessToken : (await refreshSession())?.token
  if (!token) return null
  copy.headers.set('Authorization', `Bearer ${token}`)
  return globalThis.fetch(copy)
}

const errorMiddleware: Middleware = {
  // openapi-fetch RETURNS errors as `{ error }` instead of throwing, and for an empty body (Content-Length: 0, the
  // JwtBearer 401 and the 403) even returns `{ error: undefined }`. React Query only sees a failure when the queryFn
  // throws, so without this an empty 401/403 would resolve as a success with no data.
  // Throwing here rejects the request instead, for every non-2xx response.
  async onResponse({ request, response }) {
    if (response.ok) return

    // Only a request that actually sent a token means "the access token is no longer good". A 401 from login (wrong
    // password) carries no token and must never trigger a refresh or a sign-out.
    if (response.status === 401 && request.headers.has('Authorization')) {
      // Throws a network ApiError if the refresh itself can't reach the server: the session is kept for the next try.
      const replayed = await replayWithFreshToken(request)
      if (replayed?.ok) return replayed
      if (!replayed || replayed.status === 401) {
        endSession()
        throw await toApiError(replayed ?? response)
      }
      // The retry got past authentication but failed for another reason (403, 404, ...): report that error instead.
      throw await toApiError(replayed)
    }

    throw await toApiError(response)
  },

  // Only runs when fetch itself rejects. Aborts are left alone: React Query cancels queries by aborting their signal
  // and must still see an AbortError, not an ApiError.
  onError({ error }) {
    if (error instanceof DOMException && error.name === 'AbortError') return
    return new ApiError('network', fallbackMessages.network)
  },
}

// ---------- clients ----------

// Relative URLs: the Vite dev proxy (and nginx in production) forward /api to the backend, so the browser only ever
// talks to its own origin. Never point this at the API host directly.
export const client = createFetchClient<paths>({ baseUrl: '' })
client.use(authMiddleware, errorMiddleware)

// For queryFn/mutationFn: returns the typed success body of an openapi-fetch call. Error responses never reach this
// point (errorMiddleware throws an ApiError instead), so `data` is the 2xx body — undefined only for a 204.
//   useQuery({ queryKey: ['resources'], queryFn: ({ signal }) => unwrap(client.GET('/api/Resource', { signal })) })
export async function unwrap<T>(request: Promise<{ data?: T; response: Response }>): Promise<T> {
  const { data } = await request
  return data as T
}
