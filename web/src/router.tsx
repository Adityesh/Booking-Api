import { createBrowserRouter, Navigate } from 'react-router'
import { RedirectIfAuthenticated, RequireAuth, RequireRole } from '@/auth/guards'
import { AppLayout } from '@/layouts/AppLayout'
import { AuthLayout } from '@/layouts/AuthLayout'
import { AdminBookingsPage } from '@/pages/admin/AdminBookingsPage'
import { AdminResourcesPage } from '@/pages/admin/AdminResourcesPage'
import { AuditLogPage } from '@/pages/admin/AuditLogPage'
import { ReportsPage } from '@/pages/admin/ReportsPage'
import { BookingDetailPage } from '@/pages/BookingDetailPage'
import { LoginPage } from '@/pages/LoginPage'
import { MyBookingsPage } from '@/pages/MyBookingsPage'
import { MyWaitlistPage } from '@/pages/MyWaitlistPage'
import { NotFoundPage } from '@/pages/NotFoundPage'
import { RegisterPage } from '@/pages/RegisterPage'
import { ResourceDetailPage } from '@/pages/ResourceDetailPage'
import { ResourcesPage } from '@/pages/ResourcesPage'
import { RouteErrorPage } from '@/pages/RouteErrorPage'

// No loaders: TanStack Query owns fetching and caching, the router only decides what to render. Guards and layouts are
// pathless routes, so they wrap their children without adding a URL segment.
export const router = createBrowserRouter([
  {
    errorElement: <RouteErrorPage />,
    children: [
      {
        element: <RedirectIfAuthenticated />,
        children: [
          {
            element: <AuthLayout />,
            children: [
              { path: 'login', element: <LoginPage /> },
              { path: 'register', element: <RegisterPage /> },
            ],
          },
        ],
      },
      {
        element: <RequireAuth />,
        children: [
          {
            element: <AppLayout />,
            children: [
              { index: true, element: <Navigate to="/resources" replace /> },
              { path: 'resources', element: <ResourcesPage /> },
              { path: 'resources/:id', element: <ResourceDetailPage /> },
              { path: 'bookings', element: <MyBookingsPage /> },
              { path: 'bookings/:id', element: <BookingDetailPage /> },
              { path: 'waitlist', element: <MyWaitlistPage /> },
              {
                path: 'admin',
                element: <RequireRole role="Admin" />,
                children: [
                  { index: true, element: <Navigate to="/admin/resources" replace /> },
                  { path: 'resources', element: <AdminResourcesPage /> },
                  { path: 'bookings', element: <AdminBookingsPage /> },
                  { path: 'audit-log', element: <AuditLogPage /> },
                  { path: 'reports', element: <ReportsPage /> },
                ],
              },
            ],
          },
        ],
      },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
])
