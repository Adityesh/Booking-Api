import { Link } from 'react-router'

export function NotFoundPage() {
  return (
    <main className="mx-auto max-w-md space-y-2 px-4 py-16 text-center">
      <h1 className="font-heading text-2xl font-semibold">Page not found</h1>
      <p className="text-muted-foreground">There is nothing at this address.</p>
      <Link to="/" className="text-sm underline underline-offset-4">
        Back to the start page
      </Link>
    </main>
  )
}
