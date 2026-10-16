import { setupServer } from 'msw/node'

// No default handlers: every test declares the responses it depends on with `server.use(...)`.
export const server = setupServer()
