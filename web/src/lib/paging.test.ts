import { describe, expect, it } from 'vitest'
import { readPaging, writePaging } from './paging'

const read = (search: string) => readPaging(new URLSearchParams(search))

describe('readPaging', () => {
  it('defaults to page 1 and 20 per page', () => {
    expect(read('')).toEqual({ page: 1, pageSize: 20 })
  })

  it('reads a valid page and one of the offered page sizes', () => {
    expect(read('page=3&pageSize=50')).toEqual({ page: 3, pageSize: 50 })
  })

  it.each(['0', '-1', '1.5', 'abc', ''])('falls back to page 1 for page=%j', (page) => {
    expect(read(`page=${page}`).page).toBe(1)
  })

  // Only the offered sizes: 101 would be a 400 from the API, 7 isn't in the dropdown.
  it.each(['7', '101', '0', 'abc'])('falls back to 20 per page for pageSize=%j', (pageSize) => {
    expect(read(`pageSize=${pageSize}`).pageSize).toBe(20)
  })
})

describe('writePaging', () => {
  it('leaves the defaults out of the URL', () => {
    expect(writePaging(new URLSearchParams(), { page: 1, pageSize: 20 }).toString()).toBe('')
  })

  it('writes non-default values next to the existing params', () => {
    const params = writePaging(new URLSearchParams('when=past'), { page: 2, pageSize: 100 })
    expect(params.toString()).toBe('when=past&page=2&pageSize=100')
  })

  it('round-trips through readPaging', () => {
    const paging = { page: 4, pageSize: 50 } as const
    expect(readPaging(writePaging(new URLSearchParams(), paging))).toEqual(paging)
  })
})
