// The API stores and returns UTC; the UI shows and edits local time. All conversion happens here.

const dateTimeFormat = new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' })

// The browser's IANA zone, e.g. "Asia/Kolkata", shown next to time inputs so "10:00" is never ambiguous.
export const localTimeZone = Intl.DateTimeFormat().resolvedOptions().timeZone

export function formatDateTime(iso: string): string {
  return dateTimeFormat.format(new Date(iso))
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

// A zone-less date-time string is parsed as LOCAL time; toISOString() then gives UTC with a "Z". Send the API "Z", never
// "+05:30": the backend turns an offset into a DateTime with Kind=Local, which Npgsql refuses to write to timestamptz.
export function dateTimeLocalToUtcIso(value: string): string | null {
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? null : date.toISOString()
}
