---
paths:
  - "**/*.cs"
  - "**/*.csproj"
---

# C#

Formatting lives in `.editorconfig`. This file covers choices it cannot express. Examples use metasyntactic names (see CLAUDE.md).

## Language

- Use modern language features by default.
- No primary constructors on classes: a parameter can't be `readonly` or named `_field`. Use a constructor and `private readonly` fields.
- `x is null` and `x is not null`, never `== null`, even for `Nullable<T>`.
- Compare enums with `is`: `x is Foo.Bar or Foo.Baz`, even if there's a single comparison.
- File-scoped namespaces.
- Prefer `switch` expressions and property patterns (`list is { Count: > 0 }`) where they read better.
- `var` for locals. Target-typed `new()` for fields, properties, and arguments.
- `required` and `init` on properties. We can relax them later, and tightening them later breaks callers. `required` only forces the caller to write the assignment; it says nothing about null, so `required string? Foo` is valid.
- Raw string literals (`"""`) for multi-line text, especially embedded SQL.
- `foreach`, not `List<T>.ForEach`. LINQ is for queries and transformations, never side effects (such as in `.Select()` or `.Where()`).
- Lambda parameters are named for what they are, not `x`. A short abbreviation is fine in a short lambda where the type is obvious, e.g. `fooBars.Select(fb => fb.BarId)`.
- Private instance fields are `_camelCase`. Private static and const fields are `_PascalCase`: the underscore says private, the capital says shared by every instance. Public and internal members are `PascalCase`; locals and parameters are `camelCase`. No `this.` unless required. `System` usings first, then the rest, each group alphabetical.
- Keep lines to about 120 columns and wrap longer ones. This is a guideline, not enforced by a tool.
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

## SQL in C#

SQL in a string follows [sql.md](sql.md) for formatting, naming, and style, the same as a `.sql` file.

- Never put a value into the SQL text, whether by interpolation or concatenation. Always use a parameter and pass the value separately.
  - It is how injection happens.
  - It also defeats Npgsql's prepared-statement reuse, because every distinct value is a new statement.
- Name parameters in camelCase after the C# variable (`@barId`). Dapper binds by name, so a snake_case parameter would need a snake_case property; mapping is too verbose and camelCase stands out.
- You may interpolate enum values, especially hardcoded ones like the example above. This allows for a single prepared-statement plan.
- You may interpolate SQL that the code chose: a `ORDER BY` column(s) picked from a fixed list, or optional `JOIN..ON/WHERE` predicate(s). Build the predicate in or out in C# rather than writing `@x IS NULL OR col = @x`, which can cost a prepared statement its good plan.
- Order every paginated query by a unique tiebreaker, normally the primary key, so pagination is stable.
