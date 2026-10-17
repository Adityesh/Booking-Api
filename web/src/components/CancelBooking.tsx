import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState, type ReactNode } from 'react'
import { toast } from 'sonner'
import { client } from '@/api/client'
import { allBookingsKey } from '@/api/queries'
import { useAuth } from '@/auth/authContext'
import { ConfirmDialog } from '@/components/ConfirmDialog'

const CANCELLATION_WINDOW_MS = 2 * 60 * 60 * 1000

type CancelBookingProps = {
  // A confirmed booking: the detail response or a list row, both have these.
  booking: { id: number; startTime: string; endTime: string }
  // What the dialog asks about, e.g. "Room A, 16 Oct 10:00–11:00". Lists need it: the row isn't visible behind the dialog.
  summary?: ReactNode
  // "full": the button plus the rule that applies to you (detail page). "compact": just the button, or a short reason
  // why there is none (table rows).
  variant?: 'full' | 'compact'
}

export function CancelBooking({ booking, summary, variant = 'full' }: CancelBookingProps) {
  const { user } = useAuth()
  const queryClient = useQueryClient()
  const isAdmin = user?.role === 'Admin'
  // Mirrors the API's 2-hour rule so users aren't offered a button that is bound to fail. The clock is read once, when
  // this mounts (render must stay pure); the API remains the judge, and its 409 is shown if time moved on meanwhile.
  // Admins are exempt.
  const [mountedAt] = useState(() => Date.now())
  const ended = new Date(booking.endTime).getTime() <= mountedAt
  const tooLate = !isAdmin && new Date(booking.startTime).getTime() - mountedAt < CANCELLATION_WINDOW_MS

  const cancel = useMutation({
    mutationFn: () => client.POST('/api/Booking/{id}/cancel', { params: { path: { id: booking.id } } }),
    onSuccess: () => toast.success('Booking cancelled.'),
    onError: (error) => toast.error(error.message),
    // Either way, show what the server now has: this booking's details and every list it appears in. A cancel can also
    // promote a waitlisted request into a new booking, which the admin list should show.
    onSettled: () => queryClient.invalidateQueries({ queryKey: allBookingsKey }),
  })

  const button = (
    <ConfirmDialog
      trigger={cancel.isPending ? 'Cancelling…' : variant === 'compact' ? 'Cancel' : 'Cancel booking'}
      title="Cancel this booking?"
      description={
        <>
          {summary && <span className="text-foreground block font-medium">{summary}</span>}
          This can't be undone. If someone is on the waitlist for this time, the place goes to them.
        </>
      }
      confirmLabel="Cancel booking"
      onConfirm={() => cancel.mutate()}
      disabled={tooLate || cancel.isPending}
      size={variant === 'compact' ? 'sm' : 'default'}
    />
  )

  if (variant === 'compact') {
    // Cancelling something that is over frees nothing; a user inside the 2-hour window can't.
    if (ended) return null
    if (tooLate)
      return (
        <span className="text-muted-foreground text-xs">
          {new Date(booking.startTime).getTime() <= mountedAt ? 'In progress' : 'Starts within 2 h'}
        </span>
      )
    return button
  }

  return (
    <div className="space-y-2">
      {button}
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
