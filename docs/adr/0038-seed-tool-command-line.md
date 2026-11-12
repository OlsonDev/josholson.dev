# ADR 0038: The seed tool's command line is `JoshOlson.Seed`, on Spectre.Console.Cli and a DI container

- Status: Accepted
- Date: 2026-11-11

## Context

The seed tool ([ADR 0033 (a scoped database per test)](0033-scoped-databases-per-test.md)) started as `scripts/seed.cs`, a file-based app that parsed `args` by hand and opened its own connections and paths in static helpers. By the time it had `list`, `migrate`, `relock`, `bake`, `seed`, `test`, and `clean`, each command repeated its error handling, its usage text, and its setup, and none could be tested without running the whole script against the real repository. The `JoshOlson.Database` project had the same problem one level down: the classes that touch the server built their own collaborators (`new ServerDatabases()`, `new ComposeTools(...)` inside `SeedTool.OpenAsync`).

## Decision

- **A project, `JoshOlson.Seed`, not a script.** Commands are classes with tests beside them (`*.Tests.cs`), so `dotnet format`, the analyzers, `mise run test:missing`, and `mise run lists:check` cover them like the other projects. `JoshOlson.Database` stays a library with no dependency on the command line; `JoshOlson.Seed` references it. The test entry point works as in the API: `Program.cs` hands a `dotnet test` run to the runner when the tests are compiled in.
- **Spectre.Console.Cli for parsing and output.** A command is an `AsyncCommand` with a nested `Settings` class; arguments, options, help text, validation, and usage errors (exit 2) come from the library. Output uses its widgets: a `Tree` for `list`, a `Table` and a status line for `bake`, markup for the rest.
- **`Microsoft.Extensions.DependencyInjection`, joined by our own registrar and resolver** (`Composition/`, about twenty lines): Spectre ships no container and its documented integration is exactly this. Everything is a singleton, because a run is one command in one process, so the process is the scope. `SeedApp.AddServices` registers the services; `SeedApp.Configure` registers the commands; `Program` and the tests both use them, so a test runs the wiring the tool runs.
- **`SeedToolFactory` and `OwnerConnections`.** `SeedTool` needs the layer tree and the schema, which are read from disk (or, for `migrate`, built in memory), so it cannot be a registration; the factory opens it from registered parts (the paths, `ServerDatabases`, `ComposeTools`, the clock, the dev database's name). `OwnerConnections` opens the maintenance and dev connections once, on first use, and disposes them with the container.
- **One exception handler** (`SeedApp.HandleException`) in place of a `try`/`catch` in every command: a `SeedFormatException` or `SchemaCatalogException` prints its message and exits 1, a mistake in the command line exits 2, and anything else prints a stack trace and exits 1. It takes the console as an argument because a parse error is thrown before the container exists.
- **`migrate` and `migrate --relock` are one command**, not two, since they differ by one setting. The mise tasks `seeds:migrate` and `seeds:relock` keep their names.
- **Tests run the real command line** against a `TestConsole` (`SeedAppRunner`), and swap registrations where a command could do damage: `seed` drops and makes again the dev database, so its tests give the factory a database name of their own; `clean` takes a `--prefix`, so its test drops only its own databases.
- **Building a template takes a lock on its name**, on a connection of its own to the maintenance database (`ServerDatabases.EnsureTemplateAsync`). The command-line tests and the `JoshOlson.Database` tests run side by side in `mise run test` and both build the real templates. A Postgres advisory lock belongs to one database, so a lock taken on whichever connection the caller had never met the other process's: the commands connect to `postgres`, the tests to the dev database.

## Alternatives considered

- **Keep a file-based app and split it with `#:include`** (SDK 10.0.300 and later). Included files can add types but not top-level statements, tests cannot sit beside them, and `dotnet format` and the test-coverage list would not see them as a project.
- **Put the command line inside `JoshOlson.Database`.** Possible (the API does the same with its `Program.cs` and tests), but it makes a library an application and pulls Spectre and the container into everything that references it.
- **`System.CommandLine`.** Its status when this was decided was unclear (one source called it generally available, Microsoft Learn still said preview), and it brings no output widgets.
- **A third-party registrar package** (D20Tek's). The same twenty lines plus a dependency.
- **Spectre's CLI testing package.** `Spectre.Console.Cli.Testing` exists only as `1.0.0-alpha` builds, so the tests drive `CommandApp` directly instead.
- **A scoped connection per command, as the API does per request.** A run has one command, so a singleton that lives for the process gives the same result with less machinery.

## Consequences

- Another project in the solution, with its own `Program.cs` test hand-off to keep in step with the API's.
- Every seed command is a class that takes what it needs in its constructor, so adding one means a class, a test, and one line in `SeedApp.Configure`.
- `SeedTool.OpenAsync` has overloads that take the shared `ServerDatabases` and `ComposeTools`; the older ones build their own and remain for the library's tests.
- `seeds:check` and `seeds:export` are commands in this project like the rest.
