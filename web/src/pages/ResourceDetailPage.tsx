import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { resourceQuery } from '@/api/queries'
import { BookingForm } from '@/components/BookingForm'
import { ErrorAlert } from '@/components/ErrorAlert'
import { NotFoundMessage } from '@/components/NotFoundMessage'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { parseId } from '@/lib/params'

const notFound = <NotFoundMessage title="Resource not found" backTo="/resources" backLabel="Back to resources" />

export function ResourceDetailPage() {
  const id = parseId(useParams().id)
  return id === null ? notFound : <ResourceDetail id={id} />
}

function ResourceDetail({ id }: { id: number }) {
  const { data: resource, error, isPending } = useQuery(resourceQuery(id))

  if (isPending) return <p className="text-muted-foreground">Loading…</p>
  if (error) return error.kind === 'notFound' ? notFound : <ErrorAlert>{error.message}</ErrorAlert>

  return (
    <div className="space-y-6">
      <Link to="/resources" className="text-muted-foreground hover:text-foreground text-sm">
        ← Resources
      </Link>
      <div className="space-y-2">
        <h1 className="font-heading text-2xl font-semibold">{resource.name}</h1>
        <div className="flex flex-wrap gap-2">
          <Badge variant="outline">{resource.type}</Badge>
          <Badge variant="outline">Capacity {resource.capacity}</Badge>
          {/* Only admins can load an inactive resource; users get a 404 above. */}
          {!resource.isActive && <Badge variant="secondary">Inactive</Badge>}
        </div>
      </div>

      <Card>
        <CardHeader>
          <CardTitle>Book this {resource.type === 'Room' ? 'room' : 'equipment'}</CardTitle>
          <CardDescription>
            Up to {resource.capacity} {resource.capacity === 1 ? 'booking' : 'bookings'} can overlap at the same time.
          </CardDescription>
        </CardHeader>
        <CardContent>
          {resource.isActive ? (
            <BookingForm resourceId={resource.id} />
          ) : (
            <p className="text-muted-foreground text-sm">Inactive resources can't be booked.</p>
          )}
        </CardContent>
      </Card>
    </div>
  )
}
