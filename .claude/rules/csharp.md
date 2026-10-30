---
paths:
  - "**/*.cs"
  - "**/*.csproj"
---

# C#

Formatting, language style, and naming live in `.editorconfig` (`[*.cs]`) and are enforced by `dotnet format --verify-no-changes --severity warn` from `api/` ([ADR 0028](../../docs/adr/0028-editorconfig-enforces-csharp-style.md)). This file covers choices it cannot express and records the intent behind some of them. Examples use metasyntactic names (see CLAUDE.md).

## Language

- Use modern language features by default.
- No primary constructors on classes: a parameter can't be `readonly` or named `_field`. Use a constructor and `private readonly` fields.
- `x is null` and `x is not null`, never `== null`, even for `Nullable<T>`.
- Compare enums with `is`: `x is Foo.Bar or Foo.Baz`, even if there's a single comparison.
- File-scoped namespaces. `Program.cs` uses top-level statements.
- `async` methods end in `Async`, except public methods in controllers (`*Controller.cs`) and tests: actions are named for the route, and MVC strips the suffix from action names anyway. Local constants are PascalCase.
- Expression-bodied members (`=>`) wherever the body is a single expression or statement: constructors, methods, properties. Put the `=>` on its own line below the signature, indented one level (`public Foo(Bar bar)` / `  => _bar = bar;`).
- Prefer `switch` expressions and property patterns (`list is { Count: > 0 }`) where they read better.
- `var` for locals. Target-typed `new()` for fields, properties, and arguments.
- `required` and `init` on properties. We can relax them later, and tightening them later breaks callers. `required` only forces the caller to write the assignment; it says nothing about null, so `required string? Foo` is valid.
- Raw string literals (`"""`) for multi-line text, especially embedded SQL.
- `foreach`, not `List<T>.ForEach`. LINQ is for queries and transformations, never side effects (such as in `.Select()` or `.Where()`).
- Lambda parameters are named for what they are, not `x`. A short abbreviation is fine in a short lambda where the type is obvious, e.g. `fooBars.Select(fb => fb.BarId)`.
- One type per file, named for the type. A file holding several types is named `{Name}.{Plural}.cs` (`Tag.Payloads.cs`, `Domain.Attributes.cs`): the request and response records of a feature and their shared `{Feature}Base`, or a family of attributes. `.editorconfig` turns MA0048 (file name must match type name) off for any `*.*s.cs`, so a plural second-to-last word is the whole convention. A family of arity variants of one generic type (`SqlMemoizer<T1>` to `<T1, T2, T3, T4>`) shares one file named for the type.
- Classes named `*Service` or `*Repository` are registered as scoped automatically by `ConventionRegistration`, so there are no per-feature registration classes. They must be `public sealed` (startup fails otherwise), and the suffixes are reserved for them: a fake, an abstract base, or a background job gets a different name. Other kinds of class get their own suffix and convention when the first one appears.
- An injected `*Service` or `*Repository` is named for its type, not its role: field `_tagRepository`, constructor parameter and local `tagRepository`. Other dependencies keep short names (`_connection`).
- Private instance fields are `_camelCase`. Private static and const fields are `_PascalCase`: the underscore says private, the capital says shared by every instance. Public and internal members are `PascalCase`; locals and parameters are `camelCase`. No `this.` unless required. `System` usings first, then the rest, each group alphabetical.
- Trailing commas, never comma-first. Multi-line parameter lists, argument lists, initializers, anonymous objects, `switch` arms, and enum members put one item per line, each ending in a comma. The Roslyn formatter rewrites comma-first layouts in object and anonymous initializers, `switch` expressions, and enums, so it can't be applied consistently (SQL stays comma-first).
- Keep lines to about 120 columns and wrap longer ones. This is a guideline, not enforced by a tool.
- Analyzers (BannedApiAnalyzers, Meziantou, Roslynator) fail the build ([ADR 0030](../../docs/adr/0030-analyzers.md)). To allow one use, `#pragma warning disable <ID>` with a reason; to change a rule everywhere, edit `.editorconfig`.
- `api/JoshOlson.Api/BannedSymbols.txt` lists banned APIs (RS0030), each with the replacement as its message. Add to it rather than writing a rule here. Every overload needs its own line with a full signature; a type ban misses bare declarations. Time is NodaTime ([ADR 0029](../../docs/adr/0029-nodatime-for-time.md)).
- Several lookups in a loop need a `Dictionary` or `ILookup`, not a scan per iteration. Compute an aggregate once into a named variable.

## Data access

- Dapper with Npgsql, SQL written inline. No `Dapper.Contrib`.
- Repositories take the scoped `NpgsqlConnection` (opened once per request in `AddDatabase`) and never open or dispose it. A service that needs atomicity begins a transaction on that connection and calls `CommitAsync`; disposing it uncommitted rolls back. Repositories on the same connection join it automatically.
- Repository classes only run queries. Logic that isn't a query belongs in a service.
- Pass the `CancellationToken` through a `CommandDefinition`.
- Call `DefaultTypeMap.MatchNamesWithUnderscores = true` once at startup so `bar_id` fills `BarId`.
- Insert with `RETURNING` and read the key with `QuerySingleAsync<int>`.
- Aggregate in SQL (`count`, `sum`), not in C#.

```csharp
public async Task<IReadOnlyList<Bar>> GetCorgesAsync(int barId, CancellationToken cancellationToken) {
  var sql = $"""
    SELECT
        c.corge_id
      , c.name
    FROM (SELECT)   AS _
    JOIN foo.bar    AS b  ON b.foo_id          = @barId
    JOIN foo.corge  AS c  ON c.bar_id          = b.bar_id
                         AND c.current_status  = {(int)CorgeStatus.Active}
    ORDER BY c.sort_order, c.corge_id;
    """;
  var parameters = new { barId };
  var cmd = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
  var corges = await _connection.QueryAsync<Bar>(cmd);
  return corges.AsList();
}
```

## Controllers

- A controller is named for its entity, singular, like its service and repository (`TagController`, `TagService`, `TagRepository`). The route is a literal plural for the collection (`[Route("tags")]`), not `[controller]`.
- `[ApiController]`, attribute routing, async actions taking a `CancellationToken`, and no logic: call a service and return an `ActionResult`. Not minimal APIs ([ADR 0026](../../docs/adr/0026-rest-json-with-controllers.md)).
- The constructor takes what most actions use. A dependency only one action needs is a `[FromServices]` parameter on that action.
- Validation: the service has `Validate{Operation}Async` taking the operation's arguments and returning `ValidationErrors`; the action calls it first and returns `UnprocessableEntity(errors.ToProblemDetails())` (422) when `errors.Any`. Common rules are attributes on the payload (DataAnnotations, plus `[Trimmed]`, `[KebabCase]`, `[HexColor]` for our database domains), run by `PayloadValidator`; write code only for what attributes cannot express, such as uniqueness. A payload type needs `[ValidatableType]` or the validator throws. `Get` and `Delete` have nothing to validate: a missing row is a `404`.
- Create returns `201` with a `Location` header and no body. Update and delete return `204`, or `404` when the row does not exist. Errors are `ProblemDetails`, with no try/catch for database constraint violations: `PostgresExceptionHandler` maps them.

## SQL in C#

SQL in a string follows [sql.md](sql.md) for formatting, naming, and style, the same as a `.sql` file.

- Never put a value into the SQL text, whether by interpolation or concatenation. Always use a parameter and pass the value separately.
  - It is how injection happens.
  - It also defeats Npgsql's prepared-statement reuse, because every distinct value is a new statement.
- Pass parameters as an anonymous object, one property per line, with no redundant aliasing: `new { request.Name }` binds `@name`. Dapper takes the property name and Npgsql matches it case-insensitively, so PascalCase properties bind to camelCase placeholders.
- Name parameters in camelCase after the C# variable (`@barId`). Dapper binds by name, so a snake_case parameter would need a snake_case property; mapping is too verbose and camelCase stands out.
- You may interpolate enum values, especially hardcoded ones like the example above. This allows for a single prepared-statement plan.
- You may interpolate SQL that the code chose: a `ORDER BY` column(s) picked from a fixed list, or optional `JOIN..ON/WHERE` predicate(s). Build the predicate in or out in C# rather than writing `@x IS NULL OR col = @x`, which can cost a prepared statement its good plan.
- SQL that varies only by which optional parts are included is built by a static method taking those choices, and memoized so it is built once per combination: `private static readonly SqlMemoizer<bool, bool> _FooSql = new(BuildFooSql);` then `_FooSql.ForValues(a is not null, b is not null)`. The arguments must be value types (bools, enums), never request data, because every combination stays cached.
- Order every paginated query by a unique tiebreaker, normally the primary key, so pagination is stable.
