import { queryOptions } from '@tanstack/react-query'
import { client, unwrap } from './client'
import type { paths } from './schema'

export type AuditLogQuery = NonNullable<paths['/api/AuditLog']['get']['parameters']['query']>
export type MyBookingsQuery = NonNullable<paths['/api/Booking/me']['get']['parameters']['query']>
export type AdminBookingsQuery = NonNullable<paths['/api/Booking']['get']['parameters']['query']>

// Every query in one place, so keys can't drift between the page that reads data and the mutation that invalidates
// it. Keys are hierarchical: invalidating ['resources'] also refreshes ['resources', 3].

export const auditLogQuery = (query: AuditLogQuery) =>
  queryOptions({
    queryKey: ['auditLog', query],
    queryFn: ({ signal }) => unwrap(client.GET('/api/AuditLog', { params: { query }, signal })),
  })

export type ReportQuery = paths['/api/Reports/most-booked-resources']['get']['parameters']['query']

// The three reports share From/To; only most-booked uses Top, so it is left out of the other two keys and requests.
export const mostBookedResourcesQuery = (query: ReportQuery) =>
  queryOptions({
    queryKey: ['reports', 'most-booked-resources', query],
    queryFn: ({ signal }) => unwrap(client.GET('/api/Reports/most-booked-resources', { params: { query }, signal })),
  })

export const averageBookingDurationQuery = (query: Omit<ReportQuery, 'Top'>) =>
  queryOptions({
    queryKey: ['reports', 'average-booking-duration', query],
    queryFn: ({ signal }) => unwrap(client.GET('/api/Reports/average-booking-duration', { params: { query }, signal })),
  })

export const bookingsPerDayQuery = (query: Omit<ReportQuery, 'Top'>) =>
  queryOptions({
    queryKey: ['reports', 'bookings-per-day', query],
    queryFn: ({ signal }) => unwrap(client.GET('/api/Reports/bookings-per-day', { params: { query }, signal })),
  })

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

// Prefix of every booking query, details and lists: a cancel changes both (and a promotion adds a booking).
export const allBookingsKey = ['bookings'] as const
// Prefix of the two lists only: a new booking appears in them, but no existing booking's details change.
export const bookingListsKey = ['bookings', 'list'] as const

export const myBookingsQuery = (query: MyBookingsQuery) =>
  queryOptions({
    queryKey: [...bookingListsKey, 'me', query],
    queryFn: ({ signal }) => unwrap(client.GET('/api/Booking/me', { params: { query }, signal })),
  })

// Admin only: every user's bookings.
export const adminBookingsQuery = (query: AdminBookingsQuery) =>
  queryOptions({
    queryKey: [...bookingListsKey, 'all', query],
    queryFn: ({ signal }) => unwrap(client.GET('/api/Booking', { params: { query }, signal })),
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
