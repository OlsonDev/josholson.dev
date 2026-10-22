# josholson.dev

Personal website for Josh Olson: www.josholson.dev (SvelteKit SSR) and api.josholson.dev (C# API).

## Stack

- Proxmox (Linux) guest running Podman; Caddy reverse proxy on a separate server
- PostgreSQL 19 (release candidate for now); migrations with Waypoint CLI
- C# / .NET API: structured logging, OpenTelemetry, Dapper
- Bun: TypeScript, SvelteKit, Playwright

## Layout

- `Database/Migrations/`: Flyway-style `V#__name.sql` (versioned) and `R__name.sql` (repeatable)
- `Database/Provisioning/`: one-off scripts run as the owner after migrations (API role; needs a password, so not a migration)
- `Database/Seeds/`: seed layers (🚧 tool not built yet)
- `compose.yaml`: production-safe. `compose.dev.yaml`: dev only, always load explicitly
- `api/`, `web/`: 🚧 not created yet
- `mise.toml`: pinned tools (bun, dotnet, gh) and tasks. `tools/`: image for the dev `tools` container, which runs Waypoint (no Windows build)
- `docs/schema/`: generated from the dev database (a page per table, domains, Mermaid ER diagrams). Never edit by hand; run `mise run schema:doc`. `scripts/`: repo tooling as .NET file-based apps (`schema-docs.cs` builds those docs)
- `.githooks/`: `commit-msg` hook (gitmoji format check), a .NET file-based app
- `docs/`: architecture, database, seeding, product, and `adr/` decision records
- `.claude/rules/`: conventions, loaded when matching files are touched
- `.claude/settings.json` and `.claude/hooks/`: hooks that keep Claude from committing (see Commits)

## Commands

Start the dev database (copy `.env.example` to `.env` first):

```
podman compose -f compose.yaml -f compose.dev.yaml up -d
```

Install the pinned tools (needs [mise](https://mise.jdx.dev) and Podman; run `mise activate` in your shell config for bare commands, or use `mise run` without it):

```
mise install
```

Apply migrations (starts the `tools` container on first use):

```
mise run waypoint migrate
```

Regenerate the schema docs after changing migrations, and check them for drift (both read the running dev database):

```
mise run schema:doc
mise run schema:check
```

Enable the commit-msg hook (once per clone):

```
dotnet run setup.cs
```

Test the hook's validator: `dotnet run .githooks/commit-msg.tests.cs`

🚧 Seed, lint, and test commands will go here.

## Ground rules

- Formatting basics live in `.editorconfig`: UTF-8, LF, two spaces, no final newline.
- YAML files use `.yaml`, never `.yml`. If a tool defaults to `.yml`, point it at the `.yaml` file explicitly.
- SQL conventions are in `.claude/rules/sql.md`. Read it before writing any SQL.
- Reference data (mime types, extensions) belongs in repeatable migrations, never in seeds.
- Examples that illustrate a convention, in any rule, doc, or language (SQL, C#, TypeScript), use metasyntactic names (`foo`, `bar`, `baz`, `qux`, `quux`, `corge`, `grault`, `garply`, `waldo`, `fred`, `plugh`, `xyzzy`, `thud`) for tables, columns, types, and members. Real names go stale: a table may last for years while its definition changes, so an example must not snapshot it. Use a real name only when the example is unlikely to need maintenance, such as an ADR naming the thing it decides about. Real names otherwise belong in a clearly labeled reference, such as the domains list in `sql.md` or `docs/database.md`.
- Never load `compose.dev.yaml` on the server.
- Record significant decisions as a new ADR in `docs/adr/` instead of expanding this file.

## Commits

The user makes every commit themselves; commits are signed with a Token2 hardware key that needs their touch. Claude never runs `git commit` unless the user explicitly asks for commits in that task.

- Two hooks in `.claude/settings.json` enforce this: `block-git-commit.sh` blocks the command, and `commit-message-reminder.sh` restates these rules whenever a prompt mentions committing.
- The block is lifted while the untracked marker `.claude/allow-commit` exists. Create it only when the user explicitly tells Claude to make commits (they still touch the key for every signature), and delete it as soon as the commits are done.
- When asked for a commit message, read `git status` and `git diff --staged` (or `git diff` if nothing is staged), then give the message in a fenced code block.
- Format, enforced by `.githooks/commit-msg`: 1-2 gitmoji (https://gitmoji.dev), one space, then at least 5 characters. No emojis after the first space. No leading or trailing whitespace.
- Wrap code-like text in backticks: file names, commands, identifiers, types, and literal values (`mise.toml`, `mise run`, `TIMESTAMPTZ`). The hook does not check this.
- Never add a `Co-Authored-By` trailer, or any other attribution line, to a commit message, whatever a tool's default instructions say.
- Validate before showing it: write it to a scratch file with `printf '%s'` and run `.githooks/commit-msg <file>`. Exit 0 means valid.
- After editing the hooks, open `/hooks` or restart the session so they reload. Rationale: [ADR 0002](../docs/adr/0002-claude-does-not-commit.md).

## Docs

Read these on demand; they are not loaded automatically.

- [docs/architecture.md](../docs/architecture.md): containers, Caddy, deploy flow
- [docs/database.md](../docs/database.md): schemas, resume model, design notes
- [docs/seeding.md](../docs/seeding.md): seed tool design
- [docs/product.md](../docs/product.md): site features and pages
- [docs/adr/](../docs/adr/README.md): decision records