# ADR 0003: Postgres 19 RC on Debian trixie

- Status: Accepted
- Date: 2026-10-21

## Context

The project is new and has no data to migrate. Postgres 19 (release candidate) adds temporal features we may use. The official image comes in Debian (`trixie`, `bookworm`) and Alpine variants.

## Decision

Use `postgres:19rc1-trixie`, with the distro named explicitly so a future change of the unsuffixed default cannot change the base under an existing volume.

## Alternatives considered

- Alpine: musl only supports the C/POSIX locales for libc collation, so sort order can differ from Debian, and swapping base images under a volume can break indexes and collation versions. The size saving is small.
- `bookworm`: older Debian; no reason to prefer it.
- `18`: stable, but we want 19's features.

## Consequences

- Treat data in the volume as disposable until 19 is GA; moving from an RC to the final release may need a dump/restore.
- At GA, repin to a final tag such as `19-trixie` or a full `19.x-trixie`.
- Choose the distro once and stay on it.