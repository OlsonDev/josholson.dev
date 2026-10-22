# Database

- `Migrations/`: Waypoint migrations. `V#__schema.sql` are versioned, one per schema, applied in order. `R__*.sql` are repeatable and rerun when their contents change.

Run against the dev database (start it first; see the root `CLAUDE.md`):

```
waypoint --url postgres://USER:PASSWORD@127.0.0.1:5432/DATABASE --locations Database/Migrations migrate
waypoint --url ... --locations Database/Migrations info
```

Conventions: [.claude/rules/sql.md](../.claude/rules/sql.md).