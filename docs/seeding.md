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

## IDs

- Layers carry explicit IDs, inserted with `OVERRIDING SYSTEM VALUE`. Depth 0 uses `1..`, depth 1 uses `1000..`, and so on. Siblings share a block because they never coexist ([ADR 0015](adr/0015-seed-id-blocks-and-sequence-cursor.md)).
- While layers apply, each identity sequence is positioned by the root's rows only, so a migration that generates IDs produces the same IDs in a parent and in its children.
- When a database is finally built for use, each sequence is set to the max ID of the chain.

## Build and run

- `compile-seeds` resolves layers to SQL, orders inserts by foreign key, and validates them against the current schema in a scratch database.
- Databases are cloned with `CREATE DATABASE ... TEMPLATE` on the one Postgres server ([ADR 0013](adr/0013-single-postgres-server-with-template-databases.md)): a template per schema version, a template per layer named by a hash of the migrations and layer source, and per-test or per-run clones.
- The tool only touches databases with its own prefix plus the configured app database, uses a role with `CREATEDB` and no superuser, and refuses to run unless the host is allowlisted (localhost).

## Migrating seeds

A schema stamp (`Database/Seeds/.schema-version`) records the version the seeds were written against. `migrate-seeds`, parent first, for each node:

1. Build the old-schema database (Waypoint `--target <stamp>`) and apply the chain root to node.
2. Run the new migrations on it. The result is the node's migrated full state.
3. Export the node's layer as the diff against its migrated parent's full state.
4. Verify that the parent state plus the new layer equals the migrated full state, and that compile passes at head. Write nothing if either fails.

Only layers whose diff changed are rewritten, and a per-layer summary is printed for review. Additive migrations need no seed work.

## Export

`export-seed --parent <layer> --name <name>` diffs the live database against the parent's template and writes a layer; `--root` dumps everything. Needs an ignore list for noisy columns such as `created_utc`, a canonical text encoding for timestamps, decimals, and ranges, and primary keys on every table.
