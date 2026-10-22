# ADR 0015: Seed ID blocks and sequence cursor

- Status: Accepted
- Date: 2026-10-21

## Context

A migration that generates identity values (for example splitting a skill into several) produces different IDs depending on the rows already present. Diffing a migrated child against a migrated parent by primary key then compares unrelated rows.

## Decision

Layers carry explicit IDs, inserted with `OVERRIDING SYSTEM VALUE` ([ADR 0006](0006-int-identity-always.md)), in per-depth blocks (root `1..`, depth 1 `1000..`, and so on). Explicit inserts do not advance an identity sequence, so while layers apply, the sequence stays positioned by the root's rows alone. A migration then generates the same IDs in a parent run and in a child run. Only when a database is finally built for use is each sequence set to the max ID.

## Alternatives considered

- Generated IDs in seeds: shift whenever a parent changes, which breaks tests that assert IDs.
- Diffing by natural key: needs a declared key per table and still has to renumber child rows.

## Consequences

- The tool must fail if a migration generates IDs that reach into a layer's block.
- A migration's own coalescing logic (merge an existing "git" with the split result) is replayed for every layer; the tool needs no special knowledge of it.
- Every seed becomes a test fixture for migrations.