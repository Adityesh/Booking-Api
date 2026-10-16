import { Outlet } from 'react-router'

// Centered card for /login and /register.
export function AuthLayout() {
  return (
    <div className="flex min-h-svh items-center justify-center px-4">
      <div className="w-full max-w-sm space-y-6 rounded-xl border p-6">
        <p className="font-heading text-center text-lg font-semibold">BookingApi</p>
        <Outlet />
      </div>
    </div>
  )
}
