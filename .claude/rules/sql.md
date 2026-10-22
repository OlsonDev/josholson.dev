---
paths:
  - "Database/**/*.sql"
---

# SQL

## Example

Most conventions below, in one table (names are metasyntactic, see CLAUDE.md):

```sql
CREATE TABLE foo.bar (
    bar_id         INT                      NOT NULL GENERATED ALWAYS AS IDENTITY
  , baz_id         INT                      NOT NULL
  , grault_id      INT                          NULL
  , corge_quux_id  INT                          NULL
  , sort_order     INT                      NOT NULL DEFAULT 0
  , name           foo_schema.bar_domain    NOT NULL
  , note           foo_schema.baz_domain        NULL
  , wibble         foo_schema.bar_domain[]  NOT NULL DEFAULT '{}'
  , status         SMALLINT                     NULL
  , is_thud        BOOLEAN                  NOT NULL DEFAULT FALSE
  , updated_utc    TIMESTAMPTZ                  NULL
  , created_utc    TIMESTAMPTZ              NOT NULL DEFAULT now()

  , CONSTRAINT pk_bar PRIMARY KEY (bar_id)
  , CONSTRAINT uq_bar_baz_id_name UNIQUE (baz_id, name)
  , CONSTRAINT fk_bar__baz       FOREIGN KEY (baz_id)        REFERENCES foo.baz (baz_id)       ON DELETE CASCADE
  , CONSTRAINT fk_bar__grault    FOREIGN KEY (grault_id)     REFERENCES foo.grault (grault_id) ON DELETE SET NULL
  , CONSTRAINT fk_bar__qux__quux FOREIGN KEY (corge_quux_id) REFERENCES qux.quux (quux_id)
  , CONSTRAINT ck_bar_status_range               CHECK (status BETWEEN 1 AND 5)
  , CONSTRAINT ck_bar_updated_not_before_created CHECK (updated_utc >= created_utc)
  , CONSTRAINT ck_bar_wibble_no_nulls            CHECK (array_position(wibble, NULL) IS NULL)
);

CREATE INDEX ix_bar_baz_id_sort_order
  ON foo.bar (baz_id, sort_order);

CREATE INDEX ix_bar_grault_id
  ON foo.bar (grault_id)
  WHERE grault_id IS NOT NULL;

CREATE INDEX ix_bar_corge_quux_id
  ON foo.bar (corge_quux_id)
  WHERE corge_quux_id IS NOT NULL;
```

## Formatting

- Two-space indent. Keywords and data types UPPERCASE (`INT`, `TEXT`, `BOOLEAN`, `TIMESTAMPTZ`, `TRUE`). Functions lowercase (`now()`, `lower()`).
- Comma-first. The first item is indented so it lines up with the items after the comma.
- `CREATE TABLE`: columns, one blank line, then constraints.
- Columns: data types start two spaces after the longest column name. `NULL`/`NOT NULL` are right-aligned two spaces after the longest data type. Alignment is per table.
- Foreign keys are one line. Align `FOREIGN KEY` one space after the longest `fk_` name, `REFERENCES` one space after the longest key list, and `ON DELETE` one space after the longest `schema.table (key)` reference.
- Align `CHECK` one space after the longest `ck_` name.
- Primary and unique constraints are not aligned.
- `CREATE INDEX` is split over lines, in this order. Omit the clauses an index does not have:

  ```sql
  CREATE [UNIQUE] INDEX {ix|ux}_{table}_{columns}
    ON schema.table (key1, key2)
    INCLUDE (col1, col2)
    WHERE condition;
  ```
- Short inline lists such as `(baz_id, name)` stay on one line.
- `!=`, never `<>`.
- State nullability on every column: `NOT NULL` or `NULL`. No implicit nullability.
- No quoted identifiers. Reserved words such as `user` work unquoted when schema-qualified (`foo.user`); a bare `user` means `CURRENT_USER`.
- No comments in migrations yet. This is a deliberate choice for now.

## Naming

- snake_case everywhere. Always schema-qualify objects.
- Primary key: `{table}_id`.
- Foreign key columns keep the referenced key's name (`baz_id`), with a role prefix when needed (`corge_baz_id`).
- Booleans: `is_*`.
- Timestamps: `*_utc`.
- A double underscore separates whole table names. Junction tables are `{a}__{b}`, or `{a}__{schema}__{b}` when `b` is in another schema (`foo.bar__qux__quux`).
- Constraints are named:
  - `pk_{table}`
  - `uq_{table}_{columns}`
  - `fk_{table}__{referenced}`, where `{referenced}` is the table, prefixed `{schema}__` when it is in another schema (`fk_bar__baz`, `fk_bar__qux__quux`)
  - `ck_{table}_{rule}`
    - enum-like range: `ck_{table}_{column}_range`
    - array: `ck_{table}_{column}_no_nulls`
    - timestamp order: `ck_{table}_{column}_not_before_{other}`, with column names minus `_utc`
- Indexes: `ix_{table}_{columns}`. Unique indexes (partial or expression-based, which a `UNIQUE` constraint cannot express) use `ux_`.

## Data types

- Primary key: `INT NOT NULL GENERATED ALWAYS AS IDENTITY`.
- Booleans are `BOOLEAN`.
- Timestamps are `TIMESTAMPTZ`.
- Enum-like columns are `SMALLINT` (Postgres has no `TINYINT`) holding a 1-based C# enum value, with a range check (`BETWEEN 1 AND n`). The C# enum is authoritative; mirror new values in `docs/database.md`. Never reuse a retired number.
- Use a domain instead of repeating a check (see Domains).

## Domains

Each lives in a schema named for the kind of value. Sorted by name:

- `color.hex`: `#rrggbb`, lowercase
- `hash.sha256`: exactly 32 bytes
- `jsonb.object`: a JSON object, not an array or scalar
- `temporal.year_optional_month`: partial dates, `YYYY` or `YYYY-MM`
- `text.email`: `x@y.z`, no whitespace, at most 254 characters
- `text.kebab_case`: slugs
- `text.trimmed`: every other `TEXT` column, non-empty with no surrounding whitespace; arrays are `text.trimmed[]`
- `text.url`: `http`/`https` with a host, at most 2048 characters

Plain `TEXT` only when the column has its own stricter check. A repeated rule becomes a new domain, not a copied check.

## Constraints and indexes

- Constraints sit at the end of `CREATE TABLE`, grouped in this order: primary key, unique, foreign keys, checks. `DEFAULT`, `NOT NULL`, and identity stay inline on the column, because Postgres cannot name them.
- Array columns carry a `_no_nulls` check (`array_position(col, NULL) IS NULL`), since domain checks skip `NULL` elements.
- A nullable timestamp that follows another carries a `_not_before_` check.
- Postgres does not index foreign keys. Index every FK column; make the index partial (`WHERE col IS NOT NULL`) when the column is nullable.
- Junction tables have a composite primary key and a reverse index `({b}_id, {a}_id)`.
- Ordered children carry `sort_order INT NOT NULL DEFAULT 0` and an index on `({parent}_id, sort_order)`.

## Migrations

- One versioned migration per schema, named `V{nnn}__{schema}.sql` (zero-padded to three digits so tools sort them correctly), ordered so a schema depends only on earlier ones.
- Before the first deploy, existing migrations may be rewritten. After that they are immutable: Waypoint validates checksums.
- Reference data lives in repeatable migrations as idempotent upserts (`ON CONFLICT`), not in seeds.