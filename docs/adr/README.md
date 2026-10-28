# Architecture decision records

One decision per file, numbered, never rewritten. To change a decision, add a new ADR and mark the old one `Superseded by NNNN`.

Examples in ADRs follow the metasyntactic-names rule in [CLAUDE.md](../../.claude/CLAUDE.md). Name a real thing only where the ADR decides about it.

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
- [0011](0011-resumes-own-their-sections.md): Resumes own their sections
- [0012](0012-enum-like-columns-as-smallint.md): Enum-like columns as `SMALLINT`
- [0013](0013-single-postgres-server-with-template-databases.md): One Postgres server, template databases
- [0014](0014-hierarchical-seed-layers-in-yaml.md): Hierarchical seed layers in YAML
- [0015](0015-seed-id-blocks-and-sequence-cursor.md): Seed ID blocks and sequence cursor
- [0016](0016-table-prefixed-keys-and-junction-names.md): Table-prefixed keys and `__` in compound names
- [0017](0017-domains-by-kind.md): Domains for repeated validation, one schema per kind
- [0018](0018-dml-only-api-role.md): DML-only API role
- [0019](0019-mise-for-tool-management.md): mise for tool management
- [0020](0020-waypoint-in-a-dev-tools-container.md): Waypoint in a dev tools container
- [0021](0021-generated-schema-docs.md): Generated schema docs
- [0022](0022-branch-names-enforced-by-a-hook.md): Branch names enforced by a hook
- [0023](0023-api-feature-folders-and-colocated-tests.md): API feature folders and colocated tests
