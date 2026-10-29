# ADR 0023: API feature folders and colocated tests

- Status: Accepted
- Date: 2026-10-28

## Context

The API needs a layout that keeps a feature's controller, service, repository, and tests together, and tests that make missing coverage visible. A separate test project mirrors the namespace tree, drifts out of sync, and hides gaps.

## Decision

- **One project**, `api/JoshOlson.Api`, in `api/JoshOlson.slnx`. One folder and namespace per feature (`JoshOlson.Api.Tags`) holds `TagController`, `TagService`, `TagRepository`, and the request and response types together in `Tag.Payloads.cs` (a `{Feature}.Payloads.cs` file may hold several types). Cross-cutting code lives in `Common/` (`Db`, `Errors`, `Testing`). There is no `Features` wrapper namespace and no `Services` or `Repositories` namespaces.
- **Registration by convention.** `ConventionRegistration` scans the assembly and registers every class named `*Service` or `*Repository` as scoped, plus its `I{Name}` interface if it has one. Such a class must be public, sealed, non-abstract, and non-generic, or startup fails. There are no per-feature registration classes. Controllers are discovered by MVC. Other kinds of class (background jobs, clients) get their own suffix and convention when the first one appears.
- **Repositories return the response type directly.** A separate row type is added only when a query needs optimizing.
- **Tests are `*.Tests.cs` beside the file they test**, in the same project and namespace. `IncludeTests` defaults to true for Debug and false otherwise; pass `-p:IncludeTests=true|false` (or set the environment variable) to override. When it is false the test files and the test packages are not compiled. Publish with `-p:IncludeTests=false`.
- **Entry point.** xUnit v3 test projects are executables, which collides with the web app's. `Program.Main` is explicit (since 2026-10-29 top-level statements, see [ADR 0028](0028-editorconfig-enforces-csharp-style.md)) and, when `IncludeTests` is true, hands off to the xUnit runner if the arguments look like a test runner's (`--server`, `--list-tests`, `--dotnet-test-pipe`, `--results-directory`) or `JOSHOLSON_RUN_TESTS=1` is set. Otherwise it starts the web app. `global.json` opts `dotnet test` into the Microsoft.Testing.Platform runner, which .NET 10 requires.
- **Tests use the real dev database** (`Common/Testing/TestDatabase.Tests.cs` reads `POSTGRES_*`) and clean up what they insert. Controller tests use `WebApplicationFactory<Program>`.
- **Errors.** A global `IExceptionHandler` maps Postgres unique and foreign key violations to 409 and check violations (including domain checks) to 400, as ProblemDetails without the database message. Services and controllers have no try/catch.
- `scripts/missing-tests.cs` (`mise run test:missing`) lists source files with no sibling test file. Payload files (`*.Payloads.cs`), registrations, and `Program.cs` are exempt.

## Consequences

- A test runner that passes an argument not in the list above would start the web app instead. Set `JOSHOLSON_RUN_TESTS=1` for it, or add the argument.
- The Debug binary contains the test framework and a runner. It is never deployed.
- Tests need the dev database running, so they are integration tests; there are no mocks.
- `IncludeTests` must stay false for the production build.
- Not yet decided here: the test framework is provisional (xUnit v3), as are the analyzer set and the API contract.
