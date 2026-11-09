# Database

- `migrations/`: Waypoint migrations. `V#__schema.sql` are versioned, one per schema, applied in order. `R__*.sql` are repeatable and rerun when their contents change. `schema.lock.yaml` (🚧 not generated yet) is written by the seed tool, never by hand ([ADR 0034, lock file and branch-safe migration](../docs/adr/0034-seed-lock-file-and-branch-safe-migration.md)).
- `provisioning/`: one-off scripts run as the owner after migrations.
- `seeds/`: seed layers, one YAML file each. See [docs/seeding.md](../docs/seeding.md) for the format.
- `Layers/`, `Locks/`, `Schema/`, `Catalog/`, `Migrate/` and `Seeding/`: the seed tool's code (parsing layers into typed values and building the `extends` tree; merging a chain of layers into rows and writing them as `INSERT` statements; the lock file, migration checksums, and the check against the migrations on disk; the schema model the lock serializes: tables, columns, keys; the reader that builds that model from a database's catalogs; and `seeds:migrate`, which checks the dev database is at the newest migration and writes the lock), with tests beside it ([ADR 0033](../docs/adr/0033-scoped-databases-per-test.md)). `mise run seeds:list` prints the tree and `mise run seeds:migrate` makes the lock.

Run against the dev database (start it first; see the root `CLAUDE.md`):

```
mise run waypoint migrate
mise run waypoint info
```

Conventions: [.claude/rules/sql.md](../.claude/rules/sql.md). Design notes: [docs/database.md](../docs/database.md).
