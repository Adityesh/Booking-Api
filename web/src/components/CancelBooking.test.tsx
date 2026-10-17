import { QueryClientProvider, type QueryClient } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { http, HttpResponse } from 'msw'
import { toast } from 'sonner'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createAppQueryClient } from '@/api/queryClient'
import { AuthContext, type AuthContextValue, type UserRole } from '@/auth/authContext'
import { server } from '@/test/server'
import { CancelBooking } from './CancelBooking'

vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: vi.fn() } }))

const HOUR = 60 * 60 * 1000

// A booking that starts `startsIn` from now and lasts an hour.
function bookingStartingIn(startsIn: number, id = 42) {
  const start = Date.now() + startsIn
  return { id, startTime: new Date(start).toISOString(), endTime: new Date(start + HOUR).toISOString() }
}

let queryClient: QueryClient

beforeEach(() => {
  queryClient = createAppQueryClient()
  // restoreMocks only resets vi.spyOn spies; the mocked toast functions keep their calls unless cleared.
  vi.clearAllMocks()
})

function renderAs(role: UserRole, ui: React.ReactElement) {
  const auth: AuthContextValue = {
    user: { username: 'someone', role, expiresAt: new Date(Date.now() + HOUR).toISOString() },
    lastSignOut: null,
    signIn: vi.fn(),
    signOut: vi.fn(),
  }
  return render(
    <QueryClientProvider client={queryClient}>
      <AuthContext value={auth}>{ui}</AuthContext>
    </QueryClientProvider>,
  )
}

// Opens the confirm dialog and confirms. The trigger and the confirm button share a label in the full variant, so the
// confirm button is looked up inside the dialog.
async function confirmCancel(trigger: RegExp) {
  const user = userEvent.setup()
  await user.click(screen.getByRole('button', { name: trigger }))
  const dialog = await screen.findByRole('alertdialog')
  await user.click(within(dialog).getByRole('button', { name: 'Cancel booking' }))
}

describe('CancelBooking, compact (table rows)', () => {
  it('renders nothing for a booking that has already ended, even for an admin', () => {
    const { container } = renderAs('Admin', <CancelBooking booking={bookingStartingIn(-3 * HOUR)} variant="compact" />)

    expect(container).toBeEmptyDOMElement()
  })

  it('tells a user a booking starting within 2 hours can no longer be cancelled', () => {
    renderAs('User', <CancelBooking booking={bookingStartingIn(HOUR)} variant="compact" />)

    expect(screen.getByText('Starts within 2 h')).toBeInTheDocument()
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('says "In progress" to a user once the booking has started', () => {
    renderAs('User', <CancelBooking booking={bookingStartingIn(-30 * 60 * 1000)} variant="compact" />)

    expect(screen.getByText('In progress')).toBeInTheDocument()
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('offers a user the button when the booking is more than 2 hours away', () => {
    renderAs('User', <CancelBooking booking={bookingStartingIn(3 * HOUR)} variant="compact" />)

    expect(screen.getByRole('button', { name: 'Cancel' })).toBeEnabled()
  })

  it('offers an admin the button inside the 2-hour window and while in progress (the admin bypass)', () => {
    renderAs(
      'Admin',
      <>
        <CancelBooking booking={bookingStartingIn(HOUR, 1)} variant="compact" />
        <CancelBooking booking={bookingStartingIn(-30 * 60 * 1000, 2)} variant="compact" />
      </>,
    )

    const buttons = screen.getAllByRole('button', { name: 'Cancel' })
    expect(buttons).toHaveLength(2)
    buttons.forEach((button) => expect(button).toBeEnabled())
  })

  it('names the booking in the confirm dialog', async () => {
    renderAs('User', <CancelBooking booking={bookingStartingIn(3 * HOUR)} variant="compact" summary="Room A, 10:00–11:00" />)

    await userEvent.setup().click(screen.getByRole('button', { name: 'Cancel' }))

    expect(within(await screen.findByRole('alertdialog')).getByText('Room A, 10:00–11:00')).toBeInTheDocument()
  })
})

describe('CancelBooking, full (detail page)', () => {
  it('explains the 2-hour rule to a user who can still cancel', () => {
    renderAs('User', <CancelBooking booking={bookingStartingIn(3 * HOUR)} />)

    expect(screen.getByRole('button', { name: 'Cancel booking' })).toBeEnabled()
    expect(screen.getByText('Bookings can be cancelled up to 2 hours before they start.')).toBeInTheDocument()
  })

  it('disables the button for a user inside the 2-hour window and says why', () => {
    renderAs('User', <CancelBooking booking={bookingStartingIn(HOUR)} />)

    expect(screen.getByRole('button', { name: 'Cancel booking' })).toBeDisabled()
    expect(screen.getByText(/starts in less than 2 hours/)).toBeInTheDocument()
  })

  it('lets an admin cancel inside the 2-hour window and says so', () => {
    renderAs('Admin', <CancelBooking booking={bookingStartingIn(HOUR)} />)

    expect(screen.getByRole('button', { name: 'Cancel booking' })).toBeEnabled()
    expect(screen.getByText('As an admin you can cancel at any time.')).toBeInTheDocument()
  })
})

describe('CancelBooking, the request', () => {
  it('posts the cancel for this booking, confirms with a toast and refreshes every booking query', async () => {
    const cancelled: string[] = []
    server.use(
      http.post('*/api/Booking/:id/cancel', ({ params }) => {
        cancelled.push(String(params.id))
        return new HttpResponse(null, { status: 204 })
      }),
    )
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries')
    renderAs('User', <CancelBooking booking={bookingStartingIn(3 * HOUR, 42)} variant="compact" />)

    await confirmCancel(/^Cancel$/)

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Booking cancelled.'))
    expect(cancelled).toEqual(['42'])
    // ['bookings'] is the prefix of the detail ['bookings', 42] AND both lists ['bookings', 'list', …].
    await waitFor(() => expect(invalidate).toHaveBeenCalledWith({ queryKey: ['bookings'] }))
  })

  it("shows the API's reason when the cancel is refused, and still refreshes", async () => {
    // e.g. the 2-hour window closed while the page sat open: the client check passed, the server's didn't.
    server.use(
      http.post('*/api/Booking/:id/cancel', () =>
        HttpResponse.text('Booking cannot be cancelled less than 2 hours before it starts', { status: 409 }),
      ),
    )
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries')
    renderAs('User', <CancelBooking booking={bookingStartingIn(3 * HOUR)} />)

    await confirmCancel(/^Cancel booking$/)

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith('Booking cannot be cancelled less than 2 hours before it starts'),
    )
    expect(toast.success).not.toHaveBeenCalled()
    await waitFor(() => expect(invalidate).toHaveBeenCalledWith({ queryKey: ['bookings'] }))
  })

  it('sends nothing when the user keeps the booking', async () => {
    const request = vi.fn()
    server.use(http.post('*/api/Booking/:id/cancel', request))
    renderAs('User', <CancelBooking booking={bookingStartingIn(3 * HOUR)} variant="compact" />)
    const user = userEvent.setup()

    await user.click(screen.getByRole('button', { name: 'Cancel' }))
    await user.click(within(await screen.findByRole('alertdialog')).getByRole('button', { name: 'Keep it' }))

    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument())
    expect(request).not.toHaveBeenCalled()
  })
})
