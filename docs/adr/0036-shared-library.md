# ADR 0036: A shared library for utilities

- Status: Accepted
- Date: 2026-11-03

## Context

[ADR 0023 (feature folders, colocated tests)](0023-api-feature-folders-and-colocated-tests.md) assumed the API was the only project. With `JoshOlson.Database` (the seed tool) there are two, and the second needed the same string helpers and a kebab-case check that the API already had in several forms. The loose scripts also repeated small helpers, such as the source-file walk that skips `bin` and `obj`.

## Decision

- **`JoshOlson.Shared`** holds utilities used by more than one project or script. It references the base class library only (NodaTime at most): nothing from ASP.NET, Npgsql, or Dapper, so any project or script can reference it.
- It follows the same layout as the other projects: folders by topic (`Extensions/`, `IO/`), tests beside the code as `*.Tests.cs`, and `IncludeTests` for Debug.
- Move code in when a second user appears, not before. `StringExtensions` and `SourceFiles` are the first residents; the API's database helpers stay in the API until the seed tool needs them.
- Scripts reference it with `#:project ../JoshOlson.Shared` where it removes a real duplicate. The git hooks do not: they run on every commit and a project reference adds build time to each.

## Alternatives considered

- **`JoshOlson.Core`:** suggests domain logic, which this is not.
- **`JoshOlson.Common`:** collides with the API's own `Common/` namespace.
- **Copying helpers into each project:** `LayerName` carried its own kebab-case rule beside the API's attribute, and two scripts carried the same file filter.

## Consequences

- Another project to build and test; `mise run test` runs it with the rest.
- The database's `text.kebab_case` domain, the API's `KebabCaseAttribute`, and `IsKebabCase()` are still three expressions of one rule. The attribute keeps its regex because the validation framework needs a pattern.
