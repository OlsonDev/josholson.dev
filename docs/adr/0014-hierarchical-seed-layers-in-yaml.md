# ADR 0014: Hierarchical seed layers in YAML

- Status: Accepted
- Date: 2026-10-21

## Context

In past projects seeds multiplied, nobody knew what was in them, tests inserted irrelevant data, and every migration had to be applied to every seed.

## Decision

Seeds are a tree of layers. Each layer stores a delta against its parent (upserts and deletes by primary key) in YAML. Layers compile to SQL. Export from a live database writes a layer as a diff against its parent, or a root with `--root`.

## Alternatives considered

- Composition of independent mixins: IDs clash when two mixins touch the same table. May return later as multi-parent layers if the compiler verifies disjoint tables and IDs.
- Programmatic seeds: cannot be exported from a live database. A `.ts` layer that yields the same row model stays possible for generated bulk data.
- A data format other than YAML: CSV per table is better at scale. Revisit if layers grow past a few hundred rows.

## Consequences

- Tests that need a little extra data insert it through a factory instead of adding a seed.
- Layers become partly machine-written; comments in row data may be lost on regeneration.
- Timestamps, decimals, JSON, and ranges need a canonical text encoding.