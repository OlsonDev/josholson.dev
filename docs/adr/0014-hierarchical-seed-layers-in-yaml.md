# ADR 0014: Hierarchical seed layers in YAML

- Status: Accepted
- Date: 2026-10-21
- Amended 2026-11-11: a layer's rows were `upsert`s, which were new rows when the key was new and changes when it was not, with any column left out filled in by its default. They are now `insert`s, `update`s, and `delete`s, and an insert names every column (below).

## Context

In past projects seeds multiplied, nobody knew what was in them, tests inserted irrelevant data, and every migration had to be applied to every seed.

An `upsert` hid three problems. A row a layer meant as new but whose key a parent already used overwrote the parent's silently. A column the row left out took its default, so a migration that changed a default changed the seed's data without any layer changing, and a `DEFAULT now()` time was whatever the clock said while the layer was applied, a few milliseconds that differed from run to run. And the rules for what a row must contain could not be told apart for a new row and a changed one.

## Decision

Seeds are a tree of layers. Each layer stores a delta against its parent in YAML, as three lists per table, keyed by primary key:

- **`insert`** is a new row. It names every column that is not generated from an expression (a NULL one as `~`), so nothing is left to a default, and a time is always an offset someone chose. Its key must not exist yet in the chain.
- **`update`** changes a row an earlier layer gives. It names the key and only the columns that change. Its key must exist, and it must change something.
- **`delete`** removes a row, and holds only the key.

In one layer a table's deletes apply first, then its inserts, then its updates. Layers compile to SQL. Export from a live database writes a layer as a diff against its parent (inserts for new rows, updates for changed ones, deletes for gone ones), or a root with `--root`.

## Alternatives considered

- Composition of independent mixins: IDs clash when two mixins touch the same table. May return later as multi-parent layers if the compiler verifies disjoint tables and IDs.
- Programmatic seeds: cannot be exported from a live database. A `.ts` layer that yields the same row model stays possible for generated bulk data.
- A data format other than YAML: CSV per table is better at scale. Revisit if layers grow past a few hundred rows.
- Keeping `upsert`, with the export always writing every column of a new row: a hand-written layer could still leave a default to fill in, and a collision of keys still overwrote.
- `insert` naming only the columns that are `NOT NULL` or have a default, with a nullable column that has neither left out as NULL: omission would be unambiguous, but the canonical form of a row would depend on each column's nullability and default, so a migration that changes either (a new default on a nullable column) would change what every existing row means. Naming every column has one form, whatever the schema says.

## Consequences

- Tests that need a little extra data insert it through a factory instead of adding a seed.
- Layers become partly machine-written; comments in row data may be lost on regeneration.
- Timestamps, decimals, JSON, and ranges need a canonical text encoding.
- An insert is longer than an upsert was, since every nullable column it has no value for says `~`. That is deliberate.
- A migration that adds a column rewrites every insert in every layer: `seeds:migrate` builds the database at the locked version, applies the layers, runs the migration, and exports each layer again, so the column appears in each insert and the diff records what the migration did to the seed data. Until then the layer check reports each insert that leaves it out.
- Because an insert is always a new row, its identity key can be held to the block of its layer's depth ([ADR 0015, seed ID blocks](0015-seed-id-blocks-and-sequence-cursor.md)) when the file is loaded, which an upsert could not tell from an override.
