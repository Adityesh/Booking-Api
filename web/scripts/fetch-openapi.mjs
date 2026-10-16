// Downloads the OpenAPI document from a running BookingApi and saves it as web/openapi.json, the committed snapshot
// the TypeScript types are generated from (`npm run api:types`). Run it whenever the API contract changes.
//
// The API only serves the document in Development, so start it with `dotnet run --project BookingApi` (the default
// "http" launch profile: Development on port 5013). The compose API on :8080 runs in Production and will 404.
// Override the URL with OPENAPI_URL=... if the API runs elsewhere.

import { writeFile } from 'node:fs/promises'

const url = process.env.OPENAPI_URL ?? 'http://localhost:5013/openapi/v1.json'
const output = new URL('../openapi.json', import.meta.url)

function fail(message) {
  console.error(`api:fetch failed: ${message}`)
  process.exit(1)
}

let response
try {
  response = await fetch(url, { signal: AbortSignal.timeout(10_000) })
} catch (err) {
  fail(`could not reach ${url} (${err.cause?.code ?? err.message}).\n` +
    'Is the API running? Start it with: dotnet run --project BookingApi')
}

if (response.status === 404) {
  fail(`${url} returned 404. The document is only mapped in Development; ` +
    'the compose API (:8080) runs in Production. Use `dotnet run --project BookingApi` instead.')
}
if (!response.ok) fail(`${url} returned ${response.status} ${response.statusText}`)

let document
try {
  document = await response.json()
} catch {
  fail(`${url} did not return JSON`)
}

// Never overwrite a good snapshot with something that is not an OpenAPI document.
if (typeof document?.openapi !== 'string' || typeof document?.paths !== 'object') {
  fail(`${url} returned JSON, but not an OpenAPI document (no "openapi"/"paths")`)
}

// ASP.NET fills `servers` with the host the request came from (e.g. http://localhost:5013/). Keeping it would make the
// snapshot depend on where it was fetched from, and a client that honours it would bypass the Vite proxy. The app
// always calls relative /api URLs, so the contract carries no server.
delete document.servers

// Stable formatting (2-space indent, trailing newline) keeps diffs limited to real contract changes.
await writeFile(output, JSON.stringify(document, null, 2) + '\n')
console.log(`Saved OpenAPI ${document.openapi} document (${Object.keys(document.paths).length} paths) to web/openapi.json`)
