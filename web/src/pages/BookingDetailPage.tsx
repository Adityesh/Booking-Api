import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { bookingQuery } from '@/api/queries'
import { CancelBooking } from '@/components/CancelBooking'
import { ErrorAlert } from '@/components/ErrorAlert'
import { NotFoundMessage } from '@/components/NotFoundMessage'
import { ResourceName } from '@/components/ResourceName'
import { StatusBadge } from '@/components/StatusBadge'
import { Card, CardContent, CardFooter, CardHeader, CardTitle } from '@/components/ui/card'
import { formatDateTimeRange } from '@/lib/datetime'
import { parseId } from '@/lib/params'

const notFound = <NotFoundMessage title="Booking not found" backTo="/bookings" backLabel="Back to my bookings" />

export function BookingDetailPage() {
  const id = parseId(useParams().id)
  return id === null ? notFound : <BookingDetail id={id} />
}

function BookingDetail({ id }: { id: number }) {
  const { data: booking, error, isPending } = useQuery(bookingQuery(id))

  if (isPending) return <p className="text-muted-foreground">Loading…</p>
  if (error) return error.kind === 'notFound' ? notFound : <ErrorAlert>{error.message}</ErrorAlert>

  return (
    <div className="space-y-6">
      <h1 className="font-heading text-2xl font-semibold">Booking #{booking.id}</h1>
      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-3">
            <Link to={`/resources/${booking.resourceId}`} className="hover:underline">
              <ResourceName id={booking.resourceId} />
            </Link>
            <StatusBadge status={booking.status} />
          </CardTitle>
        </CardHeader>
        <CardContent className="text-sm">{formatDateTimeRange(booking.startTime, booking.endTime)}</CardContent>
        {booking.status === 'Confirmed' && (
          <CardFooter>
            <CancelBooking booking={booking} />
          </CardFooter>
        )}
      </Card>
    </div>
  )
}
