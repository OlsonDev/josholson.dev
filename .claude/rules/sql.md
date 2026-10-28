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

## Queries

Formatting for `SELECT`. `INSERT`, `UPDATE`, and `DELETE` are still to be decided. The `Formatting` rules above also apply here.

This one example shows every rule below. Study all of it before reading the rules, and look back at it while writing a query.

```sql
-- Bars with no grault, or whose grault is retired
SELECT
    b.bar_id     AS bar_id
  , b.name       AS name
  , z.name       AS baz_name
  , g.grault_id  AS grault_id
  , EXISTS (
      SELECT 1
      FROM (SELECT) AS _
      JOIN foo.qux  AS q  ON q.bar_id   = b.bar_id
                         AND q.is_thud  = TRUE
    )            AS has_qux
FROM (SELECT) AS _
JOIN foo.bar          AS b  ON b.status     IN (1, 2)
                           AND b.sort_order <  10
                           AND (    (     b.corge_status   = 1
                                      AND b.garply_status IN (2, 3)
                                    )
                                 OR (     b.waldo_updated_utc <  b.plugh_updated_utc
                                      AND b.is_thud
                                 )
                                 OR b.fred_note LIKE 'xyzzy%'
                           )
JOIN foo.baz          AS z  ON z.baz_id      = b.baz_id
                           AND z.name       != 'corge'
                           AND (z.is_thud OR z.status = 3)
LEFT JOIN foo.grault  AS g  ON g.bar_id      = b.bar_id
                           AND g.waldo_id    = z.waldo_id
WHERE g.grault_id IS NULL
   OR ( g.retired_utc  <  b.plugh_updated_utc
    AND g.corge_status IN (4, 5, 6)
    AND g.fred_note    LIKE 'xyzzy%'
    AND g.waldo_id     != z.waldo_id
   )
ORDER BY b.sort_order, b.bar_id;
```

### Structure

- Comments are sentence cased: `-- Tables`, not `-- tables`.
- `FROM`, `JOIN`, `WHERE`, `GROUP BY`, and `ORDER BY` start at the statement's indentation. Nothing under `FROM` is indented. `ON` stays on its `JOIN` line.
- The `FROM` is always `FROM (SELECT) AS _`, a throwaway row. Every real source is a `JOIN`.
  - A condition goes in the first `ON` it can, beside the source it filters. Without the throwaway `FROM`, the first source has no `ON`, so its filters would go in a `WHERE`, far from the source in a long query. Reordering the sources would then mean moving filters between the `WHERE` and an `ON`.
  - Commenting out a `JOIN` is one operation.
  - A source with no condition of its own takes `ON TRUE`.
- `WHERE` only where a condition cannot live in an `ON`, such as checking that a `LEFT JOIN` found nothing (`WHERE g.grault_id IS NULL`). Any other `WHERE` on a left-joined table silently makes it an inner join.
- Write `JOIN` and `LEFT JOIN`, never `INNER JOIN` or `LEFT OUTER JOIN`.
- Banned:
  - `NATURAL JOIN` joins on every column the tables share. A new column, such as `name`, changes what the query means, and reordering the `JOIN`s changes it too, because it considers every earlier source.
  - `USING` cannot be combined with `ON`. The moment a query needs one more condition, the whole `USING` becomes an `ON`, and the diff is far larger than the change.
  - `CROSS JOIN` and comma joins. Use `JOIN ... ON TRUE`.
- Every source has `AS` and an alias, subqueries and functions included.

### Alignment

Alignment is per `FROM` block, across all its `JOIN`s.

- `AS` is two spaces after the longest `JOIN ... source`. `ON` is two spaces after the longest alias, which includes a column list (`k (attnum, ord)`).
- A continued condition goes on its own line, and `AND`/`OR` is right-aligned with the `N` of `ON`. `AND` starts one column left of `ON`, and `OR` directly under it.
- In every condition, the left side names the `JOIN`'s own alias first and earlier aliases second (`z.baz_id = b.baz_id` in the `baz` join), so the aliases repeat down the page.
- Operators sit in a two-character column, one space after the longest left side in the block:
  - `=` is right-aligned (` =`).
  - `<` and `>` are left-aligned (`< `), which leaves a space after them.
  - `!=`, `<=`, `>=`, and `IN` fill the column.
- Everything else is not aligned:
  - Longer operators (`NOT IN`, `LIKE`, `NOT LIKE`) start in the same column and run past it. Their right side sits further right.
  - Conditions without a left-right shape (`NOT a.is_thud`, `x IS NULL`, `TRUE`) start where a left side would.

Combining conditions:

- Parenthesize any mix of `AND` and `OR`. A group has one connective, so never nest a group inside a group with the same one; merge it into the parent.
- A plain group holds no group and sits in no group. It stays on one line, unaligned inside, when the whole line fits in 120 columns (the `baz` join).
- A plain group that does not fit is laid out like `ON`, with `(` in the place of `ON` (the `WHERE`):
  - The first condition follows `( `.
  - `AND`/`OR` is right-aligned with the `(`.
- A nested group holds a group or sits in one (the `bar` join). It is always multi-line, however short:
  - `AND`/`OR` starts one space after the `(`, and each condition follows one space after it. A group has a single connective, so the conditions line up.
  - The first condition is padded to start in the same column, and a nested `(` opens in that column too.
- In every group, operators align in a column of their own, sized to the longest left side in that group.
- `)` goes on its own line, under the first letter of the word before the `(`: `ON`, `WHERE`, `HAVING`, `AND`, or `OR`, so it points back to the connective that introduced the group. A group that opens its parent group has no such word, so `)` goes under its own `(`.
- `WHERE` and `HAVING` work like `ON`, but right-align `AND`/`OR` with their own last letter: the `E` of `WHERE`, the `G` of `HAVING`. Conditions follow `WHERE ` or `HAVING `, and their operators align in a column of their own, sized to the longest left side in that clause. `OR` can start a continued condition there, as it can in an `ON`; the example's `WHERE` does.
- Adding a nested group to a plain group, or removing the last one, re-lays out the groups around it. That is a whitespace-only change; keep it apart from changes to the logic.

### Select list

- Comma-first, one column per line, the first column on the line after `SELECT`.
- `AS` is two spaces after the longest line that carries an `AS`: a column that fits on one line, however complex, or the closing `)` line of a multi-line column (a subquery). The interior lines of a multi-line column do not count, so they may run past the `AS` column. The closing `)` sits at the column's own indentation, and its `AS` joins the same column.
- Every column of a query's outermost `SELECT` has an `AS`, even when the alias repeats the column name. The `AS` column becomes the list of result names, readable in one pass. Without it, unaliased columns sit at the far left and aliased ones at the far right, and the eye zigzags.
- Subqueries are indented two spaces from where the expression starts. Their `FROM`, `JOIN`, and `WHERE` follow the same rules.

## Naming

- snake_case everywhere except C# @parameterValues, which are camelCase.
- Always schema-qualify objects (tables, views, functions, types). Reasons:
  - `search_path` safety: an unqualified name resolves through `search_path`, which a role or session can change, so another schema's object of the same name could shadow yours. Qualified names cannot be shadowed.
  - Plan reuse: a cached plan is tied to the `search_path` it was built under, and Postgres replans when that changes. Qualified names remove the dependency. The effect is small, and plans are cached per connection, not shared.
  - Searchability: `grep foo.bar` finds every use.
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
