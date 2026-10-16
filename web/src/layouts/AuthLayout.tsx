import { Outlet } from 'react-router'
import { ThemeToggle } from '@/theme/ThemeToggle'

// Centered card for /login and /register.
export function AuthLayout() {
  return (
    <div className="relative flex min-h-svh items-center justify-center px-4">
      <div className="absolute top-3 right-3">
        <ThemeToggle />
      </div>
      <div className="w-full max-w-sm space-y-6 rounded-xl border p-6">
        <p className="font-heading text-center text-lg font-semibold">BookingApi</p>
        <Outlet />
      </div>
    </div>
  )
}
