import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router'
import { bookingStatuses, type BookingListItem, type BookingStatus } from '@/api/bookings'
import { myBookingsQuery } from '@/api/queries'
import { CancelBooking } from '@/components/CancelBooking'
import { ErrorAlert } from '@/components/ErrorAlert'
import { Pagination } from '@/components/Pagination'
import { StatusBadge } from '@/components/StatusBadge'
import { Button } from '@/components/ui/button'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatDateTimeRange } from '@/lib/datetime'
import {
  readMyBookingsFilters,
  timeFrameLabels,
  timeFrames,
  toMyBookingsQuery,
  writeMyBookingsFilters,
  type MyBookingsFilters,
} from './myBookingsFilters'

// Radix Select can't use "" as an item value, so "no status filter" needs a sentinel.
const ALL_STATUSES = 'all'

export function MyBookingsPage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const filters = readMyBookingsFilters(searchParams)
  const link = (next: Partial<MyBookingsFilters>) => writeMyBookingsFilters({ ...filters, ...next }).toString()
  const update = (next: Partial<MyBookingsFilters>) => setSearchParams(link(next))

  const { data, error, isPending, isPlaceholderData } = useQuery({
    ...myBookingsQuery(toMyBookingsQuery(filters)),
    placeholderData: keepPreviousData,
  })

  return (
    <section className="space-y-6">
      <div className="space-y-1">
        <h1 className="font-heading text-2xl font-semibold">My bookings</h1>
        <p className="text-muted-foreground">
          {filters.when === 'past' ? 'Finished bookings, most recent first.' : filters.when === 'all'
            ? 'Everything you have booked, newest first.'
            : 'Bookings that haven’t finished yet, soonest first.'}
        </p>
      </div>

      <div className="flex flex-wrap items-center justify-between gap-4">
        {/* Links, not buttons: each tab is a URL, so it can be opened in a new tab or bookmarked. */}
        <nav aria-label="Time frame" className="bg-muted inline-flex rounded-lg p-1">
          {timeFrames.map((when) => (
            <Button key={when} asChild size="sm" variant={filters.when === when ? 'outline' : 'ghost'}>
              <Link
                to={{ search: link({ when, page: 1 }) }}
                aria-current={filters.when === when ? 'page' : undefined}
                className={filters.when === when ? 'bg-background' : 'text-muted-foreground'}
              >
                {timeFrameLabels[when]}
              </Link>
            </Button>
          ))}
        </nav>
        <Select
          value={filters.status ?? ALL_STATUSES}
          onValueChange={(value) =>
            update({ status: value === ALL_STATUSES ? null : (value as BookingStatus), page: 1 })
          }
        >
          <SelectTrigger size="sm" aria-label="Status" className="w-40">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value={ALL_STATUSES}>All statuses</SelectItem>
            {bookingStatuses.map((status) => (
              <SelectItem key={status} value={status}>
                {status}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      {isPending && <p className="text-muted-foreground">Loading…</p>}
      {error && <ErrorAlert>{error.message}</ErrorAlert>}

      {data && data.data.length === 0 && (
        <p className="text-muted-foreground">
          {data.count > 0 ? (
            <>
              This page is past the end.{' '}
              <button className="underline underline-offset-4" onClick={() => update({ page: 1 })}>
                Go to page 1
              </button>
            </>
          ) : (
            <EmptyMessage filters={filters} />
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
                  <TableHead>Status</TableHead>
                  <TableHead className="text-right">
                    <span className="sr-only">Actions</span>
                  </TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {data.data.map((booking) => (
                  <BookingRow key={booking.id} booking={booking} />
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

function BookingRow({ booking }: { booking: BookingListItem }) {
  const when = formatDateTimeRange(booking.startTime, booking.endTime)
  return (
    <TableRow>
      <TableCell>
        <Link to={`/bookings/${booking.id}`} className="hover:underline">
          {when}
        </Link>
      </TableCell>
      {/* Plain text, not a link: the resource may have been retired since, and a user gets 404 for those. */}
      <TableCell>{booking.resourceName}</TableCell>
      <TableCell>
        <StatusBadge status={booking.status} />
      </TableCell>
      <TableCell className="text-right">
        {booking.status === 'Confirmed' && (
          <CancelBooking booking={booking} variant="compact" summary={`${booking.resourceName}, ${when}`} />
        )}
      </TableCell>
    </TableRow>
  )
}

function EmptyMessage({ filters }: { filters: MyBookingsFilters }) {
  if (filters.status) {
    const frame = filters.when === 'all' ? '' : `${filters.when} `
    return <>No {filters.status.toLowerCase()} {frame}bookings.</>
  }
  if (filters.when === 'past') return <>No finished bookings yet.</>
  return (
    <>
      {filters.when === 'upcoming' ? 'Nothing coming up.' : "You haven't booked anything yet."}{' '}
      <Link to="/resources" className="underline underline-offset-4">
        Browse resources
      </Link>
    </>
  )
}
