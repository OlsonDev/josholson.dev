# ADR 0004: Explicit dev compose file

- Status: Accepted
- Date: 2026-10-21

## Context

Compose auto-loads `compose.override.yaml`. If the repository is cloned onto the server, a dev-only override would apply in production.

## Decision

`compose.yaml` is production-safe on its own. Dev-only settings (durability off, more connections) live in `compose.dev.yaml`, which is always passed explicitly with a second `-f`.

## Alternatives considered

- `compose.override.yaml`: conventional but auto-loaded.
- A name like `compose.unsafe.yaml`: describes the risk, not the purpose.

## Consequences

- A plain `up` on the server cannot pick up dev settings.
- A `bun run dev` wrapper should hide the two `-f` flags.
- The loopback-only Postgres port stays in `compose.yaml`, because production needs it for the DBeaver SSH tunnel.