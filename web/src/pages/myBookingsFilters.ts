import { bookingStatuses, type BookingStatus } from '@/api/bookings'
import type { MyBookingsQuery } from '@/api/queries'
import { readPaging, writePaging, type Paging } from '@/lib/paging'

// The tabs. Upcoming = not finished yet (in progress counts), Past = finished; the API splits on EndTime.
export const timeFrames = ['upcoming', 'past', 'all'] as const
export type TimeFrame = (typeof timeFrames)[number]

export const timeFrameLabels: Record<TimeFrame, string> = { upcoming: 'Upcoming', past: 'Past', all: 'All' }

// State lives in the URL (?when=past&status=Cancelled&page=2), so the back button and links work.
export type MyBookingsFilters = Paging & {
  when: TimeFrame
  status: BookingStatus | null
}

export function readMyBookingsFilters(params: URLSearchParams): MyBookingsFilters {
  const when = params.get('when')
  const status = params.get('status')
  return {
    when: timeFrames.includes(when as TimeFrame) ? (when as TimeFrame) : 'upcoming',
    status: bookingStatuses.includes(status as BookingStatus) ? (status as BookingStatus) : null,
    ...readPaging(params),
  }
}

// Defaults are left out, so the first page of upcoming bookings is just "/bookings".
export function writeMyBookingsFilters(filters: MyBookingsFilters): URLSearchParams {
  const params = new URLSearchParams()
  if (filters.when !== 'upcoming') params.set('when', filters.when)
  if (filters.status) params.set('status', filters.status)
  return writePaging(params, filters)
}

export function toMyBookingsQuery(filters: MyBookingsFilters): MyBookingsQuery {
  return {
    When: filters.when === 'upcoming' ? 'Upcoming' : filters.when === 'past' ? 'Past' : undefined,
    Status: filters.status ?? undefined,
    Page: filters.page,
    PageSize: filters.pageSize,
  }
}
