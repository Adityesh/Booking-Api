import { describe, expect, it } from 'vitest'
import {
  readMyBookingsFilters,
  toMyBookingsQuery,
  writeMyBookingsFilters,
  type MyBookingsFilters,
} from './myBookingsFilters'

const read = (search: string) => readMyBookingsFilters(new URLSearchParams(search))

const defaults: MyBookingsFilters = { when: 'upcoming', status: null, page: 1, pageSize: 20 }

describe('readMyBookingsFilters', () => {
  it('defaults to the first page of upcoming bookings, any status', () => {
    expect(read('')).toEqual(defaults)
  })

  it('reads every filter', () => {
    expect(read('when=past&status=Cancelled&page=2&pageSize=50')).toEqual({
      when: 'past',
      status: 'Cancelled',
      page: 2,
      pageSize: 50,
    })
  })

  // Hand-edited or stale links fall back to "no filter" rather than sending the API something it rejects.
  it.each([
    ['when=tomorrow', { when: 'upcoming' }],
    ['when=Past', { when: 'upcoming' }], // the URL uses lower case; only the API query uses "Past"
    ['status=Pending', { status: null }],
    ['status=confirmed', { status: null }], // statuses are case-sensitive, like the API's enum names
  ])('ignores %s', (search, expected) => {
    expect(read(search)).toEqual({ ...defaults, ...expected })
  })
})

describe('writeMyBookingsFilters', () => {
  it('writes nothing for the defaults, so the default view is just /bookings', () => {
    expect(writeMyBookingsFilters(defaults).toString()).toBe('')
  })

  it.each<MyBookingsFilters>([
    { when: 'past', status: 'Confirmed', page: 3, pageSize: 100 },
    { when: 'all', status: null, page: 1, pageSize: 50 },
    { when: 'upcoming', status: 'Cancelled', page: 2, pageSize: 20 },
  ])('round-trips %o through the URL', (filters) => {
    expect(readMyBookingsFilters(writeMyBookingsFilters(filters))).toEqual(filters)
  })
})

describe('toMyBookingsQuery', () => {
  it.each([
    ['upcoming', 'Upcoming'],
    ['past', 'Past'],
    ['all', undefined], // no When at all: the API returns every booking
  ] as const)('maps the %s tab to When=%s', (when, expected) => {
    expect(toMyBookingsQuery({ ...defaults, when }).When).toBe(expected)
  })

  it('leaves Status out when no status is chosen, and passes paging through', () => {
    expect(toMyBookingsQuery({ ...defaults, page: 2, pageSize: 50 })).toEqual({
      When: 'Upcoming',
      Status: undefined,
      Page: 2,
      PageSize: 50,
    })
  })

  it('passes a chosen status through', () => {
    expect(toMyBookingsQuery({ ...defaults, status: 'Cancelled' }).Status).toBe('Cancelled')
  })
})
