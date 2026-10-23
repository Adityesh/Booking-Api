import { delay, http, HttpResponse } from 'msw'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { server } from '@/test/server'
import { ApiError, client, onRefreshed, onUnauthorized, refreshSession, setAccessToken, unwrap } from './client'

// client.ts keeps the access token and the in-flight refresh at module level, like the running app. Every test starts
// from a known token and leaves no listeners behind.

function session(token: string, username = 'someone') {
  return { token, username, role: 'User' as const, expiresAt: new Date(Date.now() + 15 * 60_000).toISOString() }
}

// The refresh endpoint, answering with `respond` and counting calls.
function refreshReturns(respond: () => Response | Promise<Response>) {
  const calls = { count: 0 }
  server.use(
    http.post('*/api/Auth/refresh', async () => {
      calls.count++
      return respond()
    }),
  )
  return calls
}

// GET /api/Waitlist/me that only accepts `validToken`, recording the Authorization header of every attempt.
function waitlistAccepting(validToken: string) {
  const seen: (string | null)[] = []
  server.use(
    http.get('*/api/Waitlist/me', ({ request }) => {
      const header = request.headers.get('Authorization')
      seen.push(header)
      return header === `Bearer ${validToken}` ? HttpResponse.json([]) : new HttpResponse(null, { status: 401 })
    }),
  )
  return seen
}

const getWaitlist = () => unwrap(client.GET('/api/Waitlist/me'))

const cleanups: (() => void)[] = []
function listen<T extends unknown[]>(subscribe: (listener: (...args: T) => void) => () => void) {
  const listener = vi.fn<(...args: T) => void>()
  cleanups.push(subscribe(listener))
  return listener
}

beforeEach(() => setAccessToken('old-token'))
afterEach(() => {
  cleanups.splice(0).forEach((cleanup) => cleanup())
  setAccessToken(null)
})

describe('a 401 on a request that carried a token', () => {
  it('refreshes, then replays the request with the new token', async () => {
    const refresh = refreshReturns(() => HttpResponse.json(session('new-token')))
    const seen = waitlistAccepting('new-token')
    const refreshed = listen(onRefreshed)

    await expect(getWaitlist()).resolves.toEqual([])

    expect(refresh.count).toBe(1)
    expect(seen).toEqual(['Bearer old-token', 'Bearer new-token'])
    expect(refreshed).toHaveBeenCalledWith(expect.objectContaining({ token: 'new-token' }))
  })

  it('replays a POST with its original body', async () => {
    refreshReturns(() => HttpResponse.json(session('new-token')))
    const bodies: unknown[] = []
    server.use(
      http.post('*/api/Booking', async ({ request }) => {
        bodies.push(await request.json())
        return request.headers.get('Authorization') === 'Bearer new-token'
          ? HttpResponse.json({ id: 7 }, { status: 201 })
          : new HttpResponse(null, { status: 401 })
      }),
    )
    const body = { resourceId: 3, startTime: '2030-01-01T10:00:00Z', endTime: '2030-01-01T11:00:00Z' }

    await client.POST('/api/Booking', { body })

    expect(bodies).toEqual([body, body])
  })

  it('refreshes only once when several requests fail on the same expired token', async () => {
    // The refresh is slow, so all three 401s arrive while it is still in flight.
    const refresh = refreshReturns(async () => {
      await delay(30)
      return HttpResponse.json(session('new-token'))
    })
    const seen = waitlistAccepting('new-token')

    await Promise.all([getWaitlist(), getWaitlist(), getWaitlist()])

    expect(refresh.count).toBe(1)
    expect(seen.filter((header) => header === 'Bearer new-token')).toHaveLength(3)
  })

  it('a request whose 401 arrives after another one already refreshed just resends, without rotating again', async () => {
    const refresh = refreshReturns(() => HttpResponse.json(session('new-token')))
    waitlistAccepting('new-token')
    let releaseSlowRequest!: () => void
    const slowRequestMayAnswer = new Promise<void>((resolve) => (releaseSlowRequest = resolve))
    const resourceAttempts: (string | null)[] = []
    server.use(
      http.get('*/api/Resource', async ({ request }) => {
        const header = request.headers.get('Authorization')
        resourceAttempts.push(header)
        if (header === 'Bearer new-token') return HttpResponse.json([])
        await slowRequestMayAnswer // its 401 only comes back after the other request has refreshed
        return new HttpResponse(null, { status: 401 })
      }),
    )

    const slow = unwrap(client.GET('/api/Resource')) // sent with old-token
    await getWaitlist() // 401 → refresh → the current token is now new-token
    releaseSlowRequest()

    await expect(slow).resolves.toEqual([])
    expect(refresh.count).toBe(1)
    expect(resourceAttempts).toEqual(['Bearer old-token', 'Bearer new-token'])
  })

  it('signs out when the refresh is refused too', async () => {
    refreshReturns(() => HttpResponse.text('Session expired', { status: 401 }))
    waitlistAccepting('never')
    const unauthorized = listen(onUnauthorized)

    const error = await getWaitlist().catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).kind).toBe('unauthorized')
    expect(unauthorized).toHaveBeenCalledTimes(1)
    // The dead token is gone: the next request goes out without one.
    const seen = waitlistAccepting('never')
    await getWaitlist().catch(() => {})
    expect(seen).toEqual([null])
  })

  it('keeps the session when the refresh cannot reach the server', async () => {
    refreshReturns(() => HttpResponse.error())
    const seen = waitlistAccepting('never')
    const unauthorized = listen(onUnauthorized)

    const error = await getWaitlist().catch((e: unknown) => e)

    expect((error as ApiError).kind).toBe('network')
    expect(unauthorized).not.toHaveBeenCalled()
    await getWaitlist().catch(() => {})
    expect(seen.at(-1)).toBe('Bearer old-token')
  })

  it('reports the replayed error, without signing out, when the retry fails for another reason', async () => {
    refreshReturns(() => HttpResponse.json(session('new-token')))
    server.use(
      http.get('*/api/Waitlist/me', ({ request }) =>
        request.headers.get('Authorization') === 'Bearer new-token'
          ? new HttpResponse(null, { status: 403 })
          : new HttpResponse(null, { status: 401 }),
      ),
    )
    const unauthorized = listen(onUnauthorized)

    const error = await getWaitlist().catch((e: unknown) => e)

    expect((error as ApiError).kind).toBe('forbidden')
    expect(unauthorized).not.toHaveBeenCalled()
  })
})

describe('a 401 on a request without a token', () => {
  it('never refreshes: a wrong password on login is just an error', async () => {
    setAccessToken(null)
    const refresh = refreshReturns(() => HttpResponse.json(session('new-token')))
    server.use(http.post('*/api/Auth/login', () => HttpResponse.text('Incorrect credentials', { status: 401 })))
    const unauthorized = listen(onUnauthorized)

    const error = await client
      .POST('/api/Auth/login', { body: { username: 'someone', password: 'Wrong_pass1' } })
      .catch((e: unknown) => e)

    expect((error as ApiError).message).toBe('Incorrect credentials')
    expect(refresh.count).toBe(0)
    expect(unauthorized).not.toHaveBeenCalled()
  })
})

describe('refreshSession', () => {
  it('shares one request between concurrent callers', async () => {
    const refresh = refreshReturns(() => HttpResponse.json(session('new-token')))

    const first = refreshSession()
    const second = refreshSession()

    expect(second).toBe(first)
    await expect(first).resolves.toEqual(expect.objectContaining({ token: 'new-token' }))
    expect(refresh.count).toBe(1)
  })

  it('resolves to null when the API refuses the cookie', async () => {
    refreshReturns(() => HttpResponse.text('Session expired', { status: 401 }))

    await expect(refreshSession()).resolves.toBeNull()
  })

  it('sends no body and no Authorization header: the cookie is the only credential', async () => {
    let request: Request | undefined
    server.use(
      http.post('*/api/Auth/refresh', ({ request: r }) => {
        request = r
        return HttpResponse.json(session('new-token'))
      }),
    )

    await refreshSession()

    expect(request?.headers.get('Authorization')).toBeNull()
    expect(await request?.text()).toBe('')
  })

  it('runs inside a Web Lock when the browser has them, so tabs take turns', async () => {
    refreshReturns(() => HttpResponse.json(session('new-token')))
    const lockRequest = vi.fn((_name: string, callback: () => Promise<unknown>) => callback())
    // jsdom has no navigator.locks; the app falls back to plain single-flight there.
    vi.stubGlobal('navigator', { ...navigator, locks: { request: lockRequest } })

    try {
      await refreshSession()
    } finally {
      vi.unstubAllGlobals()
    }

    expect(lockRequest).toHaveBeenCalledWith('bookingapi-refresh', expect.any(Function))
  })
})
