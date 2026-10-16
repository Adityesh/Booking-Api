import { PlaceholderPage } from '@/components/PlaceholderPage'

export function MyBookingsPage() {
  return (
    <PlaceholderPage
      title="My bookings"
      description="Your upcoming and past bookings, paged, with a link to each one."
      endpoints={['GET /api/Booking/me (to be built)']}
      blockedBy="there is no endpoint that lists the current user's bookings"
    />
  )
}
