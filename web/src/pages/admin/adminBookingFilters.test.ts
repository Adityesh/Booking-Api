import { describe, expect, it } from 'vitest'
import {
  readAdminBookingFilters,
  toAdminBookingsQuery,
  writeAdminBookingFilters,
  type AdminBookingFilters,
} from './adminBookingFilters'

const read = (search: string) => readAdminBookingFilters(new URLSearchParams(search))

const defaults: AdminBookingFilters = {
  userId: null,
  resourceId: null,
  status: null,
  from: null,
  to: null,
  page: 1,
  pageSize: 20,
}

describe('readAdminBookingFilters', () => {
  it('defaults to no filters and the first page', () => {
    expect(read('')).toEqual(defaults)
  })

  it('reads every filter', () => {
    expect(read('userId=7&resourceId=3&status=Confirmed&from=2026-10-01&to=2026-10-16&page=2&pageSize=100')).toEqual({
      userId: 7,
      resourceId: 3,
      status: 'Confirmed',
      from: '2026-10-01',
      to: '2026-10-16',
      page: 2,
      pageSize: 100,
    })
  })

  it.each([
    ['userId=0', { userId: null }],
    ['userId=abc', { userId: null }],
    ['resourceId=-3', { resourceId: null }],
    ['status=Pending', { status: null }],
    ['from=16-10-2026', { from: null }],
    ['to=2026-13-45', { to: null }],
    ['from=2026-10-01T00:00:00Z', { from: null }], // a day, not an instant
  ])('ignores %s', (search, expected) => {
    expect(read(search)).toEqual({ ...defaults, ...expected })
  })
})

describe('writeAdminBookingFilters', () => {
  it('writes nothing for the defaults, so the unfiltered view is just /admin/bookings', () => {
    expect(writeAdminBookingFilters(defaults).toString()).toBe('')
  })

  it('round-trips every filter through the URL', () => {
    const filters: AdminBookingFilters = {
      userId: 7,
      resourceId: 3,
      status: 'Cancelled',
      from: '2026-10-01',
      to: '2026-10-16',
      page: 4,
      pageSize: 50,
    }
    expect(readAdminBookingFilters(writeAdminBookingFilters(filters))).toEqual(filters)
  })
})

describe('toAdminBookingsQuery', () => {
  it('sends nothing but paging when there are no filters', () => {
    expect(toAdminBookingsQuery(defaults)).toEqual({
      UserId: undefined,
      ResourceId: undefined,
      Status: undefined,
      From: undefined,
      To: undefined,
      Page: 1,
      PageSize: 20,
    })
  })

  it('passes the id and status filters through', () => {
    const query = toAdminBookingsQuery({ ...defaults, userId: 7, resourceId: 3, status: 'Confirmed' })
    expect(query).toMatchObject({ UserId: 7, ResourceId: 3, Status: 'Confirmed' })
  })

  // The API window is half-open on StartTime. "From 1 Oct, To 16 Oct" means the whole of both days in the viewer's
  // zone: From = local midnight starting the 1st, To = local midnight starting the 17th. Asserted in local time, so
  // the test holds whatever TZ it runs in.
  it('sends From as local midnight of that day and To as local midnight of the NEXT day, as UTC instants', () => {
    const query = toAdminBookingsQuery({ ...defaults, from: '2026-10-01', to: '2026-10-16' })

    expect(query.From).toMatch(/Z$/)
    expect(query.To).toMatch(/Z$/)
    expect(new Date(query.From!)).toEqual(new Date(2026, 9, 1))
    expect(new Date(query.To!)).toEqual(new Date(2026, 9, 17))
  })

  it('crosses a month end for To', () => {
    expect(new Date(toAdminBookingsQuery({ ...defaults, to: '2026-10-31' }).To!)).toEqual(new Date(2026, 10, 1))
  })
})
