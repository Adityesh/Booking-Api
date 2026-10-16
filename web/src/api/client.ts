import createFetchClient, { type Middleware } from 'openapi-fetch'
import type { paths } from './schema'

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

// ---------- access token (in memory only, never localStorage) ----------

// A page reload therefore signs the user out; that is the accepted trade-off until refresh-token cookies exist.
let accessToken: string | null = null
const unauthorizedListeners = new Set<() => void>()

export function setAccessToken(token: string | null): void {
  accessToken = token
}

// Called when a request that carried a token gets a 401 (token expired or rejected). Returns an unsubscribe function.
export function onUnauthorized(listener: () => void): () => void {
  unauthorizedListeners.add(listener)
  return () => unauthorizedListeners.delete(listener)
}

// ---------- middleware ----------

const authMiddleware: Middleware = {
  onRequest({ request }) {
    if (accessToken) request.headers.set('Authorization', `Bearer ${accessToken}`)
  },
}

const errorMiddleware: Middleware = {
  // openapi-fetch RETURNS errors as `{ error }` instead of throwing, and for an empty body (Content-Length: 0, the
  // JwtBearer 401 and the 403) even returns `{ error: undefined }`. React Query only sees a failure when the queryFn
  // throws, so without this an empty 401/403 would resolve as a success with no data.
  // Throwing here rejects the request instead, for every non-2xx response.
  async onResponse({ request, response }) {
    if (response.ok) return

    const error = await toApiError(response)
    // Only a request that actually sent a token means "the session is over". A 401 from login (wrong password)
    // carries no token and must not trigger a global sign-out.
    if (error.kind === 'unauthorized' && request.headers.has('Authorization')) {
      accessToken = null
      unauthorizedListeners.forEach((listener) => listener())
    }
    throw error
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
