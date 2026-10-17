import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router'
import { bookingStatuses, type BookingListItem, type BookingStatus } from '@/api/bookings'
import { adminBookingsQuery, allResourcesQuery } from '@/api/queries'
import { CancelBooking } from '@/components/CancelBooking'
import { ErrorAlert } from '@/components/ErrorAlert'
import { Pagination } from '@/components/Pagination'
import { StatusBadge } from '@/components/StatusBadge'
import { Button } from '@/components/ui/button'
import { Field, FieldDescription, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatDateTimeRange, localTimeZone } from '@/lib/datetime'
import { parseId } from '@/lib/params'
import {
  readAdminBookingFilters,
  toAdminBookingsQuery,
  writeAdminBookingFilters,
  type AdminBookingFilters,
} from './adminBookingFilters'

// Radix Select can't use "" as an item value, so "no filter" needs a sentinel.
const ANY = 'any'

export function AdminBookingsPage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const filters = readAdminBookingFilters(searchParams)
  const link = (next: Partial<AdminBookingFilters>) => writeAdminBookingFilters({ ...filters, ...next }).toString()
  const update = (next: Partial<AdminBookingFilters>) => setSearchParams(link(next))

  const { data, error, isPending, isPlaceholderData } = useQuery({
    ...adminBookingsQuery(toAdminBookingsQuery(filters)),
    placeholderData: keepPreviousData,
  })

  const isFiltered = Boolean(filters.userId || filters.resourceId || filters.status || filters.from || filters.to)

  return (
    <section className="space-y-6">
      <div className="space-y-1">
        <h1 className="font-heading text-2xl font-semibold">All bookings</h1>
        <p className="text-muted-foreground">
          Every user's bookings, latest start first. As an admin you can cancel any of them, at any time.
        </p>
      </div>

      {/* Keyed on the URL, so back/forward (or a clicked user/resource) resets the inputs to what the URL says. */}
      <FilterForm
        key={searchParams.toString()}
        filters={filters}
        onApply={(next) => update({ ...next, page: 1 })}
        onReset={() => setSearchParams({})}
      />

      {isPending && <p className="text-muted-foreground">Loading…</p>}
      {/* A 400 can only come from a hand-edited URL; show the API's reasons. */}
      {error && (
        <ErrorAlert>
          {error.kind === 'validation' ? Object.values(error.fieldErrors).flat().join(' ') : error.message}
        </ErrorAlert>
      )}

      {data && data.data.length === 0 && (
        <p className="text-muted-foreground">
          {data.count > 0 ? (
            <>
              This page is past the end.{' '}
              <button className="underline underline-offset-4" onClick={() => update({ page: 1 })}>
                Go to page 1
              </button>
            </>
          ) : isFiltered ? (
            'No bookings match these filters.'
          ) : (
            'No bookings yet.'
          )}
        </p>
      )}

      {data && data.data.length > 0 && (
        <div className={isPlaceholderData ? 'opacity-60 transition-opacity' : undefined}>
          <div className="rounded-lg border">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>When</TableHead>
                  <TableHead>Resource</TableHead>
                  <TableHead>User</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead className="text-right">
                    <span className="sr-only">Actions</span>
                  </TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {data.data.map((booking) => (
                  <BookingRow key={booking.id} booking={booking} filterLink={link} />
                ))}
              </TableBody>
            </Table>
          </div>
          <Pagination
            page={data.page}
            pageSize={filters.pageSize}
            count={data.count}
            shown={data.data.length}
            busy={isPlaceholderData}
            onPage={(page) => update({ page })}
            onPageSize={(pageSize) => update({ pageSize, page: 1 })}
          />
        </div>
      )}
    </section>
  )
}

type BookingRowProps = {
  booking: BookingListItem
  filterLink: (next: Partial<AdminBookingFilters>) => string
}

function BookingRow({ booking, filterLink }: BookingRowProps) {
  const when = formatDateTimeRange(booking.startTime, booking.endTime)
  return (
    <TableRow>
      <TableCell>
        <Link to={`/bookings/${booking.id}`} className="hover:underline">
          {when}
        </Link>
      </TableCell>
      {/* Clicking a resource or a user narrows the list to them, keeping the other filters. */}
      <TableCell>
        <Link
          to={{ search: filterLink({ resourceId: booking.resourceId, page: 1 }) }}
          className="hover:underline"
          title="Show only this resource's bookings"
        >
          {booking.resourceName}
        </Link>
      </TableCell>
      <TableCell>
        <Link
          to={{ search: filterLink({ userId: booking.userId, page: 1 }) }}
          className="hover:underline"
          title="Show only this user's bookings"
        >
          {booking.username}
        </Link>
      </TableCell>
      <TableCell>
        <StatusBadge status={booking.status} />
      </TableCell>
      <TableCell className="text-right">
        {booking.status === 'Confirmed' && (
          <CancelBooking
            booking={booking}
            variant="compact"
            summary={`${booking.resourceName} for ${booking.username}, ${when}`}
          />
        )}
      </TableCell>
    </TableRow>
  )
}

type FilterFormProps = {
  filters: AdminBookingFilters
  onApply: (filters: Pick<AdminBookingFilters, 'userId' | 'resourceId' | 'status' | 'from' | 'to'>) => void
  onReset: () => void
}

function FilterForm({ filters, onApply, onReset }: FilterFormProps) {
  const [inputError, setInputError] = useState<string | null>(null)
  const resources = useQuery(allResourcesQuery())

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const rawUserId = String(form.get('userId') ?? '').trim()
    const userId = parseId(rawUserId)
    const resource = String(form.get('resourceId'))
    const status = String(form.get('status'))
    const from = String(form.get('from') ?? '') || null
    const to = String(form.get('to') ?? '') || null

    // The same rules as the API's GetAdminBookingValidator, checked here so a bad combination never reaches the URL.
    if (rawUserId && userId === null) return setInputError('User id must be a positive whole number.')
    if (from && to && to < from) return setInputError('"From" must be on or before "To".')
    setInputError(null)

    onApply({
      userId,
      resourceId: resource === ANY ? null : parseId(resource),
      status: status === ANY ? null : (status as BookingStatus),
      from,
      to,
    })
  }

  // A resourceId from the URL that isn't in the list (still loading, or a stale link) still needs an item to show.
  const selectedMissing =
    filters.resourceId !== null && !resources.data?.some((resource) => resource.id === filters.resourceId)

  return (
    <form onSubmit={handleSubmit} className="space-y-4 rounded-lg border p-4">
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-5">
        <Field>
          <FieldLabel htmlFor="resourceId">Resource</FieldLabel>
          <Select name="resourceId" defaultValue={filters.resourceId ? String(filters.resourceId) : ANY}>
            <SelectTrigger id="resourceId" className="w-full">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ANY}>All resources</SelectItem>
              {selectedMissing && (
                <SelectItem value={String(filters.resourceId)}>Resource #{filters.resourceId}</SelectItem>
              )}
              {resources.data?.map((resource) => (
                <SelectItem key={resource.id} value={String(resource.id)}>
                  {resource.name}
                  {!resource.isActive && ' (inactive)'}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </Field>
        <Field>
          <FieldLabel htmlFor="status">Status</FieldLabel>
          <Select name="status" defaultValue={filters.status ?? ANY}>
            <SelectTrigger id="status" className="w-full">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ANY}>All statuses</SelectItem>
              {bookingStatuses.map((status) => (
                <SelectItem key={status} value={status}>
                  {status}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </Field>
        <Field>
          <FieldLabel htmlFor="userId">User id</FieldLabel>
          <Input id="userId" name="userId" inputMode="numeric" defaultValue={filters.userId ?? ''} placeholder="Any" />
        </Field>
        <Field>
          <FieldLabel htmlFor="from">From</FieldLabel>
          <Input id="from" name="from" type="date" defaultValue={filters.from ?? ''} />
        </Field>
        <Field>
          <FieldLabel htmlFor="to">To</FieldLabel>
          <Input id="to" name="to" type="date" defaultValue={filters.to ?? ''} />
        </Field>
      </div>
      <FieldDescription>
        Dates filter on the day a booking starts, in your time zone ({localTimeZone}), both ends included.
      </FieldDescription>

      {inputError && <ErrorAlert>{inputError}</ErrorAlert>}

      <div className="flex gap-2">
        <Button type="submit">Apply filters</Button>
        <Button type="button" variant="outline" onClick={onReset}>
          Reset
        </Button>
      </div>
    </form>
  )
}
