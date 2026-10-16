import type { AuditLogQuery } from '@/api/queries'
import type { components } from '@/api/schema'
import { localDateToUtcIso } from '@/lib/datetime'
import { parseId } from '@/lib/params'

export type ActionType = components['schemas']['ActionType']

export const actionTypes: ActionType[] = [
  'BookingCreated',
  'BookingCancelled',
  'WaitlistEntryCreated',
  'WaitlistEntryPromoted',
  'WaitlistEntryWithdrawn',
  'WaitlistEntryExpired',
  'ResourceCreated',
  'ResourceUpdated',
  'ResourceDeleted',
]

export const pageSizes = [20, 50, 100] as const
type PageSize = (typeof pageSizes)[number]

// The audit log's state lives in the URL (?action=…&from=…&page=2): a filtered view is a shareable link and the back
// button works. Dates are calendar days ("YYYY-MM-DD") in the viewer's time zone.
export type AuditLogFilters = {
  action: ActionType | null
  userId: number | null
  systemOnly: boolean
  from: string | null
  to: string | null
  page: number
  pageSize: PageSize
}

const isDate = (value: string | null): value is string =>
  value !== null && /^\d{4}-\d{2}-\d{2}$/.test(value) && !Number.isNaN(new Date(value).getTime())

// Hand-edited or stale URLs: anything unrecognised falls back to "no filter" instead of a 400 from the API.
export function readFilters(params: URLSearchParams): AuditLogFilters {
  const action = params.get('action')
  const pageSize = Number(params.get('pageSize'))
  return {
    action: actionTypes.includes(action as ActionType) ? (action as ActionType) : null,
    userId: parseId(params.get('userId') ?? undefined),
    systemOnly: params.get('systemOnly') === 'true',
    from: isDate(params.get('from')) ? params.get('from') : null,
    to: isDate(params.get('to')) ? params.get('to') : null,
    page: parseId(params.get('page') ?? undefined) ?? 1,
    pageSize: pageSizes.includes(pageSize as PageSize) ? (pageSize as PageSize) : 20,
  }
}

// Defaults are left out, so an unfiltered first page is just "/admin/audit-log".
export function writeFilters(filters: AuditLogFilters): URLSearchParams {
  const params = new URLSearchParams()
  if (filters.action) params.set('action', filters.action)
  if (filters.userId) params.set('userId', String(filters.userId))
  if (filters.systemOnly) params.set('systemOnly', 'true')
  if (filters.from) params.set('from', filters.from)
  if (filters.to) params.set('to', filters.to)
  if (filters.page > 1) params.set('page', String(filters.page))
  if (filters.pageSize !== 20) params.set('pageSize', String(filters.pageSize))
  return params
}

// The API's window is half-open (>= From, < To). "To 16 Oct" in the UI means "through the end of the 16th", so To is
// sent as local midnight of the NEXT day.
export function toApiQuery(filters: AuditLogFilters): AuditLogQuery {
  return {
    Action: filters.action ?? undefined,
    UserId: filters.userId ?? undefined,
    SystemOnly: filters.systemOnly || undefined,
    From: filters.from ? localDateToUtcIso(filters.from) : undefined,
    To: filters.to ? localDateToUtcIso(filters.to, 1) : undefined,
    Page: filters.page,
    PageSize: filters.pageSize,
  }
}

// "WaitlistEntryPromoted" → "Waitlist entry promoted"
export function actionLabel(action: ActionType): string {
  const words = action.replace(/([a-z])([A-Z])/g, '$1 $2')
  return words[0] + words.slice(1).toLowerCase()
}

// Bookings and resources have pages of their own; waitlist entries don't.
export function entityLink(action: ActionType, entityId: number): { label: string; to: string | null } {
  if (action.startsWith('Booking')) return { label: `Booking #${entityId}`, to: `/bookings/${entityId}` }
  if (action.startsWith('Resource')) return { label: `Resource #${entityId}`, to: `/resources/${entityId}` }
  return { label: `Waitlist entry #${entityId}`, to: null }
}
