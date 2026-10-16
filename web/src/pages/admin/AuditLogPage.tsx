import { PlaceholderPage } from '@/components/PlaceholderPage'

export function AuditLogPage() {
  return (
    <PlaceholderPage
      title="Audit log"
      description="Filters (action, user id, from, to, system only) and paging live in the URL search params, so a filtered view is a shareable link."
      endpoints={['GET /api/AuditLog?action&userId&from&to&systemOnly&page&pageSize']}
    />
  )
}
