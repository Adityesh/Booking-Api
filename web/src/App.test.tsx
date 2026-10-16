import { render, screen, waitFor } from '@testing-library/react'
import { delay, http, HttpResponse } from 'msw'
import { StrictMode } from 'react'
import { describe, expect, it, vi } from 'vitest'
import App from './App'
import { server } from './test/server'

const RESOURCE_URL = '/api/resource'

describe('App — GET /api/resource probe', () => {
  it('shows a loading state before the response arrives', async () => {
    server.use(
      http.get(RESOURCE_URL, async () => {
        await delay('infinite')
        return HttpResponse.text('')
      }),
    )

    render(<App />)

    expect(screen.getByText('Loading…')).toBeInTheDocument()
  })

  it('shows 401 with an explanation and an empty body, the way JwtBearer answers an anonymous call', async () => {
    server.use(
      http.get(RESOURCE_URL, () =>
        // Mirrors the real API: no body, only the WWW-Authenticate header (verified against the compose stack).
        new HttpResponse(null, { status: 401, headers: { 'WWW-Authenticate': 'Bearer' } }),
      ),
    )

    render(<App />)

    expect(await screen.findByText('401')).toBeInTheDocument()
    expect(screen.getByText(/expected: the endpoint requires a token/)).toBeInTheDocument()
    expect(screen.getByText('(empty body)')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('shows the status and raw body for a non-401 response, without the 401 explanation', async () => {
    server.use(http.get(RESOURCE_URL, () => HttpResponse.json([{ id: 1, name: 'Room A' }])))

    render(<App />)

    expect(await screen.findByText('200')).toBeInTheDocument()
    expect(screen.getByText('[{"id":1,"name":"Room A"}]')).toBeInTheDocument()
    expect(screen.queryByText(/expected: the endpoint requires a token/)).not.toBeInTheDocument()
  })

  it('shows an alert when the request fails at the network level', async () => {
    server.use(http.get(RESOURCE_URL, () => HttpResponse.error()))

    render(<App />)

    expect(await screen.findByRole('alert')).toHaveTextContent(/^Request failed: /)
    expect(screen.queryByText('Loading…')).not.toBeInTheDocument()
  })

  it('under StrictMode, the aborted first request does not surface as an error', async () => {
    // Spy on fetch, not inside the handler: React aborts the first request synchronously during StrictMode's
    // cleanup, so it is rejected before MSW ever hands it to a handler.
    const fetchSpy = vi.spyOn(globalThis, 'fetch')
    server.use(
      http.get(RESOURCE_URL, async () => {
        // Never answer. If the second request resolved, it would overwrite a wrongly rendered AbortError and hide
        // the bug, so the only thing that can settle here is the aborted first request.
        await delay('infinite')
        return HttpResponse.text('')
      }),
    )

    render(
      <StrictMode>
        <App />
      </StrictMode>,
    )

    // StrictMode's mount → cleanup → mount must actually have fired two requests, or this test proves nothing.
    // The first one must have been aborted by the effect cleanup.
    await waitFor(() => expect(fetchSpy).toHaveBeenCalledTimes(2))
    expect(fetchSpy.mock.calls[0][1]?.signal?.aborted).toBe(true)
    expect(fetchSpy.mock.calls[1][1]?.signal?.aborted).toBe(false)
    // The aborted request rejects with an AbortError; the component must swallow it, not render it.
    await expect(screen.findByRole('alert', {}, { timeout: 300 })).rejects.toThrow()
    expect(screen.getByText('Loading…')).toBeInTheDocument()
  })
})
