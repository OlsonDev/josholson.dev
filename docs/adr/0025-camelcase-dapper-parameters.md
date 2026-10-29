# ADR 0025: camelCase Dapper parameters

- Status: Accepted
- Date: 2026-10-28

## Context

SQL is snake_case everywhere, but Dapper binds parameters by name, and C# members are not snake_case. `sql.md` and `csharp.md` disagreed about what a placeholder looks like. Result columns are already handled by `DefaultTypeMap.MatchNamesWithUnderscores = true`, which lets `bar_id` fill `BarId`. Dapper has no equivalent hook for parameter names.

## Decision

- Placeholders are camelCase and named after the C# variable or property: `@barId`, `@name`.
- Parameters are an anonymous object, one property per line, with no aliasing: `new { request.Name }` binds `@name`. Dapper builds the parameter from the property name, and Npgsql matches it to the placeholder case-insensitively, so PascalCase properties bind to camelCase placeholders. Repository and controller tests cover this.
- Two placeholders that differ only in case must not appear in one statement.

## Alternatives considered

- snake_case placeholders with snake_case properties (`new { bar_id = barId }`): uniform in SQL, but noisy and unidiomatic in C#.
- A helper that turns an object into snake_case `DynamicParameters`: a reflection pass per call (or a source generator), and every query has to go through it. This is still on the table as an option.
- Positional parameters (`$1`): unreadable.

## Consequences

- No machinery to maintain (for now).
- A camelCase placeholder in a snake_case query stands out as "this is a value", which helps reading.
- Harder to align things like:

```sql
WHEN NOT MATCHED
  THEN INSERT ( foo_bar_id,     baz_qux_id)
  VALUES      (@foo_bar_id, s.baz_qux_id)
```
