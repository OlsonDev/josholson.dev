---
paths:
  - "**/*.cs"
  - "**/*.csproj"
---

# C#

Formatting, language style, and naming live in `.editorconfig` (`[*.cs]`) and are enforced by `dotnet format --verify-no-changes --severity warn` from the repository root ([ADR 0028](../../docs/adr/0028-editorconfig-enforces-csharp-style.md)). This file covers choices it cannot express and records the intent behind some of them. Examples use metasyntactic names (see CLAUDE.md).

## Language

- Use modern language features by default.
- No primary constructors on classes: a parameter can't be `readonly` or named `_field`. Use a constructor and `private readonly` fields.
- `x is null` and `x is not null`, never `== null`, even for `Nullable<T>`.
- Compare enums with `is`: `x is Foo.Bar or Foo.Baz`, even if there's a single comparison.
- File-scoped namespaces. `Program.cs` uses top-level statements.
- `async` methods end in `Async`, except public methods in controllers (`*Controller.cs`) and tests: actions are named for the route, and MVC strips the suffix from action names anyway. Local constants are PascalCase.
- Expression-bodied members (`=>`) wherever the body is a single expression or statement: constructors, methods, properties. Put the `=>` on its own line below the signature, indented one level (`public Foo(Bar bar)` / `  => _bar = bar;`). RCS0062 enforces it.
- Choose between `switch`, `?:`, and `if` as described under "Choosing a branching form" below. Property patterns (`list is { Count: > 0 }`) read well wherever they replace a longer test.
- Extension members use C# 14 `extension(Type name) { ... }` blocks, never `this` parameters. Every member for one receiver goes in one block, and the helpers they call stay as private static methods beside it (`StringExtensions`).
- `var` for locals. Target-typed `new()` and collection expressions (`[]`) for fields, properties, and arguments, where the declared type supplies the type. A local that holds a new object or collection names the type on the right: `var foos = new List<Foo>(bars.Count)`, `var bazs = new List<Baz>()`, `await using var connection = new NpgsqlConnection(...)`, never `List<Foo> foos = new(...)` or `List<Baz> bazs = []`. The analyzers accept both forms, so this is a convention.
- `required` and `init` on properties. We can relax them later, and tightening them later breaks callers. `required` only forces the caller to write the assignment; it says nothing about null, so `required string? Foo` is valid.
- Raw string literals (`"""`) for multi-line text, especially embedded SQL.
- `foreach`, not `List<T>.ForEach`. LINQ is for queries and transformations, never side effects (such as in `.Select()` or `.Where()`).
- Lambda parameters are named for what they are, not `x`. A short abbreviation is fine in a short lambda where the type is obvious, e.g. `fooBars.Select(fb => fb.BarId)`.
- One type per file, named for the type. A file holding several types is named `{Name}.{Plural}.cs` (`Tag.Payloads.cs`, `Domain.Attributes.cs`): the request and response records of a feature and their shared `{Feature}Base`, or a family of attributes. `.editorconfig` turns MA0048 (file name must match type name) off for any `*.*s.cs`, so a plural second-to-last word is the whole convention. A family of arity variants of one generic type (`SqlMemoizer<T1>` to `<T1, T2, T3, T4>`) shares one file named for the type.
- Classes named `*Service` or `*Repository` are registered as scoped automatically by `ConventionRegistration`, so there are no per-feature registration classes. They must be `public sealed` (startup fails otherwise), and the suffixes are reserved for them: a fake, an abstract base, or a background job gets a different name. Other kinds of class get their own suffix and convention when the first one appears.
- An injected `*Service` or `*Repository` is named for its type, not its role: field `_tagRepository`, constructor parameter and local `tagRepository`. Other dependencies keep short names (`_connection`).
- Private instance fields are `_camelCase`. Private static and const fields are `_PascalCase`: the underscore says private, the capital says shared by every instance. Public and internal members are `PascalCase`; locals and parameters are `camelCase`. No `this.` unless required. `System` usings first, then the rest, each group alphabetical.
- Lists: a list of two or more items (arguments, parameters, type arguments, attributes, tuples, initializers, collection expressions, enum members) has either every item on the line of its opening token, or one item per line with none on the opening line. Never wrap right after the `(` and put several items on the next line, and never split a list mid-line. A last block lambda or initializer may run on past the opening line (`Foo(a, b, c => {` is fine). If a list is too long for one line, that is the signal to pass one object instead of many arguments. A list that is the value of a declaration (a field, property, local, or expression-bodied member) takes one item per line when it has two or more, so a new item is a one-line diff; as an argument or inside a larger expression it may stay on one line (`Assert.Equal(["foo", "bar"], baz)`). `mise run lists:check` (`scripts/list-layout.cs`) enforces it. Roslynator's [RCS0053](https://josefpihrt.github.io/docs/roslynator/analyzers/RCS0053) stays off: it accepts the wrap-after-`(` layout and also rejects the trailing block lambdas and `||` chains below. Upstream requests: [dotnet/roslynator#1861](https://github.com/dotnet/roslynator/issues/1861) and [Meziantou.Analyzer#1748](https://github.com/meziantou/Meziantou.Analyzer/issues/1748); when one lands, enable it and retire the script.
- Trailing commas, never comma-first. Multi-line parameter lists, argument lists, initializers, anonymous objects, `switch` arms, and enum members put one item per line, each ending in a comma. The Roslyn formatter rewrites comma-first layouts in object and anonymous initializers, `switch` expressions, and enums, so it can't be applied consistently (SQL stays comma-first).
- An object, anonymous-object, or collection initializer with two or more members is K&R: the `{` ends the first line, each member gets its own line starting on the second, and each ends in a comma. One member may stay on one line, except in a dictionary (any initializer of keys and values), which always spans lines so that adding a key does not rewrite the line. `IDE0055` enforces the layout once an initializer spans lines, and `MA0007`/`RCS1260` enforce the comma; nothing yet enforces that two or more members span lines.

  ```csharp
  var foo = new Foo(bar) {
    Baz = qux,
    Quux = corge,
  };
  var grault = new Foo(bar) { Baz = qux };
  ```
- A wrapped ternary starts the `?` and `:` lines with the operator, never ends a line with one (RCS0028 enforces the side). A nested ternary is indented one step further than its parent; nothing enforces the indentation.

  ```csharp
  var foo = bar
    ? baz
    : qux
      ? quux
      : corge;
  ```
- A `||`/`&&` chain wraps when it passes 120 columns or has four or more operands, and then every operand gets its own line. Each line starts with the operator, dedented so the operand after it lines up with the first operand (the repeated `foo` below). No formatter or analyzer enforces this; it is a convention.

  ```csharp
  => foos.Any(foo => foo.Bar is "baz"
                  || foo.Qux.EndsWith("quux", StringComparison.Ordinal)
                  || foo is "corge");
  ```
- An `is` pattern with three or more alternatives, or one that passes 120 columns, puts each `or` on its own line, aligned under the `is` so the constants line up. Two alternatives stay inline. If the subject is so long that the `is` would land past column 40, hoist the subject into a local first. A pattern that mixes `and` or `not` (three characters, so it cannot line up) stays on one line or becomes named booleans or a `switch`. Patterns take constants only: a class with static fields (`BindingSource`) uses a static array and `Contains`. Nothing enforces the alignment, and renaming the subject means re-indenting the `or` lines.

  ```csharp
  => foo is Bar.Baz
         or Bar.Qux
         or Bar.Quux;
  ```
- Keep lines to about 120 columns and wrap longer ones. This is a guideline, not enforced by a tool.
- Analyzers (BannedApiAnalyzers, Meziantou, Roslynator) fail the build ([ADR 0030](../../docs/adr/0030-analyzers.md)). The loose scripts and hooks follow the same rules and are checked by `mise run scripts:check`. To allow one use, `#pragma warning disable <ID>` with a reason; to change a rule everywhere, edit `.editorconfig`.
- `BannedSymbols.txt` (repository root) lists banned APIs (RS0030), each with the replacement as its message. Add to it rather than writing a rule here. Every overload needs its own line with a full signature; a type ban misses bare declarations. Time is NodaTime ([ADR 0029](../../docs/adr/0029-nodatime-for-time.md)).
- HTTP header names come from `Microsoft.Net.Http.Headers.HeaderNames` (`HeaderNames.ContentType`, `HeaderNames.Origin`), never string literals, in the API and its tests. A typed accessor such as `request.Headers.Origin` is just as good. A script that is not an ASP.NET app (`scripts/passkey-cli.cs`) has no such package and may spell the names out. Nothing enforces this yet; a literal is only caught in review.
- Collections are `List<T>`, not `IEnumerable<T>`, `IReadOnlyList<T>`, or `IReadOnlyCollection<T>`, wherever the caller can supply one: parameters, return types, and payload properties (model binding fills a `List<T>` as readily). Dapper's buffered results go through `AsList()` (the `QueryAsync` extension already does), which hands back the `List<T>` Dapper built without a copy. When a transformation builds a new collection, pre-allocate it from the source's size (`var foos = new List<Foo>(bars.Count)` and a `foreach`, not `Select(...).ToList()`). Tests pay for the conversion (`[]`, `[.. array]`).
- Several lookups in a loop need a `Dictionary` or `ILookup`, not a scan per iteration. Compute an aggregate once into a named variable.

## Choosing a branching form

What decides it is what the arms test, not how many there are.

- **`switch` expression** when every arm tests the same subject (one value, or a tuple of values) against constants, enum members, or patterns (type, property, relational). For an enum the compiler warns about a missing member. Never `switch` on a `bool`: that is a ternary written longer.
- **Ternary**, nested when needed, when each arm asks a different question. A chain is also the better fit when the conditions are much longer than the outcomes: the outcomes line up under the `?` and `:`, where a `switch` would push the conditions into `when` guards. Each level is indented one step further.
- **Merge shared outcomes.** If two branches of a chain produce the same outcome, join their conditions with `||` or `&&` instead of repeating the outcome.
- **Named booleans** when a condition is compound and every part is cheap and safe to evaluate eagerly. If a later condition is only safe or worthwhile because an earlier one failed (a null check, an expensive call), keep it inline in the chain: a local would evaluate it too soon.
- **`if`/`else`** when a branch has side effects or is a statement.

No analyzer measures branch or condition length, so when a ternary has to wrap because a branch is long, that is a judgment call: prefer named booleans if they are safe, otherwise `if`/`else`.

```csharp
// Ternary: one condition, two short outcomes
var foo = isBar ? "baz" : "qux";

// Nested ternary: different questions, and the conditions are longer than the outcomes
var foo = bar is null
  ? "baz"
  : bar.Items.Any(item => item.IsQux && item.Corge is not null)
    ? "quux"
    : "grault";

// switch: every arm tests the same subject
var foo = bar switch {
  Bar.Baz => 1,
  Bar.Qux or Bar.Quux => 2,
  _ => 3,
};

// Avoid: a repeated outcome
var foo = isBar ? "baz" : isQux ? "baz" : "quux";

// Instead, merge the conditions
var foo = isBar || isQux ? "baz" : "quux";

// Avoid: a compound branch hides the logic
var foo = isBar ? baz : !baz && (qux is null || qux.Contains(quux));

// Instead, name the parts (cheap, and safe to evaluate eagerly)
var isCorge = isBar == baz;
var isGrault = qux is null || qux.Contains(quux);
var foo = isCorge && isGrault;

// Avoid: switch on a bool
var foo = isBar switch { true => "baz", false => "qux" };

// if/else: side effects
if (isBar) {
  baz.Add(qux);
} else {
  baz.Remove(qux);
}
```

## Data access

- Dapper with Npgsql, SQL written inline. No `Dapper.Contrib`.
- Repositories take the scoped `NpgsqlConnection` (opened once per request in `AddDatabase`) and never open or dispose it. A service that needs atomicity begins a transaction on that connection and calls `CommitAsync`; disposing it uncommitted rolls back. Repositories on the same connection join it automatically.
- Repository classes only run queries. Logic that isn't a query belongs in a service.
- Queries that differ only in their filter share one private method. It holds the select list once and takes the `ON` condition (SQL the code wrote, never request data) and the parameters; each public `Get...By...` method is a one-line call into it (`PasskeyRepository.GetAllByConditionAsync`). Add a filter by adding a method, not by copying the query.
- Run queries through the `NpgsqlConnection` extensions in `JoshOlson.Shared/Data/NpgsqlConnectionExtensions` (`QueryAsync<T>`, `QuerySingleAsync<T>`, `QuerySingleOrDefaultAsync<T>`, `QueryMultipleAsync`, `ExecuteAsync`): pass the `sql`, the `parameters`, then the `CancellationToken`. They build the `CommandDefinition`, so the token cannot be forgotten. Import `JoshOlson.Shared.Data`; without it, Dapper's own overloads apply, and they take no token. Every project that touches the database (the API, the seed tool) goes through it, so Dapper's settings are the same everywhere ([ADR 0036, the shared library](../../docs/adr/0036-shared-library.md)).
- `JoshOlson.Shared/Data/DapperConfiguration` runs at assembly load (a module initializer, so tests get it too). It sets `DefaultTypeMap.MatchNamesWithUnderscores = true` so `bar_id` fills `BarId` (name a column alias for its property, and the row type needs no mapping code), and registers handlers for NodaTime's `Instant`, for `IPAddress`, and for `List<string>` (a `text[]` column or parameter), which Dapper rejects or expands otherwise. Have the query do the translating (a `CASE` that returns the enum's number, `{(short)Foo.Bar}`, or `(a.x || '.' || b.y)` for a full name) so the result fills the model type directly, with no row type in between. Enums are `: short`, the smallest type Postgres has, and Dapper fills one from a `smallint`. To look rows up by a key in code (a parent's children), use `QueryDictionaryAsync<TKey, T>` for one row per key and `QueryGroupedAsync<TKey, T>` for a list per key: the first column is the key, everything from the `splitOn` column on fills the `T`, so the model carries no database ids.
- Insert with `RETURNING` and read the key with `QuerySingleAsync<int>`.
- Aggregate in SQL (`count`, `sum`), not in C#.

```csharp
public async Task<List<Bar>> GetCorgesAsync(int barId, CancellationToken cancellationToken) {
  var sql = $"""
    SELECT
        c.corge_id
      , c.name
    FROM (SELECT)   AS _
    JOIN foo.bar    AS b  ON b.foo_id          = @barId
    JOIN foo.corge  AS c  ON c.bar_id          = b.bar_id
                         AND c.current_status  = {(short)CorgeStatus.Active}
    ORDER BY c.sort_order, c.corge_id;
    """;
  var parameters = new { barId };
  return await _connection.QueryAsync<Bar>(sql, parameters, cancellationToken);
}
```

## Controllers

- A controller is named for its entity, singular, like its service and repository (`TagController`, `TagService`, `TagRepository`). The route is a literal plural for the collection (`[Route("tags")]`), not `[controller]`.
- `[ApiController]`, attribute routing, async actions taking a `CancellationToken`, and no logic: call a service and return an `ActionResult`. Not minimal APIs ([ADR 0026](../../docs/adr/0026-rest-json-with-controllers.md)).
- Every action needs a signed-in session by default (a global `SessionFilter`, [security.md](../../docs/security.md)). Mark a public action `[AllowAnonymous]`, and add its `Controller.Action` name to the list in `SessionFilterTests`. Read the user with `HttpContext.GetSession()`.
- The constructor takes what most actions use. A dependency only one action needs is a `[FromServices]` parameter on that action.
- Validation: the service has `Validate{Operation}Async` taking the operation's arguments and returning `ValidationErrors`; the action calls it first and returns `UnprocessableEntity(errors.ToProblemDetails())` (422) when `errors.Any`. Common rules are attributes on the payload (DataAnnotations, plus `[Trimmed]`, `[KebabCase]`, `[HexColor]` for our database domains), run by `PayloadValidator`; write code only for what attributes cannot express, such as uniqueness. A payload type needs `[ValidatableType]` or the validator throws. `Get` and `Delete` have nothing to validate: a missing row is a `404`.
- Incoming strings are trimmed before an action sees them, and text that is only whitespace arrives as `null` (`Common/Binding`: a JSON converter for bodies, a model binder for the route, query, form, and headers). A padded name is stored trimmed, not rejected, and a blank optional field becomes `NULL`, as the nullable text columns expect. `[Trimmed]` stays on the payload for code that builds one without JSON, and for the 422 a service test can provoke.
- Create returns `201` with a `Location` header and no body. Update and delete return `204`, or `404` when the row does not exist. Errors are `ProblemDetails`, with no try/catch for database constraint violations: `PostgresExceptionHandler` maps them.

## SQL in C#

SQL in a string follows [sql.md](sql.md) for formatting, naming, and style, the same as a `.sql` file.

- Alignment is computed on the text as written in C#. An interpolated expression (`{Foo("bar")}`) counts at its width in the source, as if it were the SQL itself, not at the width of what it expands to. The string is read as C# far more often than as compiled text or in a debugger, and an expansion that depends on a parameter or a branch has no single width to align to. So the `AS` of a select list sits two spaces after the longest item in the source, interpolations included, and likewise for every other column of alignment in [sql.md](sql.md).

- Never put a value into the SQL text, whether by interpolation or concatenation. Always use a parameter and pass the value separately.
  - It is how injection happens.
  - It also defeats Npgsql's prepared-statement reuse, because every distinct value is a new statement.
- Pass parameters as an anonymous object, one property per line, with no redundant aliasing: `new { request.Name }` binds `@name`. Dapper takes the property name and Npgsql matches it case-insensitively, so PascalCase properties bind to camelCase placeholders.
- Name parameters in camelCase after the C# variable (`@barId`). Dapper binds by name, so a snake_case parameter would need a snake_case property; mapping is too verbose and camelCase stands out.
- You may interpolate enum values, especially hardcoded ones like the example above. This allows for a single prepared-statement plan.
- You may interpolate SQL that the code chose: a `ORDER BY` column(s) picked from a fixed list, or optional `JOIN..ON/WHERE` predicate(s). Build the predicate in or out in C# rather than writing `@x IS NULL OR col = @x`, which can cost a prepared statement its good plan.
- SQL that varies only by which optional parts are included is built by a static method taking those choices, and memoized so it is built once per combination: `private static readonly SqlMemoizer<bool, bool> _FooSql = new(BuildFooSql);` then `_FooSql.ForValues(a is not null, b is not null)`. The arguments must be value types (bools, enums), never request data, because every combination stays cached.
- Order every paginated query by a unique tiebreaker, normally the primary key, so pagination is stable.
