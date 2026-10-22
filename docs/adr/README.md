# Architecture decision records

One decision per file, numbered, never rewritten. To change a decision, add a new ADR and mark the old one `Superseded by NNNN`.

Template:

```
# ADR NNNN: Title

- Status: Accepted | Superseded by NNNN
- Date: YYYY-MM-DD

## Context
## Decision
## Alternatives considered
## Consequences
```

## Index

- [0001](0001-gitmoji-commit-messages.md): Gitmoji commit messages enforced by a hook
- [0002](0002-claude-does-not-commit.md): Claude does not commit
- [0003](0003-postgres-19-rc-on-debian-trixie.md): Postgres 19 RC on Debian trixie
- [0004](0004-explicit-dev-compose-file.md): Explicit dev compose file
- [0005](0005-waypoint-for-migrations.md): Waypoint for migrations
- [0006](0006-int-identity-always.md): `INT GENERATED ALWAYS` keys
- [0007](0007-year-optional-month-domain.md): `year_optional_month` domain
- [0008](0008-content-addressed-blobs.md): Content-addressed blobs
- [0009](0009-reference-data-in-repeatable-migrations.md): Reference data in repeatable migrations
- [0010](0010-global-tags-with-aliases.md): Global tags with aliases