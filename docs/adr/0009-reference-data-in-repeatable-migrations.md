# ADR 0009: Reference data in repeatable migrations

- Status: Accepted
- Date: 2026-10-21

## Context

Mime types and file extensions are needed in production as well as in dev and test.

## Decision

Keep them in `R__blob_reference_data.sql` as idempotent upserts (`ON CONFLICT`). Seeds hold dev and test data only.

## Alternatives considered

- Inserts in the versioned migration: every change would need a new version.
- Seeds: production would not get them.

## Consequences

- The file reruns only when its contents change, after the versioned migrations.
- Upserts never delete: removing a row from the file does not remove it from the database.
- `ON CONFLICT ... DO UPDATE` consumes identity values even when it only updates. A fresh build still numbers the rows from 1.
- The file first clears `is_preferred` and then re-sets it, so flipping the preferred extension cannot violate the one-preferred partial unique index.
