# ADR 0015: Seed ID blocks and sequence cursor

- Status: Accepted
- Date: 2026-10-21
- Amended 2026-11-11: the blocks were stated as "root `1..`, depth 1 `1000..`, and so on", which left depth 2 unsaid and made the root a special case, and a database built for use took its sequences from the chain's largest ID, which sent rows created in it (and so an export of them) into the wrong block. Both are fixed below.

## Context

A migration that generates identity values (for example splitting one row into several) produces different IDs depending on the rows already present. Diffing a migrated child against a migrated parent by primary key then compares unrelated rows.

A layer is keyed by primary key, so two layers that each add a row with the same ID are not two rows. Under the original `upsert` the later one silently overwrote the earlier; with separate `insert` and `update` ([ADR 0014, hierarchical seed layers](0014-hierarchical-seed-layers-in-yaml.md)) it is an error, and the block rule below is what keeps it from happening by accident.

## Decision

Layers carry explicit IDs, inserted with `OVERRIDING SYSTEM VALUE` ([ADR 0006](0006-int-identity-always.md)), in per-depth blocks of 1000. A layer's depth is how many parents it has (the root is 0), and depth `d` owns IDs `d × 1000 + 1` through `(d + 1) × 1000`: the root `1..1000`, depth 1 `1001..2000`, depth 2 `2001..3000`, and so on, the same for every depth. Siblings share a block, since they never coexist. A block is for the rows a layer inserts; an update of a row an ancestor added keeps that row's ID.

Explicit inserts do not advance an identity sequence, so something positions them, at two moments:

- **While layers apply for a migration**, each sequence stays positioned by the root's rows alone, so a migration generates the same IDs in a parent run and in a child run.
- **When a database is built from a layer for use** (the dev database, a test's, a template), every identity sequence in every table is set to hand out the first ID of the *next* depth's block, or the ID after the chain's largest if that is higher. A row created in that database, in the app or by a test, then lands where an export of a child layer keeps its new rows (`seed minimal` gives a new tag `1001`, `seed more-tags` gives `2001`), and a table no layer has rows in starts there too.

The block is enforced for a table whose primary key is one identity column: loading a layer refuses an `insert` whose ID is outside its layer's block (`LayerTree.IdBlockProblems`), and an export refuses a new row the same way.

## Alternatives considered

- Generated IDs in seeds: shift whenever a parent changes, which breaks tests that assert IDs.
- Diffing by natural key: needs a declared key per table and still has to renumber child rows.
- Positioning a built database by the chain's largest ID (the first version of this ADR): a layer authored by seeding its parent, adding rows in the app, and exporting would take IDs in its parent's block, where a later change to the parent collides with it, and where a migration that generates IDs reaches into them.
- Renumbering an export's new rows into the block: needs every foreign key that points at them rewritten, and makes the IDs differ between the database and the file.
- Blocks that start at the round numbers (`1000`, `2000`) with a shorter root: the root block is one ID shorter and every other layer's rule has an exception.

## Consequences

- The tool must fail if a migration generates IDs that reach into a layer's block.
- A migration's own coalescing logic (merge an existing "git" with the split result) is replayed for every layer; the tool needs no special knowledge of it.
- Every seed becomes a test fixture for migrations.
- IDs in a dev or test database start high (`1001`, `2001`), and an ID's block says how deep the layer that added it is.
- A layer can add at most 1000 rows to a table at its depth. A `smallint` identity column reaches depth 31, and an `integer` one is not a limit in practice.
