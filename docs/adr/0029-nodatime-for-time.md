# ADR 0029: NodaTime for time in the API

- Status: Accepted
- Date: 2026-10-29

## Context

`blog.post` will send its publish time to the frontend, where JavaScript's Temporal is the model (`Instant`, `PlainDate`, `ZonedDateTime`). The BCL's `DateTime` mixes instants, local times, and unspecified kinds, and `DateTimeOffset` keeps an offset that Postgres `TIMESTAMPTZ` does not store. No API code handles time yet.

## Decision

- **NodaTime** is the only time library in the API: `Instant` for `TIMESTAMPTZ`, `LocalDate` for `DATE`, `DateTimeZone` for zones. Inject `IClock` (`SystemClock.Instance` in production, `FakeClock` from `NodaTime.Testing` in tests) rather than reading the system clock.
- Npgsql reads and writes these through `Npgsql.NodaTime` (`UseNodaTime()` on the data source builder), and JSON uses `NodaTime.Serialization.SystemTextJson`. Neither package is added yet; the first feature that handles time adds them, and verifies Dapper maps the NodaTime types.
- `BannedSymbols.txt` bans `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeZoneInfo`, and `TimeProvider`. A type ban catches static access (`DateTime.UtcNow`), construction, and generic arguments, but not a bare declaration of the type.
- `TimeSpan` is not banned: it is a duration, which NodaTime also lacks a type for (`Duration` is nanosecond-precise and exact, so convert at the boundary).

## Alternatives considered

- BCL types with `TimeProvider`: built in and accepted by ASP.NET, but `DateTime` ambiguity remains and there is no `LocalDate` or `Instant` equivalent.

## Consequences

- Microsoft APIs that take a `TimeProvider` need an adapter from `IClock`.
- Library code that returns `DateTime` or `DateTimeOffset` converts at the boundary (`Instant.FromDateTimeOffset`).
