# ADR 0035: Typed seed values, relative times, and fixtures from the baked template

- Status: Proposed
- Date: 2026-11-03

## Context

[ADR 0014 (hierarchical seed layers in YAML)](0014-hierarchical-seed-layers-in-yaml.md) left value encoding open ("a canonical text encoding"). Three problems follow from keeping every value as text:

- A test that wants "the page matches the database" has to compare strings, or each layer (fixture generation, Playwright) has to cast for itself.
- Timestamps in a seed go stale. Seed from `minimal`, add a few rows by hand, export, and the rows from `minimal` are months older than the new ones. Earlier tools stored a hydration time and exported relative to it, but re-exporting drifted by a millisecond or two and produced huge diffs.
- Tags on every value in a column would repeat what the column already says.

## Decision

- **A value's type is its column's type**, taken from the catalog (recorded in `schema.lock.yaml`, [ADR 0034, lock file and branch-safe migration](0034-seed-lock-file-and-branch-safe-migration.md)), not from YAML tags. Scalars are read as text and parsed per column, never through YAML's own inference. A plain `~`, `null`, or empty value is `NULL`; a quoted `"null"` is the string. `jsonb` is native YAML structure, arrays are sequences, composite types are mappings. Each type has one spelling, so a value is either right or a reported problem: integers are range-checked against their column; a `decimal(p, s)` column refuses more digits than it holds (Postgres would round them); an absolute `timestamptz` is a UTC instant no finer than a microsecond; `bytea` is `\x` and hex digits; `inet` is an address with an optional `/bits`; and in `jsonb` a plain `true`, `false`, `null`, or number is that JSON value and a quoted scalar is a string. Types are supported as they are used. Today that is `text`, `boolean`, `smallint`, `integer`, `bigint`, `decimal`, `timestamptz`, `bytea`, `jsonb`, and `inet`, with arrays of them and domains built on them.
- **Timestamps are relative or absolute per column, never per value.** Every `timestamptz` column is relative by default; every other type is absolute. A column opts out with a line in its `COMMENT ON COLUMN` that starts with `@seed absolute`. `schema:doc` strips `@seed` lines from the documentation text, and the lock records the resulting policy. A single value cannot be marked different from its column: the exporter has no way to know which values were which, so it would not round-trip.
- **A `seed` schema holds the anchor.** `seed.hydration` is a one-row table, created by the tool in baked templates only (never a migration, never in production), recording the layer, whether the database was built for editing it, the schema version, and the anchor. It is a table because a template copies it; `schema:doc`, `schema:check`, and exports skip the `seed` schema.
- **Relative values are signed offsets from the anchor**, in exact microseconds (Postgres's resolution), written as duration text such as `-3d 4h 12m 0.0123s`. The units are `d`, `h`, `m`, and `s`, lowercase and largest first, with one space before every unit but the first. A zero unit is left out, there are no leading zeros, and the seconds' fraction (up to six digits) drops its trailing zeros. Zero is `0s`, and a `-` applies to the whole duration. That is the form the exporter writes; the parser also takes `+`, leading zeros, and an oversized largest unit (`90m`), and rejects a fraction on anything but seconds, more than six fractional digits (nothing is rounded), and values beyond a 64-bit count of microseconds (about 292,000 years). `seeds:check` reports a file that is not in canonical form. The code is `DurationText` in `JoshOlson.Shared`. Every relative column of a database shifts by the same anchor, so checks between timestamps (verified not before created) keep holding.
- **Export is deterministic.** It computes offsets against the anchor stored in the database being exported, never `now()`, using integer arithmetic. The emitter is canonical: rows sorted by primary key, columns in catalog order, fixed formatting. `seeds:check` bakes every layer, exports it, and requires the output to equal the file byte for byte.
- **Precision is tested at the smallest unit.** Tests cover offsets of one microsecond around the anchor and around zero, across second boundaries, negative and far-past and far-future offsets, a randomized pass over microsecond offsets, and a real database round trip. `infinity` and `-infinity` are rejected.
- **The anchor is the test's "now".** `FakeClock` starts at the anchor. The test composition that honors the scope header ([ADR 0033, a scoped database per test](0033-scoped-databases-per-test.md)) serves the anchor as the API's clock, so Playwright and API tests see the same time as the data.
- **`seed <layer>` for the dev database shifts to now:** it clones the template, shifts every relative column by (now minus anchor), and records the new anchor. Test clones are not shifted.
- **Fixtures are built from the baked template**, not from the YAML. Bake reads each layer's rows back through the catalog with their real types and emits a typed module per layer (TypeScript for Playwright, and C# types where useful). The module is generated, not committed, and Playwright's global setup bakes first.

## Alternatives considered

- **Tags on values** (`!timestamp`, `!inet`): duplicates the column type, so every column type change would touch every seed value. Per-value timestamp modes also cannot round-trip.
- **Policy in a hand-edited `seeds/` config file:** can change without a migration, but drifts from the schema (a renamed column leaves a stale entry) and reinterprets existing values with no version bump.
- **The anchor in a database comment or `ALTER DATABASE ... SET`:** not obviously copied by `CREATE DATABASE ... TEMPLATE`. A table is.
- **Fixtures from the YAML:** needs no database, but the database also holds reference data from repeatable migrations, defaulted and generated columns, trigger results, and normalized values (`jsonb` key order, `numeric` scale, how `inet` prints). A page asserting against such a fixture can disagree with what it serves.

## Consequences

- The parser reads typed values with the lock, so it can no longer run with no schema. The lock makes that possible without a database.
- The earlier all-text layer parser is replaced; layers written before this ADR are rewritten by the first `seeds:migrate`.
- Fixtures need Postgres at bake time. Bake already does.
- Every new `timestamptz` column is relative unless commented otherwise, so a column that holds a fixed real date must say `@seed absolute`.
- Amended 2026-11-11: `@seed` has a second directive, on a table: `COMMENT ON TABLE ... IS '@seed ignore'` marks its rows as runtime data (sessions, credentials), not seed data. The lock records it (`seed: ignore`), no layer may name the table, and an export leaves it out. The same strictness applies: an unknown word after `@seed` is a problem, and `@seed ignore` on a column is one too.
