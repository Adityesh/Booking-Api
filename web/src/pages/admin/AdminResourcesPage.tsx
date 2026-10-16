import { PlaceholderPage } from '@/components/PlaceholderPage'

export function AdminResourcesPage() {
  return (
    <PlaceholderPage
      title="Manage resources"
      description="All resources, including inactive ones. Create and edit in dialogs; Deactivate is a soft delete (history is kept)."
      endpoints={[
        'GET /api/Resource/all',
        'POST /api/Resource',
        'PUT /api/Resource/{id}',
        'DELETE /api/Resource/{id}',
      ]}
    />
  )
}
