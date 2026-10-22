# ADR 0007: `year_optional_month` domain

- Status: Accepted
- Date: 2026-10-21

## Context

JSON Resume dates are ISO 8601 and may be a year (`2014`) or a year and month (`2014-06`).

## Decision

`CREATE DOMAIN temporal.year_optional_month AS TEXT` with a check that enforces a four-digit year and months 01 to 12. Open end dates are `NULL`.

## Alternatives considered

- A composite type: awkward NULL ordering, a registered Npgsql mapping and Dapper handler, restricted `ALTER TYPE`, and an extra text encoding in seed YAML.
- Two smallint columns per date: fully typed, but doubles the columns.
- `DATE` plus a precision column.

## Consequences

- The text form is JSON Resume's own format, sorts correctly, and needs no special handling in Dapper, YAML, or the JSON export.
- Date math has to convert to a date first.
- Order checks compare only the year, so `2015-06` to `2015-03` passes.
