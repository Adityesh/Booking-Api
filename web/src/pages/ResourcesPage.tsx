import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { client, unwrap } from '@/api/client'
import { PlaceholderPage } from '@/components/PlaceholderPage'

// Already fetches the real list (the old probe page); the layout and filtering by type are still to do.
export function ResourcesPage() {
  const { data, error, isPending } = useQuery({
    queryKey: ['resources'],
    queryFn: ({ signal }) => unwrap(client.GET('/api/Resource', { signal })),
  })

  return (
    <PlaceholderPage
      title="Resources"
      description="Active rooms and equipment. Pick one to see its details and book it."
      endpoints={['GET /api/Resource']}
    >
      {isPending && <p className="text-muted-foreground">Loading…</p>}
      {error && (
        <p role="alert" className="bg-destructive/10 text-destructive rounded-lg px-3 py-2 text-sm">
          {error.message}
        </p>
      )}
      {data && (
        <ul className="divide-y rounded-lg border">
          {data.map((resource) => (
            <li key={resource.id}>
              <Link to={`/resources/${resource.id}`} className="hover:bg-muted block px-3 py-2">
                {resource.name} — {resource.type}, capacity {resource.capacity}
              </Link>
            </li>
          ))}
        </ul>
      )}
    </PlaceholderPage>
  )
}
