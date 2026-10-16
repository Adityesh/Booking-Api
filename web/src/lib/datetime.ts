// The API stores and returns UTC; the UI shows and edits local time. All conversion happens here.

const dateTimeFormat = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' })
const preciseDateTimeFormat = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'medium' })

// The browser's IANA zone, e.g. "Asia/Kolkata", shown next to time inputs so "10:00" is never ambiguous.
export const localTimeZone = Intl.DateTimeFormat().resolvedOptions().timeZone

export function formatDateTime(iso: string): string {
  return dateTimeFormat.format(new Date(iso))
}

// With seconds, for audit entries where the order of events matters.
export function formatPreciseDateTime(iso: string): string {
  return preciseDateTimeFormat.format(new Date(iso))
}

// "17 Oct 2026, 10:00 – 11:00 am" for a same-day range; Intl leaves out the repeated date by itself.
export function formatDateTimeRange(startIso: string, endIso: string): string {
  return dateTimeFormat.formatRange(new Date(startIso), new Date(endIso))
}

// <input type="datetime-local"> works with "YYYY-MM-DDTHH:mm" in local time, with no zone.
export function toDateTimeLocalValue(date: Date): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`
}

// "YYYY-MM-DD" (an <input type="date"> value) → the UTC instant of LOCAL midnight that day, plus `addDays`.
// Built from the parts on purpose: new Date("2026-10-16") parses a date-only string as UTC midnight, not local.
export function localDateToUtcIso(value: string, addDays = 0): string {
  const [year, month, day] = value.split('-').map(Number)
  return new Date(year, month - 1, day + addDays).toISOString()
}

// ---------- UTC calendar days (reports group by the UTC day a booking starts) ----------

const utcDateFormat = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeZone: 'UTC' })
const utcShortDateFormat = new Intl.DateTimeFormat(undefined, { day: 'numeric', month: 'short', timeZone: 'UTC' })

// "YYYY-MM-DD" → that UTC day's midnight, plus `addDays`, as an ISO string.
export function utcDateToIso(value: string, addDays = 0): string {
  const [year, month, day] = value.split('-').map(Number)
  return new Date(Date.UTC(year, month - 1, day + addDays)).toISOString()
}

// Today's UTC date, plus `addDays`, as "YYYY-MM-DD".
export function utcDateString(addDays = 0): string {
  const now = new Date()
  return new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate() + addDays))
    .toISOString()
    .slice(0, 10)
}

// Days from `from` to `to`, both included.
export function utcDayCount(from: string, to: string): number {
  return Math.round((Date.parse(utcDateToIso(to)) - Date.parse(utcDateToIso(from))) / 86_400_000) + 1
}

// A "YYYY-MM-DD" from the API (DateOnly) shown as that same calendar day, never shifted by the viewer's time zone.
export function formatUtcDate(value: string, short = false): string {
  return (short ? utcShortDateFormat : utcDateFormat).format(new Date(utcDateToIso(value)))
}

// A zone-less date-time string is parsed as LOCAL time; toISOString() then gives UTC with a "Z". Send the API "Z", never
// "+05:30": the backend turns an offset into a DateTime with Kind=Local, which Npgsql refuses to write to timestamptz.
export function dateTimeLocalToUtcIso(value: string): string | null {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? null : date.toISOString()
}
