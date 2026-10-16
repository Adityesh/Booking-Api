import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router'
import { toast } from 'sonner'
import { createBooking } from '@/api/bookings'
import { fieldErrors } from '@/api/client'
import { bookingQuery, myWaitlistQuery } from '@/api/queries'
import { ErrorAlert } from '@/components/ErrorAlert'
import { Button } from '@/components/ui/button'
import { Field, FieldDescription, FieldError, FieldGroup, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { dateTimeLocalToUtcIso, localTimeZone, toDateTimeLocalValue } from '@/lib/datetime'
import { asFieldErrors } from '@/lib/forms'

// Next full hour, one hour long: a sensible default that is already in the future.
function defaultSlot() {
  const start = new Date()
  start.setHours(start.getHours() + 1, 0, 0, 0)
  const end = new Date(start)
  end.setHours(end.getHours() + 1)
  return { start: toDateTimeLocalValue(start), end: toDateTimeLocalValue(end), now: toDateTimeLocalValue(new Date()) }
}

export function BookingForm({ resourceId }: { resourceId: number }) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [slot] = useState(defaultSlot)
  const [inputError, setInputError] = useState<string | null>(null)

  const book = useMutation({
    mutationFn: createBooking,
    onSuccess: (outcome) => {
      if (outcome.kind === 'booked') {
        // Seed the detail page's cache with the 201 body, so it renders without another request.
        queryClient.setQueryData(bookingQuery(outcome.booking.id).queryKey, outcome.booking)
        toast.success('Booked.')
        navigate(`/bookings/${outcome.booking.id}`)
      } else {
        void queryClient.invalidateQueries({ queryKey: myWaitlistQuery().queryKey })
        toast.info('That time is full, so you joined the waitlist.', {
          description: "If a place frees up before it starts, you'll be booked automatically.",
        })
        navigate('/waitlist')
      }
    },
  })

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const startTime = dateTimeLocalToUtcIso(String(form.get('startTime')))
    const endTime = dateTimeLocalToUtcIso(String(form.get('endTime')))
    if (!startTime || !endTime) {
      setInputError('Enter a valid start and end time.')
      book.reset()
      return
    }
    setInputError(null)
    book.mutate({ resourceId, startTime, endTime })
  }

  const { error } = book
  const startErrors = fieldErrors(error, 'startTime')
  const endErrors = fieldErrors(error, 'endTime')
  // 404 (resource gone or deactivated meanwhile), 409 (already on the waitlist for this), network, 500.
  const formError = inputError ?? (error && error.kind !== 'validation' ? error.message : null)

  return (
    <form onSubmit={handleSubmit} className="space-y-6">
      <FieldGroup>
        <div className="grid gap-6 sm:grid-cols-2">
          <Field data-invalid={startErrors.length > 0 || undefined}>
            <FieldLabel htmlFor="startTime">Start</FieldLabel>
            <Input
              id="startTime"
              name="startTime"
              type="datetime-local"
              defaultValue={slot.start}
              min={slot.now}
              required
              aria-invalid={startErrors.length > 0 || undefined}
            />
            <FieldError errors={asFieldErrors(startErrors)} />
          </Field>
          <Field data-invalid={endErrors.length > 0 || undefined}>
            <FieldLabel htmlFor="endTime">End</FieldLabel>
            <Input
              id="endTime"
              name="endTime"
              type="datetime-local"
              defaultValue={slot.end}
              min={slot.now}
              required
              aria-invalid={endErrors.length > 0 || undefined}
            />
            <FieldError errors={asFieldErrors(endErrors)} />
          </Field>
        </div>
        <FieldDescription>
          Times are in your time zone ({localTimeZone}). If the resource is full at that time, you join the waitlist
          instead and are booked automatically if a place frees up.
        </FieldDescription>
      </FieldGroup>

      {formError && <ErrorAlert>{formError}</ErrorAlert>}

      <Button type="submit" disabled={book.isPending}>
        {book.isPending ? 'Booking…' : 'Book'}
      </Button>
    </form>
  )
}
