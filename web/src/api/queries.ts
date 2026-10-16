import { queryOptions } from '@tanstack/react-query'
import { client, unwrap } from './client'

// Every query in one place, so keys can't drift between the page that reads data and the mutation that invalidates
// it. Keys are hierarchical: invalidating ['resources'] also refreshes ['resources', 3].

export const resourcesQuery = () =>
  queryOptions({
    queryKey: ['resources'],
    queryFn: ({ signal }) => unwrap(client.GET('/api/Resource', { signal })),
  })

// Admin only: includes inactive resources.
export const allResourcesQuery = () =>
  queryOptions({
    queryKey: ['resources', 'all'],
    queryFn: ({ signal }) => unwrap(client.GET('/api/Resource/all', { signal })),
  })

export const resourceQuery = (id: number) =>
  queryOptions({
    queryKey: ['resources', id],
    queryFn: ({ signal }) => unwrap(client.GET('/api/Resource/{id}', { params: { path: { id } }, signal })),
  })

export const bookingQuery = (id: number) =>
  queryOptions({
    queryKey: ['bookings', id],
    queryFn: ({ signal }) => unwrap(client.GET('/api/Booking/{id}', { params: { path: { id } }, signal })),
  })

export const myWaitlistQuery = () =>
  queryOptions({
    queryKey: ['waitlist', 'me'],
    queryFn: ({ signal }) => unwrap(client.GET('/api/Waitlist/me', { signal })),
  })
