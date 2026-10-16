import { useParams } from 'react-router'
import { PlaceholderPage } from '@/components/PlaceholderPage'

export function BookingDetailPage() {
  const { id } = useParams()
  return (
    <PlaceholderPage
      title={`Booking ${id}`}
      description="Details and Cancel. Users can't cancel within 2 hours of the start (admins can). A 404 means “not found”, which also covers someone else's booking."
      endpoints={['GET /api/Booking/{id}', 'POST /api/Booking/{id}/cancel']}
    />
  )
}
