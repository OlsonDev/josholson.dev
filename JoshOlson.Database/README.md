# Database

- `migrations/`: Waypoint migrations. `V#__schema.sql` are versioned, one per schema, applied in order. `R__*.sql` are repeatable and rerun when their contents change. `schema.lock.yaml` (🚧 not generated yet) is written by the seed tool, never by hand ([ADR 0034, lock file and branch-safe migration](../docs/adr/0034-seed-lock-file-and-branch-safe-migration.md)).
- `provisioning/`: one-off scripts run as the owner after migrations.
- `seeds/`: seed layers, one YAML file each. See [docs/seeding.md](../docs/seeding.md) for the format.
- `Layers/` and `Locks/`: the seed tool's code (parsing layers and building the `extends` tree; the lock file, migration checksums, and the check against the migrations on disk), with tests beside it ([ADR 0033](../docs/adr/0033-scoped-databases-per-test.md)). `mise run seeds:list` prints the tree.

Run against the dev database (start it first; see the root `CLAUDE.md`):

```
mise run waypoint migrate
mise run waypoint info
```

Conventions: [.claude/rules/sql.md](../.claude/rules/sql.md). Design notes: [docs/database.md](../docs/database.md).
