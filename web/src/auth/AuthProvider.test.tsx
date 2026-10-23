import { QueryClientProvider, type QueryClient } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { delay, http, HttpResponse } from 'msw'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { setAccessToken } from '@/api/client'
import { createAppQueryClient } from '@/api/queryClient'
import { server } from '@/test/server'
import { useAuth } from './authContext'
import { AuthProvider } from './AuthProvider'
import { RedirectIfAuthenticated, RequireAuth } from './guards'

// The real provider and guards around stand-in pages, so these tests only depend on the session logic.

const MINUTE = 60_000

function session(username = 'someone', expiresInMs = 15 * MINUTE) {
  return {
    token: `token-for-${username}`,
    username,
    role: 'User' as const,
    expiresAt: new Date(Date.now() + expiresInMs).toISOString(),
  }
}

// Answers each refresh with the next response in the list (the last one repeats), counting calls.
function refreshAnswers(...responses: (() => Response | Promise<Response>)[]) {
  const calls = { count: 0 }
  server.use(
    http.post('*/api/Auth/refresh', () => {
      const respond = responses[Math.min(calls.count, responses.length - 1)]
      calls.count++
      return respond()
    }),
  )
  return calls
}

const signedOut = () => HttpResponse.text('Session expired', { status: 401 })

function ProtectedPage() {
  const { user, signOut } = useAuth()
  return (
    <>
      <p>Signed in as {user?.username}</p>
      <button onClick={() => void signOut()}>Sign out</button>
    </>
  )
}

let queryClient: QueryClient

function renderAt(path: string) {
  const router = createMemoryRouter(
    [
      { element: <RedirectIfAuthenticated />, children: [{ path: '/login', element: <p>Login page</p> }] },
      {
        element: <RequireAuth />,
        children: [
          { path: '/', element: <p>Start page</p> },
          { path: '/bookings', element: <ProtectedPage /> },
        ],
      },
    ],
    { initialEntries: [path] },
  )
  render(
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <RouterProvider router={router} />
      </AuthProvider>
    </QueryClientProvider>,
  )
  return router
}

beforeEach(() => {
  queryClient = createAppQueryClient()
})
afterEach(() => setAccessToken(null))

describe('restoring the session on startup', () => {
  it('shows a loading state, not the login page, while the refresh is in flight', async () => {
    let answer!: () => void
    const answered = new Promise<void>((resolve) => (answer = resolve))
    refreshAnswers(async () => {
      await answered
      return HttpResponse.json(session('alice'))
    })

    const router = renderAt('/bookings')

    expect(screen.getByRole('status')).toHaveTextContent('Loading…')
    expect(router.state.location.pathname).toBe('/bookings')
    answer()
    expect(await screen.findByText('Signed in as alice')).toBeInTheDocument()
  })

  it('a reload with a valid cookie lands on the page that was open', async () => {
    const refresh = refreshAnswers(() => HttpResponse.json(session('alice')))

    const router = renderAt('/bookings')

    expect(await screen.findByText('Signed in as alice')).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/bookings')
    expect(refresh.count).toBe(1)
  })

  it('without a valid cookie it goes to /login, remembering the page', async () => {
    refreshAnswers(signedOut)

    const router = renderAt('/bookings')

    expect(await screen.findByText('Login page')).toBeInTheDocument()
    expect(router.state.location.state).toEqual({ from: expect.objectContaining({ pathname: '/bookings' }) })
  })

  it('goes to /login when the API is unreachable, instead of loading forever', async () => {
    refreshAnswers(() => HttpResponse.error())

    renderAt('/bookings')

    expect(await screen.findByText('Login page')).toBeInTheDocument()
  })

  it('a reload on /login with a valid cookie skips the form', async () => {
    refreshAnswers(() => HttpResponse.json(session('alice')))

    const router = renderAt('/login')

    expect(await screen.findByText('Start page')).toBeInTheDocument()
    expect(router.state.location.pathname).toBe('/')
    expect(screen.queryByText('Login page')).not.toBeInTheDocument()
  })
})

describe('signing out', () => {
  it('revokes the cookie on the server first, then shows the login page without remembering the page', async () => {
    refreshAnswers(() => HttpResponse.json(session('alice')))
    const events: string[] = []
    server.use(
      http.post('*/api/Auth/logout', async () => {
        events.push('logout request')
        await delay(10)
        return new HttpResponse(null, { status: 204 })
      }),
    )
    const router = renderAt('/bookings')
    await screen.findByText('Signed in as alice')

    await userEvent.setup().click(screen.getByRole('button', { name: 'Sign out' }))

    expect(await screen.findByText('Login page')).toBeInTheDocument()
    expect(events).toEqual(['logout request'])
    expect(router.state.location.state).toBeNull()
  })

  it('still signs out locally when the logout request fails', async () => {
    refreshAnswers(() => HttpResponse.json(session('alice')))
    server.use(http.post('*/api/Auth/logout', () => HttpResponse.error()))
    renderAt('/bookings')
    await screen.findByText('Signed in as alice')

    await userEvent.setup().click(screen.getByRole('button', { name: 'Sign out' }))

    expect(await screen.findByText('Login page')).toBeInTheDocument()
  })

  it('tells the other tabs', async () => {
    refreshAnswers(() => HttpResponse.json(session('alice')))
    server.use(http.post('*/api/Auth/logout', () => new HttpResponse(null, { status: 204 })))
    const otherTab = new BroadcastChannel('bookingapi-auth')
    const received: unknown[] = []
    otherTab.onmessage = (event) => received.push(event.data)
    renderAt('/bookings')
    await screen.findByText('Signed in as alice')

    try {
      await userEvent.setup().click(screen.getByRole('button', { name: 'Sign out' }))
      await waitFor(() => expect(received).toEqual(['signed-out']))
    } finally {
      otherTab.close()
    }
  })

  it('follows a sign-out in another tab', async () => {
    refreshAnswers(() => HttpResponse.json(session('alice')))
    renderAt('/bookings')
    await screen.findByText('Signed in as alice')
    const otherTab = new BroadcastChannel('bookingapi-auth')

    try {
      otherTab.postMessage('signed-out')
      expect(await screen.findByText('Login page')).toBeInTheDocument()
    } finally {
      otherTab.close()
    }
  })
})

describe('the expiry timer', () => {
  // The provider refreshes one minute before expiresAt, so a token that expires in "1 minute + 50 ms" is refreshed
  // after about 50 ms: real timers, no faking needed.
  const almostDue = MINUTE + 50

  it('refreshes shortly before the access token expires, and stays signed in', async () => {
    const refresh = refreshAnswers(
      () => HttpResponse.json(session('alice', almostDue)),
      () => HttpResponse.json(session('alice')),
    )

    renderAt('/bookings')

    await waitFor(() => expect(refresh.count).toBe(2))
    expect(screen.getByText('Signed in as alice')).toBeInTheDocument()
  })

  it('signs out when that refresh is refused, remembering the page', async () => {
    refreshAnswers(() => HttpResponse.json(session('alice', almostDue)), signedOut)

    const router = renderAt('/bookings')
    await screen.findByText('Signed in as alice')

    expect(await screen.findByText('Login page')).toBeInTheDocument()
    expect(router.state.location.state).toEqual({ from: expect.objectContaining({ pathname: '/bookings' }) })
  })

  it('clears the cache when the refreshed session belongs to someone else', async () => {
    // Another tab signed out and a different user signed in: the shared cookie is now theirs.
    refreshAnswers(
      () => HttpResponse.json(session('alice', almostDue)),
      () => HttpResponse.json(session('bob')),
    )
    renderAt('/bookings')
    await screen.findByText('Signed in as alice')
    queryClient.setQueryData(['bookings', 'list', 'me'], ['alice’s booking'])

    expect(await screen.findByText('Signed in as bob')).toBeInTheDocument()
    expect(queryClient.getQueryData(['bookings', 'list', 'me'])).toBeUndefined()
  })
})
