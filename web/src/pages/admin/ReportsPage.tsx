import { PlaceholderPage } from '@/components/PlaceholderPage'

export function ReportsPage() {
  return (
    <PlaceholderPage
      title="Reports"
      description="One date window (from, to in the URL) for all three reports; `top` only applies to the most-booked list. Bookings per day is zero-filled."
      endpoints={[
        'GET /api/Reports/most-booked-resources?from&to&top',
        'GET /api/Reports/average-booking-duration?from&to',
        'GET /api/Reports/bookings-per-day?from&to',
      ]}
    />
  )
}
