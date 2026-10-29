# BookingApi

A resource-booking system (rooms, equipment, anything with a capacity): a REST API built with **ASP.NET Core on .NET 10**, **EF Core** and **PostgreSQL**, and a **React + TypeScript** single-page app on top of it. The whole stack starts with one `docker compose up`.

It started as a Month 3 portfolio project, and it is deliberately about the parts of backend work that are easy to get wrong: **concurrency, transactional integrity, authorization and honest testing**, rather than CRUD breadth.

**The headline result:** under 800 concurrent requests against a resource with capacity 3, the first implementation accepted **290 bookings**. After the fix it accepts **exactly 3**. The full story is [below](#the-concurrency-bug-290--3).

![CI](https://github.com/<you>/<repo>/actions/workflows/ci.yml/badge.svg)

---

## Contents

- [What it does](#what-it-does)
- [Tech stack](#tech-stack)
- [Run it locally](#run-it-locally)
- [Run it with Docker](#run-it-with-docker)
- [The web app](#the-web-app)
- [API overview](#api-overview)
- [Architecture](#architecture)
- [The concurrency bug: 290 → 3](#the-concurrency-bug-290--3)
- [Key design decisions](#key-design-decisions)
- [Testing](#testing)
- [Security notes](#security-notes)
- [Known trade-offs](#known-trade-offs)
- [Project status and roadmap](#project-status-and-roadmap)

---

## What it does

- **Resources**: a single unified entity with a `Capacity`. Rooms and equipment are the same thing to the booking logic. `Type` is only a label.
- **Bookings**: a user books a time window on a resource. Overlap and capacity are checked on every request. Users can cancel their own bookings up to 2 hours before the start; admins can cancel any booking at any time.
- **Waitlist**: when a resource is full, the request is saved as a waitlist entry. When a booking is cancelled, waiting entries that now fit are promoted automatically, inside the same transaction. Entries whose start time passes are expired by a background job.
- **Audit log**: every state change (booking, waitlist, resource) is recorded atomically with the change itself, and exposed to admins through a paged, filterable endpoint.
- **Reports** (admin only): most-booked resources, average booking duration, and bookings per day.
- **Two roles** (`User`, `Admin`) combined with per-object ownership checks.

## Tech stack

| Area | Choice |
|---|---|
| Runtime | .NET 10 / C# 14 |
| API | ASP.NET Core controllers, Scalar for API docs (`/scalar/v1` in Development) |
| Data | PostgreSQL, EF Core, Npgsql, `EFCore.NamingConventions` (snake_case columns) |
| Auth | JWT bearer, BCrypt.Net-Next for password hashing |
| Validation | FluentValidation (invoked explicitly in controllers) |
| Tests | xUnit, Moq, Testcontainers (real PostgreSQL), `WebApplicationFactory` |
| Load testing | NBomber |
| Web app | React 19, TypeScript, Vite, TanStack Query, React Router, Tailwind CSS + shadcn/ui |
| API client | Types generated from the OpenAPI document (`openapi-typescript`), `openapi-fetch` |
| Web tests | Vitest, React Testing Library, MSW |
| Serving | nginx (static files + `/api` reverse proxy) |

---

## Run it locally

**Prerequisites:** .NET 10 SDK, a PostgreSQL instance, and Docker (only needed to run the tests).

```bash
git clone <repo-url>
cd BookingApi
```

**1. Configure secrets.** The app refuses to start without these. They are stored with `dotnet user-secrets`, never in `appsettings.json`:

```bash
cd BookingApi
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=bookingapi;Username=<user>;Password=<password>"
dotnet user-secrets set "JwtSettings:Key"           "<a random string of at least 32 characters>"
dotnet user-secrets set "JwtSettings:Issuer"        "bookingapi"
dotnet user-secrets set "JwtSettings:Audience"      "bookingapi-clients"
dotnet user-secrets set "JwtSettings:ExpiryMinutes" "60"
cd ..
```

**2. Create the database schema:**

```bash
dotnet tool install --global dotnet-ef     # once, if you don't have it
dotnet ef database update --project BookingApi
```

**3. Run the API:**

```bash
dotnet run --project BookingApi
# http://localhost:5013   https://localhost:7187
# API docs: /scalar/v1 (Development only)
```

**4. Create the first admin.** There is deliberately no public way to become an admin (registration always creates a `User`). The first admin is created by an operator with a command built into the same executable:

```bash
CreateAdmin__Username="boss_01" CreateAdmin__Password="a-strong_password" CreateAdmin__Email="boss@example.com" \
  dotnet run --project BookingApi -- create-admin
```

- Credentials come from the environment (or configuration), **never from command-line arguments**, which leak into shell history and process lists.
- The username, email and password follow the same rules as registration; the password is never printed.
- It is idempotent: if an admin already exists it says so and exits `0`, so it is safe to run on every deploy. Exit code `1` means a real failure (invalid input, username taken, database error).
- The command builds the app but never calls `Run()`, so no port is opened and the background job never starts.

---

## Run it with Docker

The whole stack (PostgreSQL, the migrations, the API and the web app) starts with one command and needs nothing installed except Docker.

```bash
cp .env.example .env          # then edit the values (database password, JWT key, ...)
docker compose up --build -d  # builds the images, starts Postgres, applies migrations, starts the API and the web app
docker compose ps -a          # db: healthy, migrate: Exited (0), api and web: running
```

Then open **http://localhost:3000**. The API is also published on `:8080` for the Vite dev server and API tools.

Create the first admin, then log in:

```bash
docker compose run --rm \
  -e CreateAdmin__Username=boss_01 -e CreateAdmin__Password='a-strong_password' \
  -e CreateAdmin__Email=boss@example.com \
  api create-admin
```

How it fits together:
- `db` is PostgreSQL with a named volume, so data survives restarts (`docker compose down -v` deletes it).
- `migrate` is a one-shot container that runs an **EF Core migration bundle** and exits. The `api` only starts after it succeeded, so migrations never race between API instances.
- `api` is a multi-stage build: the SDK image compiles and publishes, and only the published output is copied into the small ASP.NET runtime image, which runs as a non-root user.
- `web` is also multi-stage: a Node image runs `npm ci` and the production build (type check included), and only the static files are copied into an `nginx` image. nginx serves them and proxies `/api` to the `api` service, so the browser talks to a single origin and no CORS is needed. See [Serving in production](#serving-in-production).
- No secret is baked into an image. Configuration arrives as environment variables from `.env`, which is git-ignored.

---

## The web app

A React single-page app in [`web/`](web/) that covers every endpoint:

- **Users:** register and sign in, browse resources, book a time slot (a full slot puts you on the waitlist instead), see upcoming and past bookings, cancel (the 2-hour rule is explained in the UI), and withdraw from the waitlist.
- **Admins:** manage resources (create, edit, deactivate), see and cancel every booking without the 2-hour limit, read the audit log, and view the three reports.
- Light, dark and system themes.

### Develop it

Prerequisites: Node 24 and a running API.

```bash
docker compose up -d db migrate api   # the API on :8080 (or run it with `dotnet run`, see below)
cd web
npm ci
npm run dev                           # http://localhost:5173
```

The Vite dev server proxies `/api` to `http://localhost:8080`. To use an API started with `dotnet run` instead, set `API_PROXY_TARGET`. The compose API is the simpler default: the `http` profile on `:5013` answers with a redirect to HTTPS, which is a different origin.

| Script | What it does |
|---|---|
| `npm run dev` | Dev server with hot reload |
| `npm run build` | Type check (`tsc -b`) and production build into `dist/` |
| `npm run lint` | oxlint |
| `npm run typecheck` | Type check only |
| `npm test` | Vitest, once (`npm run test:watch` to keep it running) |
| `npm run api:sync` | Download the OpenAPI document and regenerate `src/api/schema.d.ts` |

**When the API contract changes,** run `npm run api:sync`. The document is only served in Development, so it reads from `dotnet run --project BookingApi` on `:5013` (override with `OPENAPI_URL`). Both `openapi.json` and the generated types are committed, so the build never needs a running API.

### How it is built

- **The API contract is the single source of truth.** Request and response types are generated from the OpenAPI document, never written by hand, so a backend change that breaks the UI fails the type check. A backend test (`OpenApiContractTests`) keeps that document precise: every success response must declare one JSON content type.
- **The token lives in memory, not in `localStorage`,** where any injected script could read it. The cost is that a page reload signs you out; the planned fix is a refresh token in an `httpOnly` cookie. Auth is behind one provider, so that change stays local.
- **One API client handles every error shape:** plain-text `401`/`409`, `ProblemDetails` `400` with field errors (shown next to the matching input), and the `201` vs `202` (waitlisted) outcomes of creating a booking. Components never parse response bodies. A `401` on an authenticated request ends the session and sends you back to the page you were on after signing in again.
- **Server state is TanStack Query.** Query keys are defined in one file and are hierarchical, so a cancel refreshes the booking's page and every list it appears in with a single invalidation.
- **Filters and paging live in the URL** (`/admin/bookings?userId=7&page=2`), so a filtered view is a link you can share and the back button works.
- **Time:** the API is UTC with half-open windows. The UI shows local time and always sends UTC `Z` timestamps. "To 16 Oct" in a filter means "through the end of the 16th", so it is sent as local midnight of the 17th. Reports group by UTC day, so their date pickers are UTC days and say so.
- **The UI mirrors the API's rules but never replaces them.** Filter forms check the same combinations as the validators (date order, ids), registration checks that the passwords match, and the cancel button knows the 2-hour rule, but the API's answer is always what is shown.

### Serving in production

`web/Dockerfile` builds the app and copies only `dist/` into `nginx:1.30-alpine`. [`web/nginx.conf`](web/nginx.conf):

- **SPA fallback:** `try_files $uri $uri/ /index.html`, so a reload on `/bookings/42` (a client-side route, not a file) still loads the app.
- **Missing assets return `404`,** not `index.html`, so a stale script URL fails clearly instead of being run as HTML.
- **Caching:** files in `/assets/` have content-hashed names and are cached for a year (`immutable`); `index.html` is `no-cache`, so a new deploy is picked up on the next load.
- **gzip** for JS, CSS, JSON and SVG.
- **`/api/` is proxied to `http://api:8080`** with the usual forwarding headers. There is no trailing slash on `proxy_pass`, which keeps the `/api` prefix in the forwarded path.

---

## API overview

All endpoints except register and login require a JWT (`Authorization: Bearer <token>`).

| Method | Route | Who | Purpose |
|---|---|---|---|
| POST | `/api/auth/register` | anyone | Create a `User` account |
| POST | `/api/auth/login` | anyone | Get a JWT |
| GET | `/api/resource` | any user | List active resources |
| GET | `/api/resource/all` | admin | List every resource, including inactive |
| POST / PUT / DELETE | `/api/resource` … | admin | Create, update, soft-delete a resource |
| POST | `/api/booking` | any user | Create a booking (or join the waitlist if full, `202`) |
| GET | `/api/booking/me` | any user | My bookings, paged; filter by status and `when` (`Upcoming` / `Past`) |
| GET | `/api/booking` | admin | Every booking, paged; filter by user, resource, status and start time |
| GET | `/api/booking/{id}` | owner or admin | Read a booking |
| POST | `/api/booking/{id}/cancel` | owner or admin | Cancel (idempotent, 2-hour rule for users) |
| GET | `/api/waitlist/me` | any user | My waitlist entries |
| POST | `/api/waitlist/{id}/withdraw` | owner | Leave the waitlist (idempotent) |
| GET | `/api/auditlog` | admin | Paged, filterable audit trail |
| GET | `/api/reports/most-booked-resources` | admin | Top resources by bookings and hours |
| GET | `/api/reports/average-booking-duration` | admin | Average duration over a window |
| GET | `/api/reports/bookings-per-day` | admin | Per-day counts, zero-filled |

Report parameters: `from`, `to` (ISO 8601 with offset, half-open window on the booking's start time, at most 366 days) and, for the first report, `top` (1–50, default 10). Remember to URL-encode a `+` offset as `%2B`.

---

## Architecture

```
HTTP request
   │
   ▼
Controller ── validates input (FluentValidation), reads the user id from the JWT claims,
   │          maps a result enum to an HTTP status
   ▼
Service ───── business rules, transactions, row locks, audit entries
   │
   ▼
AppDbContext (EF Core) ──► PostgreSQL

Background: WaitlistExpirationService (every 20 min) ──► the same services/DbContext
```

```
BookingApi/
├── Controllers/
├── Data/            entities + AppDbContext
├── Dto/             request/response records, plus Validators/
├── Service/         interface + implementation per feature
├── Extensions/      DI registration grouped with C# 14 extension blocks
BookingApi.Tests/    unit, service, concurrency and integration tests
BookingApi.LoadTests/ NBomber stress test (run manually against a live instance)
web/                 React app, its Dockerfile and nginx.conf
```

**Conventions worth knowing**

- Expected outcomes are **result enums**, not exceptions (`BookingCreationResult`, `BookingCancellationResult`, `AdminCreationResult`, …). "The resource is full" is a normal answer, not an error.
- The user id **always** comes from the JWT claims. No endpoint trusts an id or role sent in a request body.
- `CancellationToken` is threaded through every async call.

---

## The concurrency bug: 290 → 3

The first version of booking creation did three separate steps: count the overlapping bookings, compare the count with the capacity, then insert. Each step is correct on its own, but together they are not atomic. Two requests can both count "2 of 3 used", both decide there is room, and both insert.

It passed every functional test. It only showed up under load:

| | Confirmed bookings (capacity = 3, ~800 concurrent requests, same time window) |
|---|---|
| **Before the fix** | **290**, plus 91 requests failing with Postgres `53300 too many clients already` |
| **After the fix** | **exactly 3**, 797 rejected with `409`, verified both in the NBomber report and with `SELECT COUNT(*)` on the database |

**The fix** is to lock the contested resource row for the duration of the check-and-insert:

```csharp
await using var transaction = await context.Database.BeginTransactionAsync(token);

var resource = await context.Resources
    .FromSqlInterpolated($"SELECT * FROM resources WHERE id = {dto.ResourceId} FOR UPDATE")
    .FirstOrDefaultAsync(token);

// ... existence, active, overlap-count and capacity checks ...

context.Bookings.Add(booking);
await context.SaveChangesAsync(token);
await transaction.CommitAsync(token);
```

A second request for the same resource waits at the `SELECT … FOR UPDATE` until the first commits or rolls back. Requests for different resources never block each other, because the lock is on one row.

**Why `FOR UPDATE` and not `Serializable` isolation?** Serializable lets everything run in parallel and aborts a loser at commit (error `40001`), so the application owns a correct retry loop. Pessimistic locking is simpler and gives a deterministic answer on the first attempt. The cost is some throughput when many people fight over one hot resource, which is a reasonable price for a capacity-limited resource.

The 53300 errors disappeared as a side effect: serializing access to the row stopped hundreds of requests from holding connections at once.

The same lock protects cancellation (where waitlist promotion happens) and waitlist withdrawal. Cancellation takes the lock **before** it reads the booking's status. A second race lived there: two simultaneous cancels of the same booking both saw `Confirmed`, both reported success and both wrote an audit entry. Locking first makes the loser see `Cancelled` and return an idempotent no-op. Concurrency tests assert that the audit counts match the bookings.

---

## Key design decisions

**Overlap logic.** Two ranges `[S1,E1)` and `[S2,E2)` do *not* overlap when `E1 <= S2` or `E2 <= S1`. Negated, they overlap when `S1 < E2 AND S2 < E1`. The strict `<` is deliberate: back-to-back bookings (one ends exactly when the next starts) are not a conflict. Capacity is recomputed on every request and is not stored as a running counter, so it cannot drift. A finished booking never overlaps a future window, so no "is it over" logic is needed.

**Soft delete and `Restrict` foreign keys.** Users and resources have an `IsActive` flag, and "delete" flips it. All five foreign keys use `DeleteBehavior.Restrict`. Cascading would let someone erase their own audit trail by getting their account removed, so history must outlive the actor who created it.

**404, not 403, for "exists but not yours".** Responses never distinguish "doesn't exist" from "you can't see it", so ids can't be probed.

**Idempotent state changes.** Cancelling an already-cancelled booking, or withdrawing an already-withdrawn waitlist entry, succeeds with no further effect. A client retrying after a network failure always ends in the same state.

**Audit logging is synchronous and in the same transaction.** `CreateLog` adds the entry to the context without saving, so the action and its audit entry commit together or not at all. A message queue was considered and rejected for audit because it would break that atomicity; the correct asynchronous version would be a transactional outbox. Rules:
- `UserId` null means a system action (e.g. expiration).
- The *acting* user is logged. If an admin cancels someone's booking, the admin is recorded.
- A waitlist promotion writes three entries: the cancellation, the promotion and the new booking.
- Rejections log nothing.

**Waitlist promotion happens inside the cancel transaction**, under the same resource lock, in a loop (one cancellation can free room for several waiting entries).

**Expiration sweep.** A background service runs every 20 minutes and expires waiting entries whose start time has passed, using a single `UPDATE … RETURNING id` statement, then writes one audit entry per expired row. `now` is a parameter so tests control time.

**Reports.** All three share one definition of "in the window": confirmed bookings with `StartTime >= from AND StartTime < to`, in UTC. The per-day report fills the days with no bookings in C#, because the database only returns days that have rows. An empty window returns `{ bookingCount: 0, averageMinutes: null }`, not an error and not a fake zero.

**Enums are stored as text** (`'Confirmed'`, not `1`) so the data is readable and robust to reordering. The trade-off is that renaming an enum member later needs a data migration.

**Authentication hardening.**
- Registration never accepts a role (an earlier version did, which allowed self-registering as admin; fixed and covered by a test).
- Duplicate usernames are enforced by a **unique index**; the `AnyAsync` pre-check is only a fast path. A unique-violation is caught and returned as `409`, which closes the check-then-insert race.
- Password rules include a 72-character maximum, because BCrypt ignores everything beyond 72 bytes.

**Indexing.** Reports are range queries over confirmed bookings, so `ix_bookings_start_time_confirmed` is a *partial, covering* index: key `start_time`, `WHERE status = 'Confirmed'`, `INCLUDE (resource_id, end_time)`. It is defined and migrated, but not yet benchmarked with `EXPLAIN (ANALYZE, BUFFERS)`, so no speed-up is claimed here.

---

## Testing

```bash
docker info          # tests need a running Docker daemon
dotnet test
dotnet test --filter "FullyQualifiedName~Integration"   # only the full-pipeline tests
```

The suite deliberately runs against **a real PostgreSQL container** (Testcontainers), not EF Core's InMemory provider. InMemory has no transactions and no row locking, so it could never have caught the bug above.

| Layer | What it proves |
|---|---|
| Controller unit tests (Moq) | Branching logic: validation failure returns 400 and never calls the service; result enums map to the right status |
| Service tests (real Postgres) | Overlap and capacity rules, cancellation window, waitlist promotion and expiration, audit entries, reports, first-admin bootstrap |
| Concurrency tests | Racing tasks against one resource: exactly *capacity* bookings succeed and the audit counts match |
| Integration tests (`WebApplicationFactory`) | The real pipeline: anonymous gets `401` on every protected route, normal users get `403` on admin routes, ownership `404`s, privilege-escalation attempts, tampered, wrong-key and expired tokens, and booking → audit end to end |

A unit test cannot prove that `[Authorize(Roles = "Admin")]` actually blocks a caller; only the integration tests with real tokens can, which is why both layers exist.

**Techniques worth noting**
- One container is shared by the whole run, with isolation by data: unique users and resources per test, and far-past "eras" (e.g. calendar year 1001 for one test, 1002 for the next) so tests that sweep or aggregate a whole table only ever see their own rows.
- Racing tasks start together through a `TaskCompletionSource` gate, and each builds its own `DbContext`.
- The first-admin tests create a fresh empty database per test, because "no admin exists" is a global condition.
- Integration tests assert a *control* first (the owner succeeds), so a later `404` or `401` can only mean what the test claims.

**Web app tests** (`cd web && npm test`) use Vitest, React Testing Library and MSW, and run in a few seconds without Docker:
- The URL filter helpers: defaults, fallbacks for hand-edited URLs, round trips, and the local-midnight `To` bound (checked in local time, so the tests pass in any time zone).
- The cancel button: the 2-hour rule for users, the admin bypass, the request it sends, and which cached queries it refreshes.

Requests go through the real API client and are answered by MSW. MSW is started when the setup file loads, before any test imports the client, because `openapi-fetch` keeps the `fetch` function it was created with.

**Load test.** `BookingApi.LoadTests` is an NBomber console project that fires concurrent booking requests at a live instance. It is how the overbooking bug was found:

```bash
dotnet run --project BookingApi            # in one terminal
dotnet run --project BookingApi.LoadTests  # in another
```

---

## Security notes

- Passwords hashed with BCrypt; the user id is read from validated JWT claims only.
- Secrets live in user-secrets / environment variables, not in source control.
- Inactive users cannot log in.
- All SQL is parameterized (`FromSqlInterpolated`, not string concatenation).
- The first-admin command takes credentials from the environment only.

---

## Known trade-offs

These are known and deliberate, and are listed so they are visible rather than discovered:

- **Usernames are case-sensitive** (`Alice` and `alice` can coexist), matching the case-sensitive unique index.
- **Login timing can reveal whether a username exists**, because BCrypt only runs for real users.
- **`401` and `409` responses are plain strings**; only validation failures use `ProblemDetails`.
- **Audit writes are synchronous**, so they add a little latency to each action in exchange for atomicity.
- **`FOR UPDATE` serializes writers on one hot resource.** That is the point, but throughput on a single very popular resource is bounded.
- **Expiration vs withdraw has a boundary race** at the exact start time of an entry; the outcome is still consistent, but which of the two wins is not defined.
- **First-admin creation has a narrow race** if two runs with *different* usernames execute at the same instant (there is no row to lock). A `pg_advisory_xact_lock` would close it; it was judged not worth the complexity for a one-off operator command.
- **Admins cannot withdraw someone else's waitlist entry yet.**
- **A report window is on the booking's start time only**: a booking that starts inside the window and ends after it counts in full.
- **Reloading the web app signs you out,** because the token is kept in memory only (see [How it is built](#how-it-is-built)).
- **Deactivating a resource cannot be undone yet**: there is no reactivate endpoint.
- **Reports group by UTC day**, not by the viewer's time zone.
- **Right after `docker compose up`,** the first `/api` calls can return `502` for a moment: nginx waits for the API container to start, not for it to be ready, because the API has no health check yet.

---

## Project status and roadmap

**Done and tested:** authentication and authorization, resources, bookings with proven concurrency safety, booking lists for users and admins, waitlist with promotion and expiration, audit logging with a read endpoint, admin reports, first-admin bootstrap, a React web app covering every endpoint, Docker and Compose setup (EF migration bundle, nginx for the web app), and a GitHub Actions pipeline that runs the full Testcontainers suite and, in parallel, the web app's lint, type check, tests and build, then builds the API, migration and web images.

**Next:**
- A health endpoint on the API, so `web` can wait until the API is ready
- Sign the user out when the token expires, not only on the next failed request
- Deploy to a host (production secrets, a deploy step after CI, an image registry)
- Benchmark the reporting index with `EXPLAIN (ANALYZE, BUFFERS)`
- A compose-based smoke test in CI

**Possible extensions:** refresh-token rotation (which also fixes sign-out on reload), real-time updates over SignalR, notifications through a transactional outbox and RabbitMQ, and Redis caching with rate limiting.