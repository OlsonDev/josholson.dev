# ADR 0012: Enum-like columns as `SMALLINT`

- Status: Accepted
- Date: 2026-10-21

## Context

Several columns hold one of a small fixed set of values (employment type, publication type, language proficiency, project status). They map to 1-based enums in C#.

## Decision

Store them as `SMALLINT` with a range check, `BETWEEN 1 AND n`, named `ck_{table}_{column}_range`. Postgres has no `TINYINT`; `SMALLINT` is its smallest integer type. Zero is never valid, so an unset value cannot hide as 0. Optional columns are `NULL`. The C# enum is authoritative, and `docs/database.md` mirrors the mapping.

## Alternatives considered

- Postgres `ENUM` types: values can be added but not removed or reordered easily, and they need an Npgsql mapping.
- Lookup tables with foreign keys: self-describing but add tables and joins for tiny sets.
- `TEXT` with an `IN (...)` check: self-describing in `psql`, but wider and not an enum in C#.

## Consequences

- Values are numbers in `psql`, DBeaver, and seed YAML. Keep the mapping table up to date.
- Adding a value needs a migration that widens the range. Never reuse a retired number.
- A `TEXT` column whose set of values becomes fixed is a candidate to convert.
