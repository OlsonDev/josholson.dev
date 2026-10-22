# ADR 0005: Waypoint for migrations

- Status: Accepted
- Date: 2026-10-21

## Context

We want plain SQL migrations driven from a CLI, so they are not tied to the .NET API or to any one test framework. Evolve was the first choice but appears to be unsupported.

## Decision

Use the Waypoint CLI. It is Flyway-compatible: `V{version}__{description}.sql` versioned files and `R__{description}.sql` repeatable files. It supports `migrate --target <version>`, which the seed tool needs to build old-schema databases.

## Alternatives considered

- Evolve: unsupported.
- Flyway: heavier (JVM), and nothing we need beyond the shared naming.
- DbUp: no tracking.
- FluentMigrator: too bloated.

## Consequences

- Waypoint is young (0.8.x as of this decision); pin its version.
- Its history table is `waypoint_schema_history` in `public`.
- `waypoint lint` exists but flags `CONCURRENTLY` and `IF NOT EXISTS` on first-time DDL, which does not apply to our migrations.
