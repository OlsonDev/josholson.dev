# ADR 0021: Generated schema docs

- Status: Accepted
- Date: 2026-10-22

## Context

`docs/database.md` listed every schema and its contents by migration number. That restated what the migrations already say, and it went stale whenever a migration changed. We want the current structure documented, with decisions left to ADRs, and rendered the way we want to read it: constraints split by kind, foreign keys as tables, types linked to the PostgreSQL manual, and domains documented.

## Decision

`scripts/schema-docs.cs` reads the catalogs directly (Npgsql, declared with `#:package` in the file-based app) and writes all the Markdown itself, committed in `docs/schema/`. `docs/database.md` keeps design notes only and points there.

- **One page per table**, named `{schema}.{table}.md`, plus `domains.md` and a `README.md` index grouped by schema with a diagram of every relation.
- **Columns**: Name, Type, Nullable, Default, in that order, with a Comment column only when some column has a comment. Nullable reads `NULL` or `NOT NULL` and is right-aligned. Default is last because it can be long, and identity and generated columns say so there.
- **Constraints** are split into Primary key, Unique constraints, and Check constraints, each a Name and Definition table. The definition drops the keyword and the parentheses Postgres adds. `NOT NULL` is not a constraint here; the Columns table shows it.
- **Indexes** are one `sql` block with each clause on its own line. Indexes that back a primary key or unique constraint are left out, since the constraint tables list them. A table cell cannot hold a code block, and the HTML `<table>` trick that lets GitHub highlight code inside cells makes the raw Markdown hard to read, so plain tables use inline code.
- **Relations** has Foreign keys (Name, Columns, Ref Table, Ref Columns, On Delete, On Update), Incoming foreign keys (the same, with the referencing table), and a Mermaid Diagram. A Deferrable column appears only when a key is deferrable. This replaces the Children and Parents columns.
- **Links** are Markdown reference definitions at the bottom of each file, labelled by kind: `pg:{type}` for the PostgreSQL manual (the version in the URL matches [ADR 0003](0003-postgres-19-rc-on-debian-trixie.md)), `domain:{schema.name}` for a domain's heading in `domains.md`, linked by the heading slug GitHub generates (`text.trimmed` becomes `#texttrimmed`). An explicit `<a id>` anchor was tried and did not scroll in VS Code's preview, and `table:{schema.name}` for a table page. Only labels a page uses are defined.
- Table cells are padded from their final text, so alignment holds whatever markup a cell carries.
- `mise run schema:doc` regenerates the docs. `mise run schema:check` regenerates into a temporary directory and compares it with `docs/schema` using `git diff --no-index`.

## Alternatives considered

- A hand-maintained table in `database.md`: stale within a few migrations.
- [tbls](https://github.com/k1LoW/tbls), which we tried first, then wrapped in a script that edited its JSON and Markdown:
  - It does not export domains.
  - Its table rows are pre-rendered strings, so custom templates can filter and reorder but not restructure.
  - It prints Postgres 18+ `NOT NULL` constraints with the raw type `n`.
  - Foreign keys arrive as definition text.
  - Every tweak meant parsing its Markdown back into cells, which breaks whenever its layout changes.
- SchemaSpy: Java, and its output is a site, not Markdown to commit.
- mermerd: diagrams only.
- Atlas: more machinery than documentation needs.

## Consequences

- We own the renderer and maintain it. The catalog queries are standard (`pg_attribute`, `pg_constraint`, `pg_index`, `pg_type`) but need checking after a Postgres major version.
- The first run restores the Npgsql package, so it needs network access once.
- Type links cover the built-in types in the `TypeDocs` table in the script. A type that is neither in that table nor a domain (a less common built-in, or an enum) is shown without a link, and the script prints a warning naming it. Add it to `TypeDocs` to link it. Every type in use links today.
- Built-in types are capitalized everywhere (`INTEGER`, `TIMESTAMPTZ`, `VARCHAR(20)`), using the short names Postgres accepts for the multi-word ones (Mermaid forbids spaces). Domains and other types keep the names Postgres prints (`text.kebab_case`).
- Mermaid attributes are fixed as type, name, key, so the key (PK, FK, UK) cannot come first, and attribute text takes no Markdown formatting.
- The check no longer sees the Postgres version, so a Postgres upgrade is not drift by itself.
- Every Mermaid diagram parsed successfully with `mermaid.parse` when this was written. That check is not automated.
- Nothing runs the drift check automatically yet; a pre-push hook or CI job could.
- Regenerating needs the dev database running with migrations applied.
