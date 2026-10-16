import type { ReportQuery } from '@/api/queries'
import { utcDateString, utcDateToIso, utcDayCount } from '@/lib/datetime'

export const topOptions = [5, 10, 20, 50] as const
export const MAX_DAYS = 366

// ?from=2026-10-01&to=2026-10-31&top=10. Dates are UTC calendar days, because bookings-per-day groups by the UTC day
// a booking starts: local-day boundaries would add a partial day at each end with misleading labels.
export type ReportFilters = { from: string; to: string; top: number }

const isDate = (value: string | null): value is string =>
  value !== null && /^\d{4}-\d{2}-\d{2}$/.test(value) && !Number.isNaN(Date.parse(value))

// Without (valid) dates in the URL, show the last 30 days, today included. Top: 1–50 like the API, default 10.
export function readReportFilters(params: URLSearchParams): ReportFilters {
  const from = params.get('from')
  const to = params.get('to')
  const top = Number(params.get('top'))
  const hasRange = isDate(from) && isDate(to)
  return {
    from: hasRange ? from : utcDateString(-29),
    to: hasRange ? to : utcDateString(),
    top: Number.isInteger(top) && top >= 1 && top <= 50 ? top : 10,
  }
}

export function writeReportFilters(filters: ReportFilters): URLSearchParams {
  const params = new URLSearchParams({ from: filters.from, to: filters.to })
  if (filters.top !== 10) params.set('top', String(filters.top))
  return params
}

// The same rules as ReportDtoValidator, so an invalid window is explained here instead of failing three requests.
export function rangeError(from: string, to: string): string | null {
  if (to < from) return '"From" must be on or before "To".'
  if (utcDayCount(from, to) > MAX_DAYS) return `The range can be at most ${MAX_DAYS} days.`
  return null
}

// Half-open window: From = UTC midnight of the first day, To = UTC midnight AFTER the last day.
export function toReportQuery(filters: ReportFilters): Omit<ReportQuery, 'Top'> {
  return { From: utcDateToIso(filters.from), To: utcDateToIso(filters.to, 1) }
}
