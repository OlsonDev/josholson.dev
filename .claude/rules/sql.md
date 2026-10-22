---
paths:
  - "Database/**/*.sql"
---

# SQL

## Formatting

- Two-space indent. Keywords and data types UPPERCASE (`INT`, `TEXT`, `BOOLEAN`, `TIMESTAMPTZ`, `TRUE`). Functions lowercase (`now()`, `lower()`).
- Comma-first. The first item is indented so it lines up with the items after the comma:

  ```sql
  CREATE TABLE tag.alias (
      id INT NOT NULL GENERATED ALWAYS AS IDENTITY
    , tag_id INT NOT NULL
    , CONSTRAINT pk_alias PRIMARY KEY (id)
  );
  ```

  Continuation lines within one item indent two further (`REFERENCES ...` under its `FOREIGN KEY`). Short inline lists such as `(id, resume_id)` stay on one line.
- `!=`, never `<>`.
- State nullability on every column: `NOT NULL` or `NULL`. No implicit nullability.
- No quoted identifiers. Reserved words such as `user` work unquoted when schema-qualified (`auth.user`); a bare `user` means `CURRENT_USER`.
- No comments in migrations yet. This is a deliberate choice for now.

## Naming

- snake_case everywhere. Always schema-qualify objects.
- Primary key: `id INT NOT NULL GENERATED ALWAYS AS IDENTITY`.
- Foreign key columns are `{table}_id`. Booleans are `is_*`. Timestamps are `TIMESTAMPTZ` named `*_utc`.
- Constraints are named, and sit at the end of `CREATE TABLE`:
  - `pk_{table}`, `uq_{table}_{columns}`, `fk_{table}_{referenced}` (the FK column without `_id`), `ck_{table}_{rule}`
  - `DEFAULT`, `NOT NULL`, and identity stay inline on the column, because Postgres cannot name them.
- Indexes: `ix_{table}_{columns}`. Unique indexes (partial or expression-based, which a `UNIQUE` constraint cannot express) use `ux_`.
- Postgres does not index foreign keys. Index every FK column; make the index partial (`WHERE col IS NOT NULL`) when the column is nullable.
- Junction tables are `{a}_{b}` with a composite primary key and a reverse index `({b}_id, {a}_id)`.
- Ordered children carry `sort_order INT NOT NULL DEFAULT 0` and an index on `(parent_id, sort_order)`.

## Migrations

- One versioned migration per schema, named `V{n}__{schema}.sql`, ordered so a schema depends only on earlier ones.
- Before the first deploy, existing migrations may be rewritten. After that they are immutable: Waypoint validates checksums.