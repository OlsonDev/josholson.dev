# ADR 0020: Waypoint in a dev tools container

- Status: Accepted
- Date: 2026-10-22

## Context

[ADR 0005](0005-waypoint-for-migrations.md) chose Waypoint. Its releases (0.8.1) ship Linux and macOS binaries only, so it cannot be installed on Windows through mise. mise's registry entry named `waypoint` is a different tool.

## Decision

Run Waypoint in a long-running `tools` service in `compose.dev.yaml`, built from `tools/Containerfile`:

- The image downloads one pinned Waypoint release for the build architecture and verifies it against the release `SHA256SUMS`. The version is the `WAYPOINT_VERSION` build argument.
- Inside the container, `waypoint` is a wrapper that adds `--url` and `--locations`. The URL is built from the `POSTGRES_*` variables, percent-encoded, with the host `postgres` on the `compose` network. `Database/Migrations` is mounted read-only at `/migrations`.
- The container sleeps and commands use `exec`, so there is no container creation cost per command.
- It is used through `mise run waypoint <args>`, which starts the container first if needed.
- `NO_COLOR=1` is set because Waypoint's `info` table misaligns when a cell is colored. Its `comfy-table` dependency lacks the `custom_styling` feature, so it counts ANSI escape bytes as visible width. Remove the setting once that is fixed upstream.

## Alternatives considered

- `run --rm` for every command: pays container creation and teardown each time.
- WSL on Windows: an extra prerequisite for one tool.
- Building from source with Rust: a toolchain for one tool.
- The mise `github:` backend: works on Linux and macOS, but there is no Windows asset.

## Consequences

- Dev only. The `tools` service must never be loaded on the server. How migrations run on the server is not defined yet.
- A Waypoint upgrade is a build argument change plus `podman compose build tools`.
- The password is passed to the container as environment variables from `.env`.
