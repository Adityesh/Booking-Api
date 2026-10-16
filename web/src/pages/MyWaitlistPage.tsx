import { PlaceholderPage } from '@/components/PlaceholderPage'

export function MyWaitlistPage() {
  return (
    <PlaceholderPage
      title="My waitlist"
      description="Requests that were added to the waitlist because the resource was full. Waiting entries can be withdrawn."
      endpoints={['GET /api/Waitlist/me', 'POST /api/Waitlist/{id}/withdraw']}
    />
  )
}
