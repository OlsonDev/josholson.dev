# josholson.dev

Personal website for Josh Olson: www.josholson.dev (SvelteKit SSR) and api.josholson.dev (C# API).

## Stack

- Proxmox (Linux) guest running Podman; Caddy reverse proxy on a separate server
- PostgreSQL; migrations with Waypoint CLI
- C# / .NET API: structured logging, OpenTelemetry, Dapper
- Bun: TypeScript, SvelteKit, Playwright

## Layout

- `compose.yaml`: production-safe. `compose.dev.yaml`: dev only, always load explicitly
- `api/`, `web/`: 🚧 not created yet
- `docs/`: product requirements
- `.claude/rules/`: conventions, loaded when matching files are touched

## Commands

Start the dev database (copy `.env.example` to `.env` first):

```
podman compose -f compose.yaml -f compose.dev.yaml up -d
```

🚧 Lint and test commands will go here.

## Ground rules

- Formatting basics live in `.editorconfig`: UTF-8, LF, two spaces, no final newline.
- Never load `compose.dev.yaml` on the server.

## Docs

Read these on demand; they are not loaded automatically.

- [docs/product.md](../docs/product.md): site features and pages