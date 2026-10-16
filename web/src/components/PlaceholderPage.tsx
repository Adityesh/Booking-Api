import type { ReactNode } from 'react'

type PlaceholderPageProps = {
  title: string
  description: string
  // The API calls the finished page will make, e.g. "GET /api/Waitlist/me".
  endpoints: string[]
  // Set when the backend endpoint the page needs does not exist yet.
  blockedBy?: string
  children?: ReactNode
}

// Temporary stand-in for a screen that isn't built yet. Delete it once the last page is real.
export function PlaceholderPage({ title, description, endpoints, blockedBy, children }: PlaceholderPageProps) {
  return (
    <section className="space-y-4">
      <h1 className="font-heading text-2xl font-semibold">{title}</h1>
      <p className="text-muted-foreground">{description}</p>
      {blockedBy && (
        <p className="rounded-lg border border-amber-500/40 bg-amber-500/10 px-3 py-2 text-sm">
          Blocked by the backend: {blockedBy}
        </p>
      )}
      <ul className="text-muted-foreground space-y-1 font-mono text-xs">
        {endpoints.map((endpoint) => (
          <li key={endpoint}>{endpoint}</li>
        ))}
      </ul>
      {children}
    </section>
  )
}
