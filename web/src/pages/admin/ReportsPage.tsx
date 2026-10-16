import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useState, type FormEvent, type ReactNode } from 'react'
import { Link, useSearchParams } from 'react-router'
import { averageBookingDurationQuery, bookingsPerDayQuery, mostBookedResourcesQuery } from '@/api/queries'
import type { ApiError } from '@/api/client'
import { ErrorAlert } from '@/components/ErrorAlert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardAction, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { Field, FieldDescription, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatUtcDate, utcDateString, utcDayCount } from '@/lib/datetime'
import { rangeError, readReportFilters, toReportQuery, topOptions, writeReportFilters, type ReportFilters } from './reportFilters'

export function ReportsPage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const filters = readReportFilters(searchParams)
  const update = (next: Partial<ReportFilters>) => setSearchParams(writeReportFilters({ ...filters, ...next }))
  // A hand-edited URL can still hold an invalid window; then no report is requested at all.
  const invalid = rangeError(filters.from, filters.to)

  return (
    <section className="space-y-6">
      <div className="space-y-1">
        <h1 className="font-heading text-2xl font-semibold">Reports</h1>
        <p className="text-muted-foreground">Confirmed bookings that start inside the window. Cancelled ones don't count.</p>
      </div>

      <RangeForm key={searchParams.toString()} filters={filters} onApply={(range) => update(range)} />

      {invalid ? (
        <ErrorAlert>{invalid}</ErrorAlert>
      ) : (
        <>
          <div className="grid gap-6 lg:grid-cols-[1fr_2fr]">
            <AverageDurationCard filters={filters} />
            <BookingsPerDayCard filters={filters} />
          </div>
          <MostBookedCard filters={filters} onTop={(top) => update({ top })} />
        </>
      )}
    </section>
  )
}

const presets = [
  { label: 'Last 7 days', days: 7 },
  { label: 'Last 30 days', days: 30 },
  { label: 'Last 90 days', days: 90 },
]

function RangeForm({ filters, onApply }: { filters: ReportFilters; onApply: (range: Pick<ReportFilters, 'from' | 'to'>) => void }) {
  const [inputError, setInputError] = useState<string | null>(null)

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const from = String(form.get('from'))
    const to = String(form.get('to'))
    const error = rangeError(from, to)
    if (error) return setInputError(error)
    setInputError(null)
    onApply({ from, to })
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-4 rounded-lg border p-4">
      <div className="flex flex-wrap items-end gap-4">
        <Field className="w-auto">
          <FieldLabel htmlFor="from">From</FieldLabel>
          <Input id="from" name="from" type="date" defaultValue={filters.from} required />
        </Field>
        <Field className="w-auto">
          <FieldLabel htmlFor="to">To</FieldLabel>
          <Input id="to" name="to" type="date" defaultValue={filters.to} required />
        </Field>
        <Button type="submit">Apply</Button>
        <div className="flex flex-wrap gap-2">
          {presets.map((preset) => (
            <Button
              key={preset.days}
              type="button"
              variant="outline"
              size="sm"
              onClick={() => onApply({ from: utcDateString(1 - preset.days), to: utcDateString() })}
            >
              {preset.label}
            </Button>
          ))}
        </div>
      </div>
      <FieldDescription>
        Days are UTC calendar days, both ends included, at most 366. A booking counts on the UTC day it starts.
      </FieldDescription>
      {inputError && <ErrorAlert>{inputError}</ErrorAlert>}
    </form>
  )
}

// Shared loading / error / stale-while-refetching handling for the three report cards.
function ReportBody<T>({
  data,
  error,
  isPlaceholderData,
  children,
}: {
  data: T | undefined
  error: ApiError | null
  isPlaceholderData: boolean
  children: (data: T) => ReactNode
}) {
  if (error) return <ErrorAlert>{error.message}</ErrorAlert>
  if (data === undefined) return <p className="text-muted-foreground text-sm">Loading…</p>
  return <div className={isPlaceholderData ? 'opacity-60 transition-opacity' : undefined}>{children(data)}</div>
}

function formatMinutes(minutes: number): string {
  const rounded = Math.round(minutes)
  const hours = Math.floor(rounded / 60)
  const rest = rounded % 60
  if (hours === 0) return `${rest} min`
  return rest === 0 ? `${hours} h` : `${hours} h ${rest} min`
}

function AverageDurationCard({ filters }: { filters: ReportFilters }) {
  const query = useQuery({ ...averageBookingDurationQuery(toReportQuery(filters)), placeholderData: keepPreviousData })

  return (
    <Card>
      <CardHeader>
        <CardTitle>Average booking</CardTitle>
        <CardDescription>Length of an average booking</CardDescription>
      </CardHeader>
      <CardContent>
        <ReportBody {...query}>
          {(report) =>
            // averageMinutes is null, not 0, when there were no bookings: "no data" is not "zero minutes".
            report.averageMinutes === null ? (
              <p className="text-muted-foreground text-sm">No bookings in this window.</p>
            ) : (
              <div className="space-y-1">
                <p className="font-heading text-3xl font-semibold tabular-nums">{formatMinutes(report.averageMinutes)}</p>
                <p className="text-muted-foreground text-sm tabular-nums">
                  across {report.bookingCount} {report.bookingCount === 1 ? 'booking' : 'bookings'}
                </p>
              </div>
            )
          }
        </ReportBody>
      </CardContent>
    </Card>
  )
}

function BookingsPerDayCard({ filters }: { filters: ReportFilters }) {
  const query = useQuery({ ...bookingsPerDayQuery(toReportQuery(filters)), placeholderData: keepPreviousData })
  const days = utcDayCount(filters.from, filters.to)

  return (
    <Card>
      <CardHeader>
        <CardTitle>Bookings per day</CardTitle>
        <CardDescription>
          {formatUtcDate(filters.from)} – {formatUtcDate(filters.to)} ({days} {days === 1 ? 'day' : 'days'})
        </CardDescription>
      </CardHeader>
      <CardContent>
        <ReportBody {...query}>{(perDay) => <DayBars perDay={perDay} />}</ReportBody>
      </CardContent>
    </Card>
  )
}

// Plain CSS bars instead of a chart library: one series, at most 366 bars. The API zero-fills empty days, so every day
// in the window has a bar.
function DayBars({ perDay }: { perDay: { date: string; count: number }[] }) {
  const max = Math.max(1, ...perDay.map((day) => day.count))
  const total = perDay.reduce((sum, day) => sum + day.count, 0)
  const busiest = perDay.reduce<(typeof perDay)[number] | null>(
    (best, day) => (day.count > (best?.count ?? 0) ? day : best),
    null,
  )

  return (
    <div className="space-y-3">
      <p className="text-muted-foreground text-sm">
        {total} {total === 1 ? 'booking' : 'bookings'}
        {busiest && `, busiest day ${formatUtcDate(busiest.date)} (${busiest.count})`}
      </p>
      <div className="flex h-40 items-end gap-px" role="list" aria-label="Bookings per day">
        {perDay.map((day) => {
          const label = `${formatUtcDate(day.date)}: ${day.count} ${day.count === 1 ? 'booking' : 'bookings'}`
          return (
            <div key={day.date} role="listitem" aria-label={label} title={label} className="group flex h-full flex-1 items-end">
              <div
                className="bg-primary/80 group-hover:bg-primary w-full rounded-t-sm"
                // Empty days keep a hairline so the axis stays readable.
                style={{ height: day.count === 0 ? '1px' : `${(day.count / max) * 100}%` }}
              />
            </div>
          )
        })}
      </div>
      {perDay.length > 0 && (
        <div className="text-muted-foreground flex justify-between text-xs">
          <span>{formatUtcDate(perDay[0].date, true)}</span>
          <span>{formatUtcDate(perDay[perDay.length - 1].date, true)}</span>
        </div>
      )}
    </div>
  )
}

function MostBookedCard({ filters, onTop }: { filters: ReportFilters; onTop: (top: number) => void }) {
  const query = useQuery({
    ...mostBookedResourcesQuery({ ...toReportQuery(filters), Top: filters.top }),
    placeholderData: keepPreviousData,
  })

  return (
    <Card>
      <CardHeader>
        <CardTitle>Most booked resources</CardTitle>
        <CardDescription>By number of bookings, then total hours</CardDescription>
        <CardAction>
          <Select value={String(filters.top)} onValueChange={(value) => onTop(Number(value))}>
            <SelectTrigger size="sm" aria-label="Number of resources">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {topOptions.map((top) => (
                <SelectItem key={top} value={String(top)}>
                  Top {top}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </CardAction>
      </CardHeader>
      <CardContent>
        <ReportBody {...query}>
          {(resources) =>
            resources.length === 0 ? (
              <p className="text-muted-foreground text-sm">No bookings in this window.</p>
            ) : (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead className="w-12">#</TableHead>
                    <TableHead>Resource</TableHead>
                    <TableHead className="text-right">Bookings</TableHead>
                    <TableHead className="text-right">Hours booked</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {resources.map((resource, index) => (
                    <TableRow key={resource.resourceId}>
                      <TableCell className="text-muted-foreground tabular-nums">{index + 1}</TableCell>
                      <TableCell>
                        <span className="flex items-center gap-2">
                          <Link to={`/resources/${resource.resourceId}`} className="hover:underline">
                            {resource.name}
                          </Link>
                          {/* Retired resources still count: their bookings happened. */}
                          {!resource.isActive && <Badge variant="secondary">Inactive</Badge>}
                        </span>
                      </TableCell>
                      <TableCell className="text-right tabular-nums">{resource.bookingCount}</TableCell>
                      <TableCell className="text-right tabular-nums">{resource.totalBookedHours.toFixed(1)}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )
          }
        </ReportBody>
      </CardContent>
    </Card>
  )
}
