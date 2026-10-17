import { parseId } from './params'

// The paged admin/user lists (audit log, bookings) share these: the API caps PageSize at 100.
export const pageSizes = [20, 50, 100] as const
export type PageSize = (typeof pageSizes)[number]
const DEFAULT_PAGE_SIZE: PageSize = 20

export type Paging = { page: number; pageSize: PageSize }

// Anything unrecognised in a hand-edited URL falls back to the defaults instead of a 400 from the API.
export function readPaging(params: URLSearchParams): Paging {
  const pageSize = Number(params.get('pageSize'))
  return {
    page: parseId(params.get('page') ?? undefined) ?? 1,
    pageSize: pageSizes.includes(pageSize as PageSize) ? (pageSize as PageSize) : DEFAULT_PAGE_SIZE,
  }
}

// Defaults are left out of the URL.
export function writePaging(params: URLSearchParams, { page, pageSize }: Paging): URLSearchParams {
  if (page > 1) params.set('page', String(page))
  if (pageSize !== DEFAULT_PAGE_SIZE) params.set('pageSize', String(pageSize))
  return params
}
