import { QueryClientProvider } from '@tanstack/react-query'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { RouterProvider } from 'react-router/dom'
import { createAppQueryClient } from './api/queryClient'
import { AuthProvider } from './auth/AuthProvider'
import { Toaster } from './components/ui/sonner'
import { router } from './router'
import { ThemeProvider } from './theme/ThemeProvider'
import './index.css'

const queryClient = createAppQueryClient()

// AuthProvider sits inside QueryClientProvider (it clears the cache on sign-in/out) and outside the router (the guards
// read it on every route).
createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ThemeProvider>
      <QueryClientProvider client={queryClient}>
        <AuthProvider>
          <RouterProvider router={router} />
          <Toaster position="top-center" />
        </AuthProvider>
      </QueryClientProvider>
    </ThemeProvider>
  </StrictMode>,
)
