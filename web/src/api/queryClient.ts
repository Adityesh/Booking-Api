import { QueryClient } from '@tanstack/react-query'
import { ApiError } from './client'

// Retry only failures that might succeed on a second attempt. A 4xx is the server's answer, not a glitch: retrying a
// 401/403/404/409 (React Query's default is 3 retries) just delays the error the user needs to see.
function shouldRetry(failureCount: number, error: Error): boolean {
  if (failureCount >= 2) return false
  if (error instanceof ApiError) return error.kind === 'network' || error.kind === 'unexpected'
  return false
}

// A factory, not a singleton: tests create a fresh client each so cached data never leaks between them.
export function createAppQueryClient(): QueryClient {
  return new QueryClient({
    defaultOptions: {
      queries: { retry: shouldRetry },
      // Mutations are not idempotent in general (POST /api/Booking): never retry them automatically.
      mutations: { retry: false },
    },
  })
}
