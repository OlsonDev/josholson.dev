# josholson.dev

Personal website for Josh Olson: www.josholson.dev (SvelteKit SSR) and api.josholson.dev (C# API).

## Stack

- Proxmox (Linux) guest running Podman; Caddy reverse proxy on a separate server
- PostgreSQL 19 (release candidate for now); migrations with Waypoint CLI
- C# / .NET API: structured logging, OpenTelemetry, Dapper
- Bun: TypeScript, SvelteKit, Playwright

## Layout

- `database/migrations/`: Flyway-style `V#__name.sql` (versioned) and `R__name.sql` (repeatable)
- `database/provisioning/`: one-off scripts run as the owner after migrations (API role; needs a password, so not a migration)
- `database/seeds/`: seed layers (🚧 tool not built yet)
- `compose.yaml`: production-safe. `compose.dev.yaml`: dev only, always load explicitly
- `api/`: the .NET solution (`JoshOlson.slnx`) with one project, `JoshOlson.Api`, in feature folders (one per feature, plus `Common/` for cross-cutting code). Tests sit beside the code as `*.Tests.cs` ([ADR 0023](../docs/adr/0023-api-feature-folders-and-colocated-tests.md))
- `web/`: 🚧 not created yet
- `mise.toml`: pinned tools (bun, dotnet, gh) and tasks. `tools/`: image for the dev `tools` container, which runs Waypoint (no Windows build)
- `docs/schema/`: generated from the dev database (a page per table, domains, Mermaid ER diagrams). Never edit by hand; run `mise run schema:doc`. `scripts/`: repo tooling as .NET file-based apps (`schema-docs.cs` builds those docs)
- `Directory.Build.props` (root): build settings and analyzers for the loose scripts in `scripts/` and `.githooks/`; `api/` has its own
- `.githooks/`: `commit-msg` (gitmoji format check) and `pre-commit` (branch name check, the analyzer rule check when packages or `.editorconfig` are staged, and the script check when scripts, hooks, `.editorconfig`, or `Directory.Build.props` are staged) hooks, .NET file-based apps
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

Create or update the DML-only API role in the dev database (needs `POSTGRES_API_PASSWORD` in `.env`). Rerun it after a migration adds a schema. The API tests connect as this role:

```
mise run db:provision
```

Regenerate the schema docs after changing migrations, and check them for drift (both read the running dev database):

```
mise run schema:doc
mise run schema:check
```

Enable the git hooks (once per clone):

```
dotnet run setup.cs
```

Run the API tests (needs the dev database running and provisioned; reads `.env`), against a Release build, and list source files with no sibling test:

```
mise run test
mise run test:release
mise run test:missing
```

Publish the API with `-p:IncludeTests=false` so no test code ships.

Check that every rule in the analyzer packages has a severity in `.editorconfig` (run after any package upgrade; `mise run analyzers:add` appends the missing ones, ADR 0030):

```
mise run analyzers:check
```

Build every loose script with the style rules and analyzers on (`dotnet format` cannot open them; the pre-commit hook runs this when scripts are staged):

```
mise run scripts:check
```

Check that no wrapped list (arguments, parameters, initializers) has several items on one line ([csharp.md](rules/csharp.md), the "Lists" bullet):

```
mise run lists:check
```

Test the hooks' validators: `dotnet run .githooks/commit-msg.tests.cs` and `dotnet run .githooks/pre-commit.tests.cs`

🚧 Seed and lint commands will go here.

## Ground rules

- Formatting basics live in `.editorconfig`: UTF-8, LF, two spaces, no trailing whitespace, and every file ends with a final newline.
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
- [docs/security.md](../docs/security.md): secrets, transport, authorization, injection
- [docs/adr/](../docs/adr/README.md): decision records
- [docs/open-decisions/](../docs/open-decisions/README.md): decisions still to make before and while building `api/` and `web/`. These are only tracked locally, never committed.
