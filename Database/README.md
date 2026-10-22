# Database

- `Migrations/`: Waypoint migrations. `V#__schema.sql` are versioned, one per schema, applied in order. `R__*.sql` are repeatable and rerun when their contents change.
- `Seeds/`: seed layers (🚧 tool not built yet). See [docs/seeding.md](../docs/seeding.md).

Run against the dev database (start it first; see the root `CLAUDE.md`):

```
mise run waypoint migrate
mise run waypoint info
```

Conventions: [.claude/rules/sql.md](../.claude/rules/sql.md). Design notes: [docs/database.md](../docs/database.md).