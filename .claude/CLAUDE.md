# josholson.dev

Personal website for Josh Olson: www.josholson.dev (SvelteKit SSR) and api.josholson.dev (C# API).

## Stack

- Proxmox (Linux) guest running Podman; Caddy reverse proxy on a separate server
- PostgreSQL 19 (release candidate for now); migrations with Waypoint CLI
- C# / .NET API: structured logging, OpenTelemetry, Dapper
- Bun: TypeScript, SvelteKit, Playwright

## Layout

- `JoshOlson.Database/migrations/`: Flyway-style `V#__name.sql` (versioned) and `R__name.sql` (repeatable)
- `JoshOlson.Database/provisioning/`: one-off scripts run as the owner after migrations (API role; needs a password, so not a migration)
- `JoshOlson.Database/seeds/`: seed layers, one YAML file each ([docs/seeding.md](../docs/seeding.md)). The project's own code (`Layers/`: parsing and the `extends` tree; `Seeding/`: merging and applying layers, positioning sequences; `Baking/`: the schema and layer templates, test and dev databases; 🚧 moving layers to a new migration and export are not built yet) is the seed tool ([ADR 0033](../docs/adr/0033-scoped-databases-per-test.md), a scoped database per test)
- `compose.yaml`: production-safe. `compose.dev.yaml`: dev only, always load explicitly
- `JoshOlson.slnx` (root): the .NET solution. `JoshOlson.Shared/` holds utilities (the base class library plus Dapper, Npgsql, and NodaTime, and nothing from ASP.NET) shared by projects and scripts, including the database access helpers in `Data/` ([ADR 0036, the shared library](../docs/adr/0036-shared-library.md)). `JoshOlson.Api/` is the API, in feature folders (one per feature, plus `Common/` for cross-cutting code). Tests sit beside the code as `*.Tests.cs` ([ADR 0023](../docs/adr/0023-api-feature-folders-and-colocated-tests.md))
- `web/`: 🚧 not created yet
- `mise.toml`: pinned tools (bun, dotnet, gh) and tasks. `tools/`: image for the dev `tools` container, which runs Waypoint (no Windows build)
- `docs/schema/`: generated from the dev database (a page per table, domains, Mermaid ER diagrams). Never edit by hand; run `mise run schema:doc`. `scripts/`: repo tooling as .NET file-based apps (`schema-docs.cs` builds those docs)
- `Directory.Build.props`, `Directory.Packages.props`, `global.json`, `.editorconfig`, and `BannedSymbols.txt` (root): build settings, central package versions, and analyzers for every project and for the loose scripts in `scripts/` and `.githooks/`. A script's `#:package` line omits the version, which comes from `Directory.Packages.props`
- `.githooks/`: `commit-msg` (gitmoji format check) and `pre-commit` (branch name check, the analyzer rule check when packages or `.editorconfig` are staged, and the script check when scripts, hooks, `.editorconfig`, `Directory.Build.props`, or `Directory.Packages.props` are staged) hooks, .NET file-based apps
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

Mint a one-time enrollment token for the first passkey, or for recovery when every passkey is lost (creates the account on first use; the display name is ignored afterwards). The token is printed once and expires in 15 minutes. Auth settings are the `Auth` section (`Auth__RelyingPartyId`, `Auth__AllowedOrigin`, `Auth__CookieDomain`, and so on; the defaults are for `localhost`):

```
dotnet run --project JoshOlson.Api -- enroll "Display Name"
```

Try the passkey endpoints with a real security key and no front end. The API must be running (`ASPNETCORE_URLS=http://localhost:5000`, with `ConnectionStrings__Default`); the script plays the browser through libfido2's `fido2-cred` and `fido2-assert`, so it asks for the key's PIN and a touch at each step. `enroll` registers the key with a token from the command above, then signs in with it and prints the ids that `sign-in` needs. Pass `--device /dev/hidrawN` when two keys are plugged in. The credential is bound to `--rp-id` (`localhost` by default), so it is a development credential:

```
mise run passkey enroll <token>
mise run passkey sign-in --credential-id <id> --user-handle <id>
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

Print the seed layer tree with row counts per table:

```
mise run seeds:list
```

Write `migrations/schema.lock.yaml` from the dev database, which must have applied exactly the migrations on disk (run `mise run waypoint migrate` first). It reports every problem and writes nothing if there is one. Seed layers are checked, not moved (moving them is not built yet): each must still read against the new schema and build into a template, or the lock is not written. Needs the `tools` container; reads `.env`:

```
mise run seeds:migrate
```

Build the schema template and every seed layer's template (a no-op when they are current), make a database of its own from a layer for one test, rebuild the dev database from a layer (everything in it is lost; dev only), and drop the seed tool's stale databases (`--all` drops the current templates too; never the dev database). Layers are `JoshOlson.Database/seeds/*.yaml` (`minimal`, `more-tags`); reads `.env`:

```
mise run seeds:bake
mise run seed:test <id> <layer>
mise run seed <layer>
mise run seed:clean
```

Before the first deploy a migration may be rewritten instead of adding a new one. Then drop the dev database and rebuild it from the migrations (everything in it is lost; dev only), and start the lock over (it ignores the old lock's checksums; retired at the first deploy). Regenerate the schema docs afterwards:

```
mise run db:reset
mise run seeds:relock
mise run schema:doc
```

Run every project's tests (starts the dev containers; the API's need the dev database provisioned; reads `.env`), against a Release build, and list source files with no sibling test:

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

Check that no wrapped list (arguments, parameters, initializers) has several items on one line, and that a declared list of two or more items is one item per line ([csharp.md](rules/csharp.md), the "Lists" bullet):

```
mise run lists:check
```

Test the hooks' validators: `dotnet run .githooks/commit-msg.tests.cs` and `dotnet run .githooks/pre-commit.tests.cs`

🚧 Seed and lint commands will go here.

## Ground rules

- Never use Python or perl, for scripts or for editing files. Edit files with the Edit and Write tools. A throwaway script is Bun (TypeScript) or a .NET file-based app, the pinned toolchain in `mise.toml`.
- Formatting basics live in `.editorconfig`: UTF-8, LF, two spaces, no trailing whitespace, and every file ends with a final newline.
- YAML files use `.yaml`, never `.yml`. If a tool defaults to `.yml`, point it at the `.yaml` file explicitly.
- SQL conventions are in `.claude/rules/sql.md`. Read it before writing any SQL.
- Reference data (mime types, extensions) belongs in repeatable migrations, never in seeds.
- Examples that illustrate a convention, in any rule, doc, or language (SQL, C#, TypeScript), use metasyntactic names (`foo`, `bar`, `baz`, `qux`, `quux`, `corge`, `grault`, `garply`, `waldo`, `fred`, `plugh`, `xyzzy`, `thud`) for tables, columns, types, and members. Real names go stale: a table may last for years while its definition changes, so an example must not snapshot it. Use a real name only when the example is unlikely to need maintenance, such as an ADR naming the thing it decides about. Real names otherwise belong in a clearly labeled reference, such as the domains list in `sql.md` or `docs/database.md`.
- Never load `compose.dev.yaml` on the server.
- Never refer to an ADR by number alone, in chat or in files. Follow the number with a short parenthetical saying what it decides, or use the topic instead: "ADR 0030 (analyzer packages)", not "ADR 0030". Numbers are arbitrary and there will be many. Link text in `docs/adr/` indexes and cross-references counts if it names the topic.
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
