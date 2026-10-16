import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { Link, useSearchParams } from 'react-router'
import { auditLogQuery } from '@/api/queries'
import { ErrorAlert } from '@/components/ErrorAlert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Field, FieldDescription, FieldLabel } from '@/components/ui/field'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatPreciseDateTime, localTimeZone } from '@/lib/datetime'
import { parseId } from '@/lib/params'
import {
  actionLabel,
  actionTypes,
  entityLink,
  pageSizes,
  readFilters,
  toApiQuery,
  writeFilters,
  type ActionType,
  type AuditLogFilters,
} from './auditLogFilters'

// Radix Select can't use "" as an item value, so "no action filter" needs a sentinel.
const ALL_ACTIONS = 'all'

export function AuditLogPage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const filters = readFilters(searchParams)
  const update = (next: Partial<AuditLogFilters>) => setSearchParams(writeFilters({ ...filters, ...next }))

  const { data, error, isPending, isPlaceholderData } = useQuery({
    ...auditLogQuery(toApiQuery(filters)),
    // Keep showing the current page while the next one loads, instead of flashing "Loading…" on every page change.
    placeholderData: keepPreviousData,
  })

  const isFiltered = Boolean(filters.action || filters.userId || filters.systemOnly || filters.from || filters.to)

  return (
    <section className="space-y-6">
      <div className="space-y-1">
        <h1 className="font-heading text-2xl font-semibold">Audit log</h1>
        <p className="text-muted-foreground">Every booking, waitlist and resource change, newest first.</p>
      </div>

      {/* Keyed on the URL, so back/forward (or a clicked user filter) resets the inputs to what the URL says. */}
      <FilterForm
        key={searchParams.toString()}
        filters={filters}
        onApply={(next) => update({ ...next, page: 1 })}
        onReset={() => setSearchParams({})}
      />

      {isPending && <p className="text-muted-foreground">Loading…</p>}
      {/* A 400 can only come from a hand-edited URL (e.g. userId together with systemOnly); show the API's reasons. */}
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
            'No entries match these filters.'
          ) : (
            'No audit entries yet.'
          )}
        </p>
      )}

      {data && data.data.length > 0 && (
        <div className={isPlaceholderData ? 'opacity-60 transition-opacity' : undefined}>
          <div className="rounded-lg border">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Time</TableHead>
                  <TableHead>Action</TableHead>
                  <TableHead>Entity</TableHead>
                  <TableHead>Actor</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {data.data.map((entry) => {
                  const entity = entityLink(entry.action, entry.entityId)
                  return (
                    <TableRow key={entry.id}>
                      <TableCell className="tabular-nums">{formatPreciseDateTime(entry.timestamp)}</TableCell>
                      <TableCell>{actionLabel(entry.action)}</TableCell>
                      <TableCell>
                        {entity.to ? (
                          <Link to={entity.to} className="hover:underline">
                            {entity.label}
                          </Link>
                        ) : (
                          entity.label
                        )}
                      </TableCell>
                      <TableCell>
                        {entry.userId === null ? (
                          <Badge variant="secondary">System</Badge>
                        ) : (
                          // Clicking an actor narrows the log to them, keeping the other filters.
                          <Link
                            to={{ search: writeFilters({ ...filters, userId: entry.userId, systemOnly: false, page: 1 }).toString() }}
                            className="hover:underline"
                            title="Show only this user's actions"
                          >
                            User #{entry.userId}
                          </Link>
                        )}
                      </TableCell>
                    </TableRow>
                  )
                })}
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

type FilterFormProps = {
  filters: AuditLogFilters
  onApply: (filters: Pick<AuditLogFilters, 'action' | 'userId' | 'systemOnly' | 'from' | 'to'>) => void
  onReset: () => void
}

function FilterForm({ filters, onApply, onReset }: FilterFormProps) {
  const [inputError, setInputError] = useState<string | null>(null)

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = new FormData(event.currentTarget)
    const action = String(form.get('action'))
    const rawUserId = String(form.get('userId') ?? '').trim()
    const userId = parseId(rawUserId)
    const systemOnly = form.get('systemOnly') === 'on'
    const from = String(form.get('from') ?? '') || null
    const to = String(form.get('to') ?? '') || null

    // The same rules as the API's AuditLogValidator, checked here so a bad combination never reaches the URL.
    if (rawUserId && userId === null) return setInputError('User id must be a positive whole number.')
    if (userId && systemOnly) return setInputError('Choose either a user id or system actions only, not both.')
    if (from && to && to < from) return setInputError('"From" must be on or before "To".')
    setInputError(null)

    onApply({ action: action === ALL_ACTIONS ? null : (action as ActionType), userId, systemOnly, from, to })
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-4 rounded-lg border p-4">
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <Field>
          <FieldLabel htmlFor="action">Action</FieldLabel>
          <Select name="action" defaultValue={filters.action ?? ALL_ACTIONS}>
            <SelectTrigger id="action" className="w-full">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ALL_ACTIONS}>All actions</SelectItem>
              {actionTypes.map((action) => (
                <SelectItem key={action} value={action}>
                  {actionLabel(action)}
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
      <Field orientation="horizontal">
        <Checkbox id="systemOnly" name="systemOnly" defaultChecked={filters.systemOnly} />
        <FieldLabel htmlFor="systemOnly" className="font-normal">
          System actions only (waitlist expirations)
        </FieldLabel>
      </Field>
      <FieldDescription>Dates are whole days in your time zone ({localTimeZone}), both ends included.</FieldDescription>

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

type PaginationProps = {
  page: number
  pageSize: number
  count: number
  shown: number
  // The next page is loading and `page` still describes the previous one: block clicks until it arrives.
  busy: boolean
  onPage: (page: number) => void
  onPageSize: (pageSize: (typeof pageSizes)[number]) => void
}

function Pagination({ page, pageSize, count, shown, busy, onPage, onPageSize }: PaginationProps) {
  const totalPages = Math.max(1, Math.ceil(count / pageSize))
  const first = (page - 1) * pageSize + 1

  return (
    <div className="flex flex-wrap items-center justify-between gap-4 pt-4 text-sm">
      <p className="text-muted-foreground tabular-nums">
        {first}–{first + shown - 1} of {count}
      </p>
      <div className="flex items-center gap-2">
        <Select value={String(pageSize)} onValueChange={(value) => onPageSize(Number(value) as (typeof pageSizes)[number])}>
          <SelectTrigger size="sm" aria-label="Entries per page">
            <SelectValue />
          </SelectTrigger>
          <SelectContent>
            {pageSizes.map((size) => (
              <SelectItem key={size} value={String(size)}>
                {size} per page
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
        <Button variant="outline" size="sm" disabled={busy || page <= 1} onClick={() => onPage(page - 1)}>
          Previous
        </Button>
        <span className="tabular-nums">
          Page {page} of {totalPages}
        </span>
        <Button variant="outline" size="sm" disabled={busy || page >= totalPages} onClick={() => onPage(page + 1)}>
          Next
        </Button>
      </div>
    </div>
  )
}
