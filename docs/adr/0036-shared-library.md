# ADR 0036: A shared library for utilities

- Status: Accepted
- Date: 2026-11-03
- Amended 2026-11-05: the seed tool needs the API's database helpers, so they moved here and the library now references Dapper, Npgsql, and NodaTime as well (below).

## Context

[ADR 0023 (feature folders, colocated tests)](0023-api-feature-folders-and-colocated-tests.md) assumed the API was the only project. With `JoshOlson.Database` (the seed tool) there are two, and the second needed the same string helpers and a kebab-case check that the API already had in several forms. The loose scripts also repeated small helpers, such as the source-file walk that skips `bin` and `obj`.

## Decision

- **`JoshOlson.Shared`** holds utilities used by more than one project or script. It references the base class library and the packages every project uses for data (Dapper, Npgsql, NodaTime): nothing from ASP.NET, so any project or script can reference it.
- It follows the same layout as the other projects: folders by topic (`Extensions/`, `IO/`, `Data/`), tests beside the code as `*.Tests.cs`, and `IncludeTests` for Debug.
- Move code in when a second user appears, not before. `StringExtensions` and `SourceFiles` were the first residents. The database helpers moved in when the seed tool became their second user: `Data/NpgsqlConnectionExtensions` (Dapper calls that carry the cancellation token) and `Data/DapperConfiguration` (Dapper's process-wide settings and type handlers, applied by a module initializer, so every project and test gets the same ones). The handlers cover `Instant`, `IPAddress`, and `List<string>` for `text[]`.
- Its tests that need the database use the dev database as the DML-only API role, through their own `TestDatabase` helper, as the API's tests do.
- Scripts reference it with `#:project ../JoshOlson.Shared` where it removes a real duplicate. The git hooks do not: they run on every commit and a project reference adds build time to each.

## Alternatives considered

- **`JoshOlson.Core`:** suggests domain logic, which this is not.
- **`JoshOlson.Common`:** collides with the API's own `Common/` namespace.
- **Copying helpers into each project:** `LayerName` carried its own kebab-case rule beside the API's attribute, and two scripts carried the same file filter.

## Consequences

- Another project to build and test; `mise run test` runs it with the rest, and its data tests need the dev database like the API's.
- Anything that references the library, scripts included, restores Dapper, Npgsql, and NodaTime. The git hooks still do not reference it.
- `TestDatabase` now exists in the API's tests and the library's tests, and the seed tool's catalog tests build their own owner connection. Linking one source file into each test assembly would remove the copies; it is not done yet.
- The database's `text.kebab_case` domain, the API's `KebabCaseAttribute`, and `IsKebabCase()` are still three expressions of one rule. The attribute keeps its regex because the validation framework needs a pattern.
