# Seeding

Status: design only. The tool is not built yet. Seeds are dev and test data; reference data lives in migrations.

## Goals

- Pick a seed by name (`bun run seed minimal`) and get a database that has only the data the test needs.
- Keep the number of seeds small and self-describing; avoid inserting data that does not affect assertions.
- Keep seeds valid as migrations change, without hand-editing every file.
- Seed a database, use the app, then export the changes back out as a seed.

## Layers

- Seeds form a **tree** of layers; each layer has a parent (`extends`) and stores only its delta: upserts and deletes per table, keyed by primary key. Format: YAML ([ADR 0014](adr/0014-hierarchical-seed-layers-in-yaml.md)).
- Each layer has a `description`. `seed list` prints the tree with per-table row counts so nobody has to read a layer to know what is in it.
- Tests needing something extra insert it themselves through a factory, so the relevant data is visible in the test.

## Layer file format

One file per layer in `JoshOlson.Database/seeds/`. The layer's name is the file name without `.yaml` (lower-kebab-case, at most 40 characters, because it ends up in database names). Unknown keys are an error.

```yaml
description: What this layer adds, in a sentence
extends: parent-layer        # omit for a root layer
tables:
  foo.bar:                   # schema.table
    upsert:
      - { bar_id: 1000, name: baz, qux: ~ }
    delete:
      - { bar_id: 1001 }     # primary key columns only
```

- A value's type is its column's type, from `schema.lock.yaml` ([ADR 0035, typed values and relative times](adr/0035-typed-seed-values-and-relative-times.md)). A plain `~`, `null`, or empty value is `NULL`; quoted (`"null"`) it is the string. `jsonb` is native YAML, arrays are sequences, composites are mappings.
- A `timestamptz` column is relative by default: its values are signed offsets from the database's anchor (the time it was hydrated), so seeds never hold real dates. An offset is written like `-3d 4h 12m 0.0123s`: lowercase `d`, `h`, `m`, `s`, zero units left out, `0s` for zero, one `-` for the whole duration, exact to the microsecond. A column opts out with `@seed absolute` in its `COMMENT ON COLUMN`. The choice is per column, never per value.
- 🚧 The parser built so far still reads every value as text. It changes to typed values with the lock file.
- Parsing and the tree (duplicate names, a missing parent, and cycles are errors) are `JoshOlson.Database/Layers`. `mise run seeds:list` prints the tree.

## IDs

- Layers carry explicit IDs, inserted with `OVERRIDING SYSTEM VALUE`. Depth 0 uses `1..`, depth 1 uses `1000..`, and so on. Siblings share a block because they never coexist ([ADR 0015](adr/0015-seed-id-blocks-and-sequence-cursor.md)).
- While layers apply, each identity sequence is positioned by the root's rows only, so a migration that generates IDs produces the same IDs in a parent and in its children.
- When a database is finally built for use, each sequence is set to the max ID of the chain.

## Build and run

- `bake` (once called `compile-seeds`) resolves layers to SQL, orders inserts by foreign key, validates them against the current schema in a scratch database, and saves each layer's template with its typed fixtures.
- Databases are cloned with `CREATE DATABASE ... TEMPLATE` on the one Postgres server ([ADR 0013](adr/0013-single-postgres-server-with-template-databases.md)): a template per schema version, a template per layer named by a hash of the migrations and layer source, and a clone per test ([ADR 0033, a scoped database per test](adr/0033-scoped-databases-per-test.md)). The tool is `JoshOlson.Database`.
- The tool only touches databases with its own prefix plus the configured app database, uses a role with `CREATEDB` and no superuser, and refuses to run unless the host is allowlisted (localhost).

## Migrating seeds

`JoshOlson.Database/migrations/schema.lock.yaml` is generated, never edited by hand. It records the migration version the seeds were last migrated to, a checksum per versioned migration up to it, and the schema those migrations produce (each column's type, domain, and seed policy, and the foreign keys; `docs/schema` is rendered from it as well) ([ADR 0034, lock file and branch-safe migration](adr/0034-seed-lock-file-and-branch-safe-migration.md)). `seeds:migrate`, parent first, for each node:

1. Build the database at the locked version (migrations 1 through N, a cached template) and apply the chain root to node.
2. Apply the next migration. The result is the node's migrated full state.
3. Export the node's layer as the diff against its migrated parent's full state.
4. Verify that the parent state plus the new layer equals the migrated full state. Write nothing, for any layer, if any layer fails.

Only layers whose diff changed are rewritten, and a per-layer summary is printed for review. Additive migrations need no seed work.

If two branches claim the same migration number, `seeds:check` names the first migration that differs from the lock. Rename yours to the next number, take the main branch's `seeds/` and `migrations/schema.lock.yaml` wholesale, and rerun `seeds:migrate`.

If you only edit an already-applied migration's comment or whitespace, its checksum changes. `seeds:accept <version>` rebuilds with the edited file and, only if the catalog still equals the lock and every layer still exports to its own file, records the new checksum as accepted. If either differs, the edit changes something the seeds see, so add a new migration instead. The checksum is the SHA-256 of the raw file bytes; nothing is normalized.

## Export

`export-seed --parent <layer> --name <name>` diffs the live database against the parent's template and writes a layer; `--root` dumps everything. Primary keys are required on every table.

- Relative columns are exported as offsets from the anchor stored in the database being exported (`seed.hydration`), never from `now()`, in exact microseconds. Output is canonical (sorted by primary key, columns in catalog order), so unchanged rows give identical text and `seeds:check` can require a byte-for-byte round trip.
- Tests on this assert at one-microsecond increments ([ADR 0035, typed values and relative times](adr/0035-typed-seed-values-and-relative-times.md)).
- `mise run seed <layer>` for the dev database shifts every relative column to now. Test clones keep the anchor, which is also their `FakeClock` start.
- Fixtures for tests come from reading the baked template back with real types, not from the YAML.
