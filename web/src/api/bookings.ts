import { client } from './client'
import type { components } from './schema'

export type BookingResponse = components['schemas']['BookingResponseDto']
export type WaitlistEntryResponse = components['schemas']['WaitlistEntryResponseDto']
export type CreateBookingRequest = components['schemas']['CreateBookingDto']

export type CreateBookingOutcome =
  | { kind: 'booked'; booking: BookingResponse }
  | { kind: 'waitlisted'; entry: WaitlistEntryResponse }

// POST /api/Booking has TWO success responses: 201 with the booking, or 202 with a waitlist entry when the resource is
// full. openapi-fetch types `data` as the union of both, and only the status tells them apart, so this can't go
// through unwrap() (which drops the response). Errors still throw ApiError from the middleware.
export async function createBooking(body: CreateBookingRequest): Promise<CreateBookingOutcome> {
  const { data, response } = await client.POST('/api/Booking', { body })
  if (response.status === 202) return { kind: 'waitlisted', entry: data as WaitlistEntryResponse }
  return { kind: 'booked', booking: data as BookingResponse }
}
