import { useParams } from 'react-router'
import { PlaceholderPage } from '@/components/PlaceholderPage'

export function ResourceDetailPage() {
  const { id } = useParams()
  return (
    <PlaceholderPage
      title={`Resource ${id}`}
      description="Details and a booking form. 201 → go to the new booking; 202 → added to the waitlist, go to /waitlist; 409 → explain why."
      endpoints={['GET /api/Resource/{id}', 'POST /api/Booking']}
      blockedBy="no availability endpoint yet (optional: the form works without it, but can't show “2 of 3 free”)"
    />
  )
}
