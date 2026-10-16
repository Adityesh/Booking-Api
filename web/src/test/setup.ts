import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterAll, afterEach, beforeAll } from 'vitest'
import { server } from './server'

// MSW 3 renamed `onUnhandledRequest` to `onUnhandledFrame`. 'error' makes any request without a handler fail the test
// instead of silently reaching the network.
beforeAll(() => server.listen({ onUnhandledFrame: 'error' }))

afterEach(() => {
  // Vitest globals are off, so RTL cannot register its own auto-cleanup; unmount explicitly.
  cleanup()
  // Drop per-test `server.use(...)` overrides so they never leak into the next test.
  server.resetHandlers()
})

afterAll(() => server.close())
