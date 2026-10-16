import { useQuery } from '@tanstack/react-query'
import { client, unwrap } from './api/client'

// Temporary probe page: proves the typed client, the error middleware and React Query work end to end. Without a token
// the API answers 401 with an empty body, which must show up as an error here — not as an empty list.
function App() {
  const { data, error, isPending } = useQuery({
    queryKey: ['resources'],
    // The signal lets React Query cancel the request (unmount, StrictMode's double mount, a newer fetch).
    queryFn: ({ signal }) => unwrap(client.GET('/api/Resource', { signal })),
  })

  return (
    <main>
      <h1>BookingApi</h1>
      <h2>GET /api/Resource</h2>
      {isPending && <p>Loading…</p>}
      {error && (
        <p role="alert">
          {error.status ?? 'No response'} ({error.kind}): {error.message}
        </p>
      )}
      {data && (
        <ul>
          {data.map((resource) => (
            <li key={resource.id}>
              {resource.name} — {resource.type}, capacity {resource.capacity}
            </li>
          ))}
        </ul>
      )}
    </main>
  )
}

export default App
