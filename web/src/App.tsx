import { useEffect, useState } from 'react'

type ProbeState =
  | { kind: 'loading' }
  | { kind: 'response'; status: number; body: string }
  | { kind: 'error'; message: string }

function App() {
  const [state, setState] = useState<ProbeState>({ kind: 'loading' })

  useEffect(() => {
    // StrictMode mounts effects twice in dev; aborting the first request keeps only one result.
    const controller = new AbortController()

    fetch('/api/resource', { signal: controller.signal })
      // Read as text, not json(): a JwtBearer 401 has an EMPTY body (details are in WWW-Authenticate).
      .then(async (res) => setState({ kind: 'response', status: res.status, body: await res.text() }))
      .catch((err: unknown) => {
        if (controller.signal.aborted) return
        setState({ kind: 'error', message: err instanceof Error ? err.message : String(err) })
      })

    return () => controller.abort()
  }, [])

  return (
    <main>
      <h1>BookingApi</h1>
      <h2>GET /api/resource (no token)</h2>
      {state.kind === 'loading' && <p>Loading…</p>}
      {state.kind === 'error' && <p role="alert">Request failed: {state.message}</p>}
      {state.kind === 'response' && (
        <>
          <p>
            Status: <strong>{state.status}</strong>
            {state.status === 401 && ' — expected: the endpoint requires a token'}
          </p>
          <pre>{state.body || '(empty body)'}</pre>
        </>
      )}
    </main>
  )
}

export default App
