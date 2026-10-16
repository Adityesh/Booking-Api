import { useQuery } from '@tanstack/react-query'
import { Button } from '@/components/ui/button'
import { client, unwrap } from './api/client'

// Temporary probe page: proves the typed client, the error middleware and React Query work end to end. Without a token
// the API answers 401 with an empty body, which must show up as an error here — not as an empty list.
function App() {
  const { data, error, isPending, isFetching, refetch } = useQuery({
    queryKey: ['resources'],
    // The signal lets React Query cancel the request (unmount, StrictMode's double mount, a newer fetch).
    queryFn: ({ signal }) => unwrap(client.GET('/api/Resource', { signal })),
  })

  return (
    <main className="mx-auto max-w-3xl space-y-4 px-4 py-8">
      <h1 className="font-heading text-2xl font-semibold">BookingApi</h1>
      <div className="flex items-center justify-between">
        <h2 className="text-muted-foreground font-mono text-sm">GET /api/Resource</h2>
        <Button variant="outline" size="sm" onClick={() => refetch()} disabled={isFetching}>
          Refetch
        </Button>
      </div>
      {isPending && <p className="text-muted-foreground">Loading…</p>}
      {error && (
        <p role="alert" className="bg-destructive/10 text-destructive rounded-lg px-3 py-2 text-sm">
          {error.status ?? 'No response'} ({error.kind}): {error.message}
        </p>
      )}
      {data && (
        <ul className="divide-y rounded-lg border">
          {data.map((resource) => (
            <li key={resource.id} className="px-3 py-2">
              {resource.name} — {resource.type}, capacity {resource.capacity}
            </li>
          ))}
        </ul>
      )}
    </main>
  )
}

export default App
