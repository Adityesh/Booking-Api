import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterAll, afterEach } from 'vitest'
import { server } from './server'

// The API client uses relative URLs ('/api/...'), which a browser resolves against the page. Node's Request does not
// (jsdom's `location` doesn't reach it) and throws "Failed to parse URL". Resolve them against jsdom's page URL, as a
// browser would, so tests exercise the real client unchanged.
const NodeRequest = globalThis.Request
globalThis.Request = class extends NodeRequest {
  constructor(input: RequestInfo | URL, init?: RequestInit) {
    super(typeof input === 'string' ? new URL(input, globalThis.location.href) : input, init)
  }
}

// MSW 3 renamed `onUnhandledRequest` to `onUnhandledFrame`. 'error' makes any request without a handler fail the test
// instead of silently reaching the network.
// Started at module level, NOT in beforeAll: openapi-fetch keeps a reference to globalThis.fetch when createClient()
// runs, i.e. when a test file imports '@/api/client'. beforeAll runs after those imports, so the app's client would
// hold the unpatched fetch and hit the real network. Setup files run before the test file is imported.
server.listen({ onUnhandledFrame: 'error' })

afterEach(() => {
  // Vitest globals are off, so RTL cannot register its own auto-cleanup; unmount explicitly.
  cleanup()
  // Drop per-test `server.use(...)` overrides so they never leak into the next test.
  server.resetHandlers()
})

afterAll(() => server.close())
