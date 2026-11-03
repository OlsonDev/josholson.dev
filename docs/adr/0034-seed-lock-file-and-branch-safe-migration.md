# ADR 0034: Seed lock file and branch-safe migration

- Status: Proposed
- Date: 2026-11-03

## Context

Seed YAML is written against one version of the schema. When a migration lands, the seeds must move with it, and the tool has to cope with two things a bare version number cannot:

- **Branches.** Two developers can each claim migration 43. One merges first, the other renames theirs to 44, and a seed stamped "43" now means different things on the two branches.
- **A migration that fails on some seeds** while it is still being perfected must not leave the seeds half converted.

SQL Server projects solved the first half with a snapshot script at the last released migration. Here, [ADR 0033 (a scoped database per test)](0033-scoped-databases-per-test.md) already caches a template per hash of the migrations, so a database at any earlier version is cheap to rebuild.

## Decision

- **`migrations/schema.lock.yaml` is generated, never hand-edited.** It sits with the migrations because it describes both: the migrations it checksums and the schema the seeds are written against. It records the version the seeds were last migrated to, a checksum for each versioned migration up to that version, and for each table its columns: type, nullability, primary key, and seed policy (see [ADR 0035, typed values and relative times](0035-typed-seed-values-and-relative-times.md)). Repeatable (`R__`) migrations are left out of the checksums, since reference data changes often and is applied at head.
- **`seeds:migrate`** moves every layer to the next migration, all or nothing:
  1. For each layer, parent first, build the template at the locked version (migrations 1 through N, cached by checksum), apply the layer's chain, then apply migration N+1.
  2. Export the layer as a diff against its migrated parent, and verify that the parent's state plus the layer equals the migrated state.
  3. Write the new layers and lock only if every layer succeeded. Otherwise write nothing, report each failing layer with the Postgres error, and drop the scratch databases (an option keeps them for debugging).
- **A mismatch names the migration.** If the lock's checksums differ from the migrations on disk, the tool reports the first migration that differs ("the seeds' 43 is not your 43") and prints the fix instead of failing generically.
- **Resolving a branch conflict means regenerating, not merging.** Rename the migration to the next free number, take the main branch's `seeds/` and `migrations/schema.lock.yaml` wholesale, and rerun `seeds:migrate`. Because export is deterministic, the diff is exactly what the migration changed.
- **`seeds:check`** fails if the lock differs from the catalog of a database built at the locked version, or if any layer does not export back to its own file (drift, see ADR 0035, typed values and relative times).
- The tool takes its paths and schema exclusions from configuration, not constants, so it can move to another project.

## Alternatives considered

- **A bare version number** (the earlier `.schema-version` file in `seeding.md`): cannot tell two branches' 43 apart.
- **A snapshot script at the released version**: a second artifact to keep in step with the migrations. Templates keyed by checksum rebuild any version from the migrations alone.
- **Hand-merging seeds when branches meet:** error-prone, and the diff of a regenerated export is smaller and exact.

## Consequences

- A migration that changes a column's type or policy needs no seed edits by hand: `seeds:migrate` rewrites the lock and the values together.
- The lock can say an older version than the newest migration on disk: that is the state while migration N+1 is being perfected. The checksums skip the lock itself, and Waypoint ignores a `.yaml` file among the migrations (checked with `waypoint info`).
- The tool writes into a folder that is otherwise hand-written and append-only, so a change to the lock in a pull request is always the tool's doing and is reviewed as such.
- Editing an old versioned migration changes its checksum and invalidates the lock, as it would invalidate Waypoint's history.
- A repeatable migration that removes a reference row a seed points at is not caught by the stamp. Bake fails on the foreign key instead.
- Waypoint's `--target` builds the old-version database, so the tool needs the dev `tools` container ([ADR 0020, Waypoint in a dev container](0020-waypoint-in-a-dev-tools-container.md)).
