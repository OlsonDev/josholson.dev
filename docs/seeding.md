# Seeding

Status: partly built (the layer parser with typed values, the tree, the lock, `seeds:migrate` (which checks the layers instead of moving them), applying a layer's rows, the schema and layer templates, test and dev databases, sequence positioning, and the commands `seeds:bake`, `seed`, `seed:test`, and `seed:clean`; moving layers to a new migration, export, and `seeds:accept` and `seeds:check` are not). Seeds are dev and test data; reference data lives in migrations.

## Goals

- Pick a seed by name (`bun run seed minimal`) and get a database that has only the data the test needs.
- Keep the number of seeds small and self-describing; avoid inserting data that does not affect assertions.
- Keep seeds valid as migrations change, without hand-editing every file.
- Seed a database, use the app, then export the changes back out as a seed.

## Layers

- The root layer is `minimal` (`seeds/minimal.yaml`): the three tags Database, Language, and Tool, colored `indigo-200`, `emerald-200`, and `amber-200`. `more-tags` extends it with the other 19 tag kinds, each a different family at weight 200, in the `1001..` block, and renames Language to Programming Language by upserting its row (`tag_id: 2`) with only the new `name` and `slug`, so it keeps its color: the layer chain's merge at work.
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
      - { bar_id: 1001, name: baz, qux: ~ }
    delete:
      - { bar_id: 1001 }     # primary key columns only
```

- A value's type is its column's type, from `schema.lock.yaml` ([ADR 0035, typed values and relative times](adr/0035-typed-seed-values-and-relative-times.md)). A plain `~`, `null`, or empty value is `NULL`; quoted (`"null"`) it is the string. `jsonb` is native YAML, arrays are sequences, composites are mappings.
- A `timestamptz` column is relative by default: its values are signed offsets from the database's anchor (the time it was hydrated), so seeds never hold real dates. An offset is written like `-3d 4h 12m 0.0123s`: lowercase `d`, `h`, `m`, `s`, zero units left out, `0s` for zero, one `-` for the whole duration, exact to the microsecond. A column opts out with `@seed absolute` in its `COMMENT ON COLUMN`. The choice is per column, never per value.
- Formats by type: `boolean` is `true` or `false`; integers are digits with an optional sign, inside the type's range; a `decimal(p, s)` column refuses more digits than it holds, since Postgres would round them; an absolute `timestamptz` is a UTC instant (`2026-01-02T03:04:05.123456Z`), never finer than a microsecond; `bytea` is `\x` and hex digits, as Postgres prints it; `inet` is an address, with `/bits` for a network; inside `jsonb`, a plain `true`, `false`, `null`, and number are those JSON values and anything quoted is a string.
- Parsing reads each value as its column's type from the lock (`mise run seeds:list` needs the lock to exist once there is a layer). A file that is not the shape above fails at the first fault. Past that, every problem with its tables and values is collected and reported together: an unknown table or column, a value that is not its column's type, a NULL in a `NOT NULL` column, a missing key column, a delete that holds more than the key, and a column generated from an expression.
- Parsing and the tree (duplicate names, a missing parent, and cycles are errors) are `JoshOlson.Database/Layers`. `mise run seeds:list` prints the tree.

## IDs

- Layers carry explicit IDs, inserted with `OVERRIDING SYSTEM VALUE`, in blocks of 1000 per depth, where a layer's depth is how many parents it has: the root `minimal` uses `1..1000`, its children `1001..2000`, their children `2001..3000`, and so on (`IdBlocks`: depth × 1000 + 1 up to (depth + 1) × 1000, the same for every depth). Siblings share a block because they never coexist ([ADR 0015](adr/0015-seed-id-blocks-and-sequence-cursor.md)). A block is for the rows a layer adds; an upsert of an ancestor's row keeps its ID (`more-tags` upserts `tag_id: 2`).
- While layers apply for a migration, each identity sequence is positioned by the root's rows only, so a migration that generates IDs produces the same IDs in a parent and in its children.
- A database built from a layer for use (the dev database, a test's, a template) hands out IDs from the **next** depth's block: every identity sequence, in every table, is set so that the next generated ID is the first of that block, or past the chain's largest ID if that is higher. So `seed minimal` gives a new tag `1001`, `seed more-tags` gives `2001`, and a table no layer has rows in starts at the same place. What you create in the app is then already where an exported child layer keeps its new rows, and cannot collide with a row of any layer at the same depth or above.

## Build and run

🚧 Built so far (`JoshOlson.Database/Seeding`): `LayerMerger` folds a chain of layers, root first, into the rows they come to (in each layer a table's deletes apply before its upserts; every problem is reported together, including a NOT NULL column with no default that no layer gives), and `SeedSql` turns those rows into typed `INSERT` statements in foreign-key order, with `OVERRIDING SYSTEM VALUE` for identity columns, `DEFAULT` for columns a row leaves out, and relative times worked out against a given anchor. `SeedApplier` runs a layer's chain (merge, build, execute in order) inside a transaction the caller owns, and names the table when an insert fails. Its tests, like the rest of the seed tool's, connect as the owner and roll back. `SchemaTemplate` (`JoshOlson.Database/Baking`) builds the schema template: it creates a database named `seed_schema_<hash>` (the hash covers every file in `migrations/`, repeatable ones included, and the provisioning script), runs Waypoint in the `tools` container against it (`ComposeTools` overrides `POSTGRES_DB` for the call), runs `provisioning/api_role.sql` through `psql` in the `postgres` container, then `Baking/seed_schema.sql`, which creates the `seed` schema with `seed.hydration` (one row, the anchor, filled when a layer is applied; it comes after the API role's script so the role is granted nothing in it), and sets `ALLOW_CONNECTIONS false`. Both scripts are in the hash. It builds under a `_building` name and renames at the end, so a bake that dies halfway leaves no template that looks finished, and it does nothing when the template for the current files exists. Every database the tool makes starts with `seed_` (`SeedDatabaseNames`).

Layer templates, test databases, and the commands (`Baking/`, run through `JoshOlson.Seed`, the command line; each takes a connection to the `postgres` database, since Postgres cannot drop or clone a database someone is connected to):

- A **layer template** (`LayerTemplate`) is `seed_layer_<hash>`, where the hash covers the schema template's name and the source of every layer in the chain, so a change to any file the layer depends on gives a new name and a change to a sibling does not. It is a clone of the *schema* template (`ServerDatabases.CloneAsync`, which retries error 55006, the source still in use) with the layer's whole chain applied (`SeedHydrator`), not of the parent's template plus the layer's delta: the rows are the same, and a delta would need an `UPDATE` and `DELETE` writer beside the `INSERT` one. It is built under a `_building` name, renamed at the end, and refuses connections, like the schema template (`ServerDatabases.EnsureTemplateAsync`).

`ServerDatabases` (`Baking/`) is the one place that creates, clones, drops, closes, renames, lists, and looks for databases on the server; no other class writes `CREATE DATABASE`, `DROP DATABASE`, or `ALTER DATABASE`. A name reaches it only through `SeedDatabaseNames`, which checks it before it is quoted into SQL.
- **Hydrating** (`SeedHydrator`) applies the chain in one transaction, records the anchor in `seed.hydration` (the bake time, cut to a microsecond, which is also the clock a test starts at), and then positions the sequences. `SequencePositioner` has both rules from ADR 0015 (seed ID blocks): `ChainAsync` moves every identity sequence to the start of the next depth's block (`IdBlocks`), or past the largest ID its table has in the whole chain if that is higher, which is what a database built for use gets, and `RootAsync` moves the sequences of the tables the root layer has rows in to the largest ID there, which `seeds:migrate` will use when it replays migrations over layers. A sequence never moves backwards.
- `mise run seeds:bake` builds the schema template and then every layer's template, parent first; templates that are current are left alone. `mise run seed:test <id> <layer>` clones the layer's template into `seed_test_<id>` (the id is lowercase letters and digits, at most 40) and prints its name. `mise run seed <layer>` drops the dev database (`POSTGRES_DB`, never a server database) and makes it again from the schema template with the chain applied and the anchor set to now, so its relative times are current; everything in it is lost. `mise run seed:clean` drops every `seed_` database the current migrations and layers do not name (old templates, test databases, builds that died halfway), and `seed:clean --all` the current templates as well.
- The tests bake the real layers and share the real templates, so classes that build or clone one run one after another (`TemplateCollection`); the template test uses a copy of the migrations with one extra file to get a name of its own.

- `bake` (once called `compile-seeds`) resolves layers to SQL, orders inserts by foreign key, validates them against the current schema in a scratch database, and saves each layer's template with its typed fixtures.
- Databases are cloned with `CREATE DATABASE ... TEMPLATE` on the one Postgres server ([ADR 0013](adr/0013-single-postgres-server-with-template-databases.md)): a template per schema version, a template per layer named by a hash of the migrations and layer source, and a clone per test ([ADR 0033, a scoped database per test](adr/0033-scoped-databases-per-test.md)). The tool is `JoshOlson.Database`.
- The tool only touches databases with its own prefix plus the configured app database, uses a role with `CREATEDB` and no superuser, and refuses to run unless the host is allowlisted (localhost).

## Migrating seeds

`JoshOlson.Database/migrations/schema.lock.yaml` is generated, never edited by hand. It records the migration version the seeds were last migrated to, a checksum per versioned migration up to it, and the schema those migrations produce (each column's type, default, comment, and seed policy, the domains with their checks, the keys and constraints, the indexes, and the schema comments; `docs/schema` is rendered from it as well) ([ADR 0034, lock file and branch-safe migration](adr/0034-seed-lock-file-and-branch-safe-migration.md)). `seeds:migrate`, parent first, for each node:

1. Build the database at the locked version (migrations 1 through N, a cached template) and apply the chain root to node.
2. Apply the next migration. The result is the node's migrated full state.
3. Export the node's layer as the diff against its migrated parent's full state.
4. Verify that the parent state plus the new layer equals the migrated full state. Write nothing, for any layer, if any layer fails.

Only layers whose diff changed are rewritten, and a per-layer summary is printed for review. Additive migrations need no seed work.

🚧 Built so far is the lock half. `mise run seeds:migrate` (`JoshOlson.Database/Migrate`) checks the lock against the migrations on disk (the same check `seeds:check` will run), checks that the dev database has applied exactly those migrations (Waypoint's history table: run `mise run waypoint migrate` first), reads its catalog, and writes the lock at the newest migration, keeping any accepted checksums. It reports every problem together and writes nothing if there is one. Moving layers (steps 1 to 4) is not built. With layers in `seeds/` it checks them instead of rewriting them: each layer is read against the schema the new lock describes (a value that no longer fits its column, an unknown column, a new `NOT NULL` column with no default) and built into a template from the migrations on disk (`SeedTool.CheckAsync`), and every layer that fails is reported, children of a failed layer skipped. The lock is written only when all of them apply. A layer a migration broke is fixed by hand. What this does not catch is a migration that changes what a column means and leaves the layer valid, which is what the export-and-verify steps are for.

Until the first deploy, migrations may be rewritten (`sql.md`), which leaves the lock's checksums stale and Waypoint refusing the changed file. Two commands cover that, both for the dev database only: `mise run db:reset` drops it and rebuilds it from the migrations (everything in it is lost), and `mise run seeds:relock` then writes the lock afresh from it, ignoring the old lock's version and checksums. It still checks that the history matches the files, and checks the layers the same way. Both are retired at the first deploy, when migrations become immutable.

If two branches claim the same migration number, `seeds:check` names the first migration that differs from the lock. Rename yours to the next number, take the main branch's `seeds/` and `migrations/schema.lock.yaml` wholesale, and rerun `seeds:migrate`.

If you only edit an already-applied migration's comment or whitespace, its checksum changes. `seeds:accept <version>` rebuilds with the edited file and, only if the catalog still equals the lock and every layer still exports to its own file, records the new checksum as accepted. If either differs, the edit changes something the seeds see, so add a new migration instead. The checksum is the SHA-256 of the raw file bytes; nothing is normalized.

## Export

`export-seed --parent <layer> --name <name>` diffs the live database against the parent's template and writes a layer; `--root` dumps everything. Primary keys are required on every table.

- The diff is as small as it can be: a row that is new is an upsert of every column; a row that exists in the parent's state and changed is an upsert of its key and only the columns that changed (an upsert changes only the columns it names, so the merge rebuilds the full row); a row that is gone is a delete of its key. A row that did not change is left out.
- The flow it is built for: `seed minimal`, use the app, `export-seed --parent minimal --name more-but-different-tags`. The new rows came from sequences that start at the next depth's block, so they already carry IDs `1001..` (see IDs above). The export refuses a new row whose ID is outside its layer's block (`IdBlocks.Contains`), naming the table and the ID, because a row at an ancestor's depth could collide with one the ancestor adds later, and an upsert would silently replace it.

- Relative columns are exported as offsets from the anchor stored in the database being exported (`seed.hydration`), never from `now()`, in exact microseconds. Output is canonical (sorted by primary key, columns in catalog order), so unchanged rows give identical text and `seeds:check` can require a byte-for-byte round trip.
- Tests on this assert at one-microsecond increments ([ADR 0035, typed values and relative times](adr/0035-typed-seed-values-and-relative-times.md)).
- `mise run seed <layer>` for the dev database applies the layer with the anchor set to now, so every relative column is current. Test clones keep the anchor they were baked with, which is also their `FakeClock` start.
- Fixtures for tests come from reading the baked template back with real types, not from the YAML.
