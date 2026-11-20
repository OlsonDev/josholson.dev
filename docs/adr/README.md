# Architecture decision records

One decision per file, numbered, never rewritten. To change a decision, add a new ADR and mark the old one `Superseded by NNNN`.

Examples in ADRs follow the metasyntactic-names rule in [CLAUDE.md](../../.claude/CLAUDE.md). Name a real thing only where the ADR decides about it.

Template:

```
# ADR NNNN: Title

- Status: Accepted | Superseded by NNNN
- Date: YYYY-MM-DD

## Context
## Decision
## Alternatives considered
## Consequences
```

## Index

- [0001](0001-gitmoji-commit-messages.md): Gitmoji commit messages enforced by a hook
- [0002](0002-claude-does-not-commit.md): Claude does not commit
- [0003](0003-postgres-19-rc-on-debian-trixie.md): Postgres 19 RC on Debian trixie
- [0004](0004-explicit-dev-compose-file.md): Explicit dev compose file
- [0005](0005-waypoint-for-migrations.md): Waypoint for migrations
- [0006](0006-int-identity-always.md): `INT GENERATED ALWAYS` keys
- [0007](0007-year-optional-month-domain.md): `year_optional_month` domain
- [0008](0008-content-addressed-blobs.md): Content-addressed blobs
- [0009](0009-reference-data-in-repeatable-migrations.md): Reference data in repeatable migrations
- [0010](0010-global-tags-with-aliases.md): Global tags with aliases
- [0011](0011-resumes-own-their-sections.md): Resumes own their sections
- [0012](0012-enum-like-columns-as-smallint.md): Enum-like columns as `SMALLINT`
- [0013](0013-single-postgres-server-with-template-databases.md): One Postgres server, template databases
- [0014](0014-hierarchical-seed-layers-in-yaml.md): Hierarchical seed layers in YAML
- [0015](0015-seed-id-blocks-and-sequence-cursor.md): Seed ID blocks and sequence cursor
- [0016](0016-table-prefixed-keys-and-junction-names.md): Table-prefixed keys and `__` in compound names
- [0017](0017-domains-by-kind.md): Domains for repeated validation, one schema per kind
- [0018](0018-dml-only-api-role.md): DML-only API role
- [0019](0019-mise-for-tool-management.md): mise for tool management
- [0020](0020-waypoint-in-a-dev-tools-container.md): Waypoint in a dev tools container
- [0021](0021-generated-schema-docs.md): Generated schema docs
- [0022](0022-branch-names-enforced-by-a-hook.md): Branch names enforced by a hook
- [0023](0023-api-feature-folders-and-colocated-tests.md): API feature folders and colocated tests
- [0024](0024-scoped-open-connection-per-request.md): One open connection per request
- [0025](0025-camelcase-dapper-parameters.md): camelCase Dapper parameters
- [0026](0026-rest-json-with-controllers.md): REST and JSON with Controllers
- [0027](0027-passkey-auth-and-direct-browser-to-api.md): Passkey auth and direct browser-to-API calls
- [0028](0028-editorconfig-enforces-csharp-style.md): `.editorconfig` enforces C# style; top-level `Program.cs`
- [0029](0029-nodatime-for-time.md): NodaTime for time in the API
- [0030](0030-analyzers.md): BannedApiAnalyzers, Meziantou.Analyzer, and Roslynator
- [0031](0031-three-passkeys-and-phone-registration.md): Sign in with Red, Purple, or a phone; register the phone from a signed-in page
- [0032](0032-constructor-injection-in-tests.md): Constructor injection in tests (`Xunit.DependencyInjection`)
- [0033](0033-scoped-databases-per-test.md): A scoped database per test, baked seed templates, and a scope header
- [0034](0034-seed-lock-file-and-branch-safe-migration.md): Seed lock file with migration checksums, and all-or-nothing, branch-safe `seeds:migrate`
- [0035](0035-typed-seed-values-and-relative-times.md): Typed seed values from the column type, per-column relative times against an anchor, and fixtures from the baked template
- [0036](0036-shared-library.md): A shared library for utilities (`JoshOlson.Shared`)
- [0037](0037-dotnet-11-and-csharp-15.md): Target .NET 11 and C# 15, `[with(...)]` for comparers, and a scoped suppression for generated validation code
- [0038](0038-seed-tool-command-line.md): The seed tool's command line is `JoshOlson.Seed`, on Spectre.Console.Cli and a DI container
- [0039](0039-derive-the-lock-and-seeds-from-the-base.md): Derive the lock and the seeds from the base branch (`origin/main`), so a branch can iterate on an unmerged migration and a merge needs no manual resolution
- [0040](0040-seed-tool-tests-share-scenarios-and-databases.md): The seed tool's tests share scenarios and databases: several behaviors per test, starting points built once per run, templates for fixed test text kept on the server, and fixture layers in place of the real ones
- [0041](0041-parameter-records-result-records-and-union-results.md): Records for parameter groups and returned tuples, and `Result<T, TErrors>` unions for failures a caller handles
- [0042](0042-openapi-contract-and-enum-numbers.md): The OpenAPI document and the TypeScript types generated from it, with enums as numbers on the wire and names in the types
- [0043](0043-generated-api-client.md): A generated API client of standalone functions (tree-shakeable), with a configured `fetch`, in-flight requests shared for every method, `ApiError`, and instants as `Temporal.Instant`
- [0044](0044-first-passkey-from-the-browser-with-a-setup-secret.md): The first passkey is registered from the browser with a setup secret that works only while no passkey exists, replacing the CLI token, and a passkey's nickname goes onto the key
- [0045](0045-tailwind-css-and-shadcn-svelte.md): Tailwind CSS site-wide and shadcn-svelte components copied into the project, with `.ts` imports, for the admin and the public pages
- [0046](0046-tag-slug-history-versions-and-rendered-markdown.md): Tag slugs keep a history that redirects with `308`, a save is checked against a version (`409`), Markdown is rendered and sanitized when it is written, color is a Tailwind name, and a delete reports its blockers and warnings
