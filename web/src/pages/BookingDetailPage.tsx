import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { Link, useParams } from 'react-router'
import { toast } from 'sonner'
import { client } from '@/api/client'
import { bookingQuery } from '@/api/queries'
import type { BookingResponse } from '@/api/bookings'
import { useAuth } from '@/auth/authContext'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { ErrorAlert } from '@/components/ErrorAlert'
import { NotFoundMessage } from '@/components/NotFoundMessage'
import { ResourceName } from '@/components/ResourceName'
import { StatusBadge } from '@/components/StatusBadge'
import { Card, CardContent, CardFooter, CardHeader, CardTitle } from '@/components/ui/card'
import { formatDateTimeRange } from '@/lib/datetime'
import { parseId } from '@/lib/params'

const CANCELLATION_WINDOW_MS = 2 * 60 * 60 * 1000

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

function CancelBooking({ booking }: { booking: BookingResponse }) {
  const { user } = useAuth()
  const queryClient = useQueryClient()
  const isAdmin = user?.role === 'Admin'
  // Mirrors the API's 2-hour rule so users aren't offered a button that is bound to fail. The clock is read once, when
  // this mounts (render must stay pure); the API remains the judge, and its 409 is shown if time moved on meanwhile.
  // Admins are exempt.
  const [mountedAt] = useState(() => Date.now())
  const tooLate = !isAdmin && new Date(booking.startTime).getTime() - mountedAt < CANCELLATION_WINDOW_MS

  const cancel = useMutation({
    mutationFn: () => client.POST('/api/Booking/{id}/cancel', { params: { path: { id: booking.id } } }),
    onSuccess: () => toast.success('Booking cancelled.'),
    onError: (error) => toast.error(error.message),
    // Either way, show what the server now has.
    onSettled: () => queryClient.invalidateQueries({ queryKey: bookingQuery(booking.id).queryKey }),
  })

  return (
    <div className="space-y-2">
      <ConfirmDialog
        trigger={cancel.isPending ? 'Cancelling…' : 'Cancel booking'}
        title="Cancel this booking?"
        description="This can't be undone. If someone is on the waitlist for this time, the place goes to them."
        confirmLabel="Cancel booking"
        onConfirm={() => cancel.mutate()}
        disabled={tooLate || cancel.isPending}
      />
      <p className="text-muted-foreground text-sm">
        {tooLate
          ? 'This booking starts in less than 2 hours, so it can no longer be cancelled.'
          : isAdmin
            ? 'As an admin you can cancel at any time.'
            : 'Bookings can be cancelled up to 2 hours before they start.'}
      </p>
    </div>
  )
}
