import { bookingStatuses, type BookingStatus } from '@/api/bookings'
import type { AdminBookingsQuery } from '@/api/queries'
import { localDateToUtcIso } from '@/lib/datetime'
import { readPaging, writePaging, type Paging } from '@/lib/paging'
import { parseId } from '@/lib/params'

// State lives in the URL (?userId=…&resourceId=…&from=…), so a filtered view is a shareable link.
// Dates are calendar days ("YYYY-MM-DD") in the viewer's time zone and filter on the day a booking STARTS.
export type AdminBookingFilters = Paging & {
  userId: number | null
  resourceId: number | null
  status: BookingStatus | null
  from: string | null
  to: string | null
}

const isDate = (value: string | null): value is string =>
  value !== null && /^\d{4}-\d{2}-\d{2}$/.test(value) && !Number.isNaN(new Date(value).getTime())

// Hand-edited or stale URLs: anything unrecognised falls back to "no filter" instead of a 400 from the API.
export function readAdminBookingFilters(params: URLSearchParams): AdminBookingFilters {
  const status = params.get('status')
  return {
    userId: parseId(params.get('userId') ?? undefined),
    resourceId: parseId(params.get('resourceId') ?? undefined),
    status: bookingStatuses.includes(status as BookingStatus) ? (status as BookingStatus) : null,
    from: isDate(params.get('from')) ? params.get('from') : null,
    to: isDate(params.get('to')) ? params.get('to') : null,
    ...readPaging(params),
  }
}

// Defaults are left out, so the unfiltered first page is just "/admin/bookings".
export function writeAdminBookingFilters(filters: AdminBookingFilters): URLSearchParams {
  const params = new URLSearchParams()
  if (filters.userId) params.set('userId', String(filters.userId))
  if (filters.resourceId) params.set('resourceId', String(filters.resourceId))
  if (filters.status) params.set('status', filters.status)
  if (filters.from) params.set('from', filters.from)
  if (filters.to) params.set('to', filters.to)
  return writePaging(params, filters)
}

// The API's window is half-open (>= From, < To) on StartTime. "To 16 Oct" means "through the end of the 16th", so To
// is sent as local midnight of the NEXT day.
export function toAdminBookingsQuery(filters: AdminBookingFilters): AdminBookingsQuery {
  return {
    UserId: filters.userId ?? undefined,
    ResourceId: filters.resourceId ?? undefined,
    Status: filters.status ?? undefined,
    From: filters.from ? localDateToUtcIso(filters.from) : undefined,
    To: filters.to ? localDateToUtcIso(filters.to, 1) : undefined,
    Page: filters.page,
    PageSize: filters.pageSize,
  }
}
