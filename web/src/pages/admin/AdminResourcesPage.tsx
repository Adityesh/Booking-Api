import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router'
import { toast } from 'sonner'
import { client } from '@/api/client'
import { allResourcesQuery, resourcesQuery } from '@/api/queries'
import type { components } from '@/api/schema'
import { ResourceFormDialog } from '@/components/admin/ResourceFormDialog'
import { ConfirmDialog } from '@/components/ConfirmDialog'
import { ErrorAlert } from '@/components/ErrorAlert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'

type ResourceResponse = components['schemas']['ResourceResponseDto']

// Active first, then by name.
function byStatusThenName(a: ResourceResponse, b: ResourceResponse) {
  return Number(b.isActive) - Number(a.isActive) || a.name.localeCompare(b.name)
}

export function AdminResourcesPage() {
  const { data, error, isPending } = useQuery(allResourcesQuery())

  return (
    <section className="space-y-6">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div className="space-y-1">
          <h1 className="font-heading text-2xl font-semibold">Manage resources</h1>
          <p className="text-muted-foreground">All resources, including inactive ones.</p>
        </div>
        <ResourceFormDialog trigger={<Button>New resource</Button>} />
      </div>

      {isPending && <p className="text-muted-foreground">Loading…</p>}
      {error && <ErrorAlert>{error.message}</ErrorAlert>}
      {data?.length === 0 && <p className="text-muted-foreground">No resources yet. Create the first one.</p>}
      {data && data.length > 0 && (
        <div className="rounded-lg border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Name</TableHead>
                <TableHead>Type</TableHead>
                <TableHead className="text-right">Capacity</TableHead>
                <TableHead>Status</TableHead>
                <TableHead>
                  <span className="sr-only">Actions</span>
                </TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.toSorted(byStatusThenName).map((resource) => (
                <TableRow key={resource.id} className={resource.isActive ? undefined : 'text-muted-foreground'}>
                  <TableCell className="font-medium">
                    <Link to={`/resources/${resource.id}`} className="hover:underline">
                      {resource.name}
                    </Link>
                  </TableCell>
                  <TableCell>{resource.type}</TableCell>
                  <TableCell className="text-right tabular-nums">{resource.capacity}</TableCell>
                  <TableCell>
                    <Badge variant={resource.isActive ? 'outline' : 'secondary'}>
                      {resource.isActive ? 'Active' : 'Inactive'}
                    </Badge>
                  </TableCell>
                  <TableCell>
                    <div className="flex justify-end gap-2">
                      <ResourceFormDialog
                        resource={resource}
                        trigger={
                          <Button variant="outline" size="sm">
                            Edit
                          </Button>
                        }
                      />
                      {resource.isActive && <DeactivateButton resource={resource} />}
                    </div>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </section>
  )
}

function DeactivateButton({ resource }: { resource: ResourceResponse }) {
  const queryClient = useQueryClient()
  const deactivate = useMutation({
    mutationFn: () => client.DELETE('/api/Resource/{id}', { params: { path: { id: resource.id } } }),
    onSuccess: () => toast.success(`Deactivated ${resource.name}.`),
    onError: (error) => toast.error(error.message),
    onSettled: () => queryClient.invalidateQueries({ queryKey: resourcesQuery().queryKey }),
  })

  return (
    <ConfirmDialog
      size="sm"
      trigger={deactivate.isPending ? 'Deactivating…' : 'Deactivate'}
      title={`Deactivate ${resource.name}?`}
      description="Users can no longer see or book it. Its history is kept: confirmed bookings stay as they are, and waitlist requests are no longer promoted and expire. There is no way to reactivate it yet."
      confirmLabel="Deactivate"
      onConfirm={() => deactivate.mutate()}
      disabled={deactivate.isPending}
    />
  )
}
