# ADR 0016: Table-prefixed keys and `__` in compound names

- Status: Accepted
- Date: 2026-10-21

## Context

A bare `id` primary key makes every join and `USING` clause ambiguous, and every foreign key column differs from the key it points at. Junction names like `foo_bar_baz` do not say where one table name ends, and the ambiguity grows with every new table.

## Decision

- A primary key is named `{table}_id` (`foo.bar.bar_id`). A foreign key column to it normally keeps that name, with a role prefix when needed (`corge_baz_id`). This amends [ADR 0006](0006-int-identity-always.md), which still defines the type.
- A double underscore separates whole table names. A junction table is `{a}__{b}`. When `b` lives in another schema it is `{a}__{schema}__{b}` (`foo.bar__qux__quux`).
- Foreign key constraints follow the same shape: `fk_{table}__{referenced}`, where `{referenced}` is the table name, prefixed `{schema}__` only when it lives in another schema (`fk_bar__baz`, `fk_bar__qux__quux`).

## Alternatives considered

- Keep `id`: shortest, but joins read `a.id = b.a_id` and `USING` is impossible.
- Name foreign keys after the column (`fk_bar_corge_baz`): keeps the role, but needs the single-underscore convention we are leaving.

## Consequences

- Names grow. Postgres truncates identifiers at 63 bytes, so check the longest constraint and index names when adding tables.
- Two foreign keys from one table to the same table would collide on `fk_{table}__{referenced}`. When that happens, append the column role to both.
- Single `_` still joins words inside a name; `__` is reserved for separating names.