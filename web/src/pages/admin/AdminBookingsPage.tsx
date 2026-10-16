import { PlaceholderPage } from '@/components/PlaceholderPage'

export function AdminBookingsPage() {
  return (
    <PlaceholderPage
      title="All bookings"
      description="Every booking, paged and filterable; an admin can cancel any of them, without the 2-hour rule."
      endpoints={['GET /api/Booking (to be built)', 'POST /api/Booking/{id}/cancel']}
      blockedBy="there is no endpoint that lists all bookings"
    />
  )
}
