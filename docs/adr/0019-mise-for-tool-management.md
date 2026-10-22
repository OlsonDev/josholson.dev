# ADR 0019: mise for tool management

- Status: Accepted
- Date: 2026-10-22

## Context

The project is developed on both Linux and Windows. It needs several CLIs at known versions (Bun, .NET, `gh`) and a few common commands that should behave the same on both systems. We want the fewest possible prerequisites.

## Decision

Use [mise](https://mise.jdx.dev). `mise.toml` pins `bun`, `dotnet`, and `gh`, and defines tasks as the shared interface (`mise run waypoint migrate`, `mise run schema:doc`). Tasks set up their own environment, so nobody has to run `mise activate`; it stays optional for people who want bare commands.

Podman stays a separate prerequisite. mise's registry entry installs `podman-remote` only, and rootless Podman needs system support (user namespaces, a container runtime, and a VM on Windows).

## Alternatives considered

- direnv with `.envrc`: another tool with its own shell hook, and Unix only.
- `setup.cs` detecting the OS and installing tools: brittle, and a setup script should not install software.
- Installing each tool by hand with the system package manager: versions drift between machines.
- `bin/` wrapper scripts on `PATH`: shell scripts do not run on Windows.

## Consequences

- Prerequisites are mise and Podman.
- Versions are pinned in one file. Changing a pin is a normal commit.
- mise's registry has a `waypoint` entry, but it is HashiCorp's deploy tool, not ours. Never `mise use waypoint`; see [ADR 0020](0020-waypoint-in-a-dev-tools-container.md).
- Extra arguments to a task are appended to its command (`mise run waypoint info --json`).
- Task commands must be plain command lines that work in both a POSIX shell and Windows.
