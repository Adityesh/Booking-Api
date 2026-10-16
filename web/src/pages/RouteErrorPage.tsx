import { isRouteErrorResponse, Link, useRouteError } from 'react-router'

// The router's error boundary: a render error in any page lands here instead of a blank screen. API errors are not
// route errors; pages show those themselves from React Query's `error`.
export function RouteErrorPage() {
  const error = useRouteError()
  const message = isRouteErrorResponse(error)
    ? `${error.status} ${error.statusText}`
    : error instanceof Error
      ? error.message
      : 'Unknown error'

  return (
    <main className="mx-auto max-w-md space-y-2 px-4 py-16 text-center">
      <h1 className="font-heading text-2xl font-semibold">Something went wrong</h1>
      <p className="text-muted-foreground text-sm">{message}</p>
      <Link to="/" className="text-sm underline underline-offset-4">
        Back to the start page
      </Link>
    </main>
  )
}
