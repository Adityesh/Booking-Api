import { Link } from 'react-router'

// In-page "not found". The API answers 404 both for "doesn't exist" and "not yours / inactive", so the UI never
// claims more than "not found".
export function NotFoundMessage({ title, backTo, backLabel }: { title: string; backTo: string; backLabel: string }) {
  return (
    <div className="space-y-2">
      <h1 className="font-heading text-2xl font-semibold">{title}</h1>
      <p className="text-muted-foreground">It doesn't exist, or you don't have access to it.</p>
      <Link to={backTo} className="text-sm underline underline-offset-4">
        {backLabel}
      </Link>
    </div>
  )
}
