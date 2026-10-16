import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent, type ReactNode } from 'react'
import { toast } from 'sonner'
import { client, fieldErrors } from '@/api/client'
import { resourcesQuery } from '@/api/queries'
import type { components } from '@/api/schema'
import { ErrorAlert } from '@/components/ErrorAlert'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from '@/components/ui/dialog'
import { Field, FieldDescription, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { asFieldErrors } from '@/lib/forms'

type ResourceResponse = components['schemas']['ResourceResponseDto']
type ResourceType = components['schemas']['ResourceType']
// Create and Update take the same three fields.
type ResourceRequest = components['schemas']['CreateResourceDto']

const resourceTypes: ResourceType[] = ['Room', 'Equipment']

type ResourceFormDialogProps = {
  // Absent: create a new resource. Present: edit this one.
  resource?: ResourceResponse
  trigger: ReactNode
}

export function ResourceFormDialog({ resource, trigger }: ResourceFormDialogProps) {
  const [open, setOpen] = useState(false)
  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>{trigger}</DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{resource ? `Edit ${resource.name}` : 'New resource'}</DialogTitle>
          <DialogDescription>
            {resource ? 'Changes apply to new bookings straight away.' : 'Users can book it as soon as it is created.'}
          </DialogDescription>
        </DialogHeader>
        {/* DialogContent unmounts when closed, so every opening starts with a fresh form and mutation state. */}
        <ResourceForm resource={resource} onDone={() => setOpen(false)} />
      </DialogContent>
    </Dialog>
  )
}

function ResourceForm({ resource, onDone }: { resource?: ResourceResponse; onDone: () => void }) {
  const queryClient = useQueryClient()
  const save = useMutation({
    mutationFn: async (body: ResourceRequest) => {
      if (resource) await client.PUT('/api/Resource/{id}', { params: { path: { id: resource.id } }, body })
      else await client.POST('/api/Resource', { body })
    },
    onSuccess: (_, body) => {
      toast.success(resource ? `Saved ${body.name}.` : `Created ${body.name}.`)
      onDone()
    },
    // ['resources'] is the prefix of the admin list, the users' list and every single-resource query.
    onSettled: () => queryClient.invalidateQueries({ queryKey: resourcesQuery().queryKey }),
  })

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    save.mutate({
      name: String(form.get('name')),
      type: String(form.get('type')) as ResourceType,
      // The API reads numbers strictly ("3" is rejected), so send a JSON number.
      capacity: Number(form.get('capacity')),
    })
  }

  const { error } = save
  const nameErrors = fieldErrors(error, 'name')
  const typeErrors = fieldErrors(error, 'type')
  const capacityErrors = fieldErrors(error, 'capacity')
  const formError = error && error.kind !== 'validation' ? error.message : null

  return (
    <form onSubmit={handleSubmit} className="space-y-6">
      <FieldGroup>
        <Field data-invalid={nameErrors.length > 0 || undefined}>
          <FieldLabel htmlFor="name">Name</FieldLabel>
          <Input
            id="name"
            name="name"
            defaultValue={resource?.name}
            autoComplete="off"
            required
            aria-invalid={nameErrors.length > 0 || undefined}
          />
          <FieldError errors={asFieldErrors(nameErrors)} />
        </Field>
        <div className="grid gap-6 sm:grid-cols-2">
          <Field data-invalid={typeErrors.length > 0 || undefined}>
            <FieldLabel htmlFor="type">Type</FieldLabel>
            {/* With `name`, Radix renders a hidden native <select>, so the value reaches FormData. */}
            <Select name="type" defaultValue={resource?.type ?? 'Room'}>
              <SelectTrigger id="type" className="w-full" aria-invalid={typeErrors.length > 0 || undefined}>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {resourceTypes.map((type) => (
                  <SelectItem key={type} value={type}>
                    {type}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <FieldError errors={asFieldErrors(typeErrors)} />
          </Field>
          <Field data-invalid={capacityErrors.length > 0 || undefined}>
            <FieldLabel htmlFor="capacity">Capacity</FieldLabel>
            <Input
              id="capacity"
              name="capacity"
              type="number"
              inputMode="numeric"
              min={1}
              step={1}
              defaultValue={resource?.capacity ?? 1}
              required
              aria-invalid={capacityErrors.length > 0 || undefined}
            />
            <FieldError errors={asFieldErrors(capacityErrors)} />
          </Field>
        </div>
        <FieldDescription>
          Capacity is how many bookings may overlap at once.
          {resource && ' Lowering it keeps existing bookings; new ones wait until the overlap drops below the new value.'}
        </FieldDescription>
      </FieldGroup>

      {formError && <ErrorAlert>{formError}</ErrorAlert>}

      <DialogFooter>
        <DialogClose asChild>
          <Button type="button" variant="outline">
            Cancel
          </Button>
        </DialogClose>
        <Button type="submit" disabled={save.isPending}>
          {save.isPending ? 'Saving…' : resource ? 'Save' : 'Create'}
        </Button>
      </DialogFooter>
    </form>
  )
}
