# BookingApi web app

The React + TypeScript front end for BookingApi: Vite, TanStack Query, React Router, Tailwind CSS with shadcn/ui, and
an API client typed from the backend's OpenAPI document.

Setup, scripts, design decisions and the production nginx setup are described in the
[root README](../README.md#the-web-app).

```bash
npm ci
npm run dev        # http://localhost:5173, proxies /api to the API on :8080
npm test
npm run api:sync   # after an API change: refresh openapi.json and src/api/schema.d.ts
```

Layout:

```
src/
├── api/         generated schema, the API client (auth + error handling), query definitions
├── auth/        in-memory session, route guards
├── components/  shared components; ui/ holds the shadcn/ui primitives
├── layouts/     app and sign-in layouts
├── lib/         date/time, paging and form helpers
├── pages/       one file per route; admin/ for the admin screens, with their URL filter helpers
├── theme/       light / dark / system theme
└── test/        Vitest setup and the MSW server
```
