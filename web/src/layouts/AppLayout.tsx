import { Link, NavLink, Outlet } from 'react-router'
import { Button } from '@/components/ui/button'
import { useAuth } from '@/auth/authContext'
import { cn } from '@/lib/utils'
import { ThemeToggle } from '@/theme/ThemeToggle'

const userLinks = [
  { to: '/resources', label: 'Resources' },
  { to: '/bookings', label: 'My bookings' },
  { to: '/waitlist', label: 'My waitlist' },
]

const adminLinks = [
  { to: '/admin/resources', label: 'Manage resources' },
  { to: '/admin/bookings', label: 'All bookings' },
  { to: '/admin/audit-log', label: 'Audit log' },
  { to: '/admin/reports', label: 'Reports' },
]

function NavItem({ to, label }: { to: string; label: string }) {
  return (
    <NavLink
      to={to}
      className={({ isActive }) =>
        cn(
          'rounded-md px-2.5 py-1.5 text-sm transition-colors',
          isActive ? 'bg-muted text-foreground font-medium' : 'text-muted-foreground hover:text-foreground',
        )
      }
    >
      {label}
    </NavLink>
  )
}

// Shell for every signed-in page. Admin links are only hidden for users, not protected: <RequireRole> and the API's
// 403 do that.
export function AppLayout() {
  // No navigate() here: dropping the user makes <RequireAuth> redirect to /login. Navigating as well would race it.
  const { user, signOut } = useAuth()

  return (
    <div className="min-h-svh">
      <header className="border-b">
        <div className="mx-auto flex max-w-5xl flex-wrap items-center gap-x-6 gap-y-2 px-4 py-3">
          <Link to="/" className="font-heading font-semibold">
            BookingApi
          </Link>
          <nav aria-label="Main" className="flex flex-wrap gap-1">
            {userLinks.map((link) => (
              <NavItem key={link.to} {...link} />
            ))}
          </nav>
          {user?.role === 'Admin' && (
            <nav aria-label="Admin" className="flex flex-wrap gap-1 border-l pl-4">
              {adminLinks.map((link) => (
                <NavItem key={link.to} {...link} />
              ))}
            </nav>
          )}
          <div className="ml-auto flex items-center gap-3 text-sm">
            <span>
              {user?.username}
              {user?.role === 'Admin' && (
                <span className="bg-muted text-muted-foreground ml-2 rounded px-1.5 py-0.5 text-xs">Admin</span>
              )}
            </span>
            <ThemeToggle />
            <Button variant="outline" size="sm" onClick={signOut}>
              Sign out
            </Button>
          </div>
        </div>
      </header>
      <main className="mx-auto max-w-5xl px-4 py-8">
        <Outlet />
      </main>
    </div>
  )
}
