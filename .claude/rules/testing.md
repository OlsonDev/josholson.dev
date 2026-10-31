---
paths:
  - "**/*.Tests.cs"
  - "**/*.test.ts"
  - "**/*.spec.ts"
---

# Testing

## C#

- xUnit v3 on Microsoft.Testing.Platform. Tests sit beside the code as `Foo.Tests.cs` in the same project and namespace ([ADR 0023](../../docs/adr/0023-api-feature-folders-and-colocated-tests.md)). `mise run test:missing` lists source files with no sibling test.
- The class is `FooTests`, and a test is named `Member_Behavior` (`Create_DuplicateSlug_ReturnsUnprocessable`).
- Anything that touches data is an integration test against the dev database, with no mocks. Pure helpers (string extensions, attributes, the SQL memoizer) get plain unit tests with no database. Use data no other test shares (`test-{Guid.NewGuid():N}` slugs) and delete what you insert in a `finally`, passing `CancellationToken.None` so cleanup runs even when the test was cancelled.
- Tests connect as the DML-only `josholson_api` role, like the API, so a missing grant fails here. `mise run db:provision` creates it; rerun it after a migration adds a schema. Tests never need the owner, and `ApiRoleTests` checks the role cannot change structure.
- Take the token from `TestContext.Current.CancellationToken`.
- Controller tests build a `WebApplicationFactory<Program>` with the connection string from `TestDatabase`. Repository tests open a connection from `TestDatabase.DataSource`, which reads and writes NodaTime types like the API's.
- Helper types go inside the test class. The convention scan skips nested types, so a fake named `FooService` there is not registered by `ConventionRegistration`.
- A test that needs the database fails with a message if `POSTGRES_API_PASSWORD` is not in the environment or `.env`. Start and provision the dev database first (see CLAUDE.md).

## 🚧 TypeScript and Playwright

Not written yet.
