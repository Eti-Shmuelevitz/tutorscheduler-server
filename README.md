# TutorScheduler

A system for scheduling private tutoring lessons. Tutors post available lesson
slots; students browse and book them. Built as an ASP.NET Core Web API final
project, with a minimal React client used only to demonstrate the server
end-to-end.

## The limited resource and how conflicts are handled

> **Note:** this section is drafted as a starting point. Rewrite it in your
> own words before submitting — the assignment explicitly wants this
> explanation to demonstrate your own understanding, not a template.

The contested resource is the `LessonSlot`: one tutor, one hour. Only one
student can ever hold an active booking on a given slot, but two students can
easily try to book the same slot within milliseconds of each other. This is
handled with **optimistic concurrency**: `LessonSlot` carries a `Version`
(`Guid`) concurrency token, rotated on every update. When two requests race to
book the same slot, both read it as `Available`, but only the first
`SaveChangesAsync` call succeeds — its `UPDATE` matches the row's current
`Version`. The second request's `UPDATE` matches zero rows, because the
`Version` it holds is now stale, and Entity Framework Core surfaces that as
`DbUpdateConcurrencyException`, which `BookingService` catches and turns into
an HTTP `409 Conflict` with a clear message. No row is ever locked ahead of
time — the conflict is detected only at the moment of the write.

This is proven directly by
`TutorScheduler.Tests/Concurrency/LessonSlotConcurrencyTests.cs`, which opens
two independent `AppDbContext` instances against the same row and asserts
exactly this outcome.

### Beyond the single slot: no person in two lessons at once

The per-slot `Version` only protects writes to *the same row*. It cannot stop
one student from booking two **different** slots that overlap in time (say
10:00–11:00 with one tutor and 10:30–11:30 with another) in two parallel
requests: each request writes a different `LessonSlot` row, and each one's
"does this overlap my other bookings?" check runs before the other has
committed. A stress test against the original code double-booked the student
in 15 out of 15 attempts. The same gap let a tutor create overlapping slots.

The fix reuses the same optimistic mechanism one level up: `User` carries a
second concurrency token, `ScheduleVersion`. Every booking rotates the
student's `ScheduleVersion` (and every slot create/update rotates the tutor's)
in the **same** `SaveChangesAsync` as the booking itself. Two parallel
schedule changes for one person therefore both write the same `Users` row, and
only the first can commit. The second gets `DbUpdateConcurrencyException` →
`409`, and its whole transaction rolls back, so no slot is left marked
`Booked` without a booking.

A partial unique index (`IX_Bookings_LessonSlotId_ActiveUnique`: at most one
`Active` booking per slot) backs this up at the database level, even for writes
that bypass the Service layer. Because EF Core sends the `Booking` INSERT
before the `LessonSlot` UPDATE, a losing request can hit that index (or, if
the tutor deleted the slot in the meantime, the foreign key) *before* its stale
`Version` is checked. `UnitOfWork` reports those specific PostgreSQL
violations as the same `DbUpdateConcurrencyException`, so every lost race
returns `409`, never `500`.

| Scenario | Result |
|---|---|
| 20 students book the same slot at the same moment | exactly 1 × `201`, 19 × `409` |
| One student double-clicks "book" 10 times | exactly 1 booking |
| One student books two overlapping slots in parallel | exactly 1 booking, the other `409` |
| Overlapping booking made sequentially | `400` |
| Back-to-back slots (09–10, 10–11, 11–12) | all allowed (intervals are half-open) |
| Tutor creates an overlapping slot / the same slot 5× in parallel | `409` / exactly 1 created |
| Tutor deletes a slot while a student is booking it | student gets `409`, no orphan booking |
| Tutor cancels a lesson while a student books / cancels it | whichever commits second gets `409`; never a cancelled lesson with an active booking, never a notification about a booking the student already cancelled |

These are covered deterministically by
`TutorScheduler.Tests/Concurrency/DoubleBookingRaceTests.cs` (real services
and repositories, real SQL engine, with request A held right before its save
while request B commits). They can also be reproduced live against the running
API with `scripts/double-booking-stress-test.ps1`.

### Tutor cancels a lesson, student is notified

`POST /api/lessonslots/{id}/cancel` (Tutor, body `{ "reason": "..." }`, reason
optional, up to 300 characters) cancels a future lesson even when it's booked.
In a single save, the slot becomes `Cancelled`, the booking becomes
`CancelledByTutor`, and a `Notification` row is created for the student. Like
every other write to a slot, this one goes through the slot's `Version` token,
so if a student books or cancels the lesson at that moment, one of the two
requests gets `409` instead of both half-applying.

| Endpoint | |
|---|---|
| `GET /api/notifications/mine?page=&pageSize=` | the signed-in user's notifications, newest first |
| `GET /api/notifications/unread-count` | `{ "count": n }`, used by the badge in the client |
| `POST /api/notifications/{id}/read` | mark one as read (idempotent; `403` for someone else's) |
| `POST /api/notifications/read-all` | mark all as read |

Notifications store structured data (lesson, reason), not finished text, so the
client phrases them in Hebrew.

## Tech stack

- ASP.NET Core 8 Web API, 4-project layered solution (`Core` / `Data` /
  `Service` / `API`)
- EF Core 8 (Npgsql / PostgreSQL), code-first with migrations
- JWT bearer authentication, two roles (`Tutor`, `Student`) with different
  permissions enforced both by `[Authorize(Roles = ...)]` and by
  resource-ownership checks in the Service layer
- AutoMapper for entity↔DTO mapping
- NLog for structured logging, with a `CorrelationId` middleware tagging every
  request end-to-end and a global exception-handling middleware
- xUnit + Moq for unit tests, plus a dedicated real-`DbContext` concurrency test

## Running locally

### Prerequisites

- .NET 8 SDK
- PostgreSQL (local install, or via Docker: `docker run --name pg -e POSTGRES_PASSWORD=yourpassword -p 5432:5432 -d postgres:16`)

### 1. Configure secrets

The connection string and JWT signing key are never committed to the repo.
Set them via User Secrets:

```
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Port=5432;Database=TutorSchedulerDb;Username=postgres;Password=yourpassword" --project TutorScheduler.API
dotnet user-secrets set "Jwt:Secret" "<a long random string, 32+ bytes>" --project TutorScheduler.API
```

### 2. Run the API

```
dotnet run --project TutorScheduler.API --launch-profile http
```

On startup the app applies any pending EF Core migrations itself (it creates
the database if it doesn't exist yet), then seeds two demo accounts and one
demo lesson slot (see below). No manual database step is needed beyond
PostgreSQL running. To apply migrations by hand instead:

```
dotnet ef database update -p TutorScheduler.Data -s TutorScheduler.API
```

Swagger UI opens at `http://localhost:5041/swagger`. Use **Authorize** (🔒)
with the `token` returned by `POST /api/auth/login` to call protected
endpoints. In Visual Studio, pick the **http** launch profile: the React client
expects the API on `http://localhost:5041`.

### 3. Run the React client (separate repository)

```
npm install
npm run dev
```

Opens at `http://localhost:5173` by default and expects the API at
`http://localhost:5041/api` (configurable via `.env` → `VITE_API_BASE_URL`).

### 4. Run the tests

```
dotnet test TutorScheduler.Tests
```

No database needed: Service tests use Moq, and the concurrency tests use an
in-memory SQLite database.

Optional live stress test (API must be running):

```
powershell -ExecutionPolicy Bypass -File scripts\double-booking-stress-test.ps1
```

## Demo users

Seeded automatically on first run (see `TutorScheduler.API/SeedData.cs`):

| Role    | Email             | Password    |
|---------|-------------------|-------------|
| Tutor   | tutor@demo.com    | Passw0rd!   |
| Student | student@demo.com  | Passw0rd!   |

A demo `Mathematics` lesson slot (tomorrow, 1 hour) is seeded under the demo
tutor so there's something to browse and book immediately.
