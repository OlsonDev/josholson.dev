# ADR 0037: Target .NET 11 and C# 15

- Status: Accepted
- Date: 2026-11-10

## Context

.NET 11 shipped on 2026-11-10 with C# 15. It is a Standard Term Support release, and .NET 10 (long-term support) ends on the same day, 2028-11-14, so staying on .NET 10 buys no extra support time. The project had one real choice: take the new features and the early churn, or wait. Nothing here is in production yet, so churn is cheap now and expensive later.

## Decision

- Target `net11.0` in `Directory.Build.props`, and pin SDK 11.0.100 in `global.json` and `mise.toml`. C# 15 is the default language version, so no `LangVersion` is set.
- Packages follow the framework where a version exists (`Microsoft.AspNetCore.Mvc.Testing` 11.0.0). Npgsql stays on 10.0.3 until an 11 release exists; it runs on the .NET 11 runtime.
- **Collections with a comparer or capacity use `[with(...)]`** in a collection expression where the declared type supplies the type (fields, properties, assignments). IDE0028 enforces it. A local keeps `var x = new List<Foo>(n)`, as `csharp.md` says.
- **A closed set of cases in one assembly is `closed`** (an abstract record whose sealed cases are all ours), so a switch that misses a case fails to compile. Not applied yet; see Consequences.
- Labeled `break` and `continue` replace `goto` and flag variables for nested loops (IDE0410). We have none today.
- The generated validation code names `DateTime` and the other banned types, and RS0030 checks generated code. `JoshOlson.Api/Common/Validation/GeneratedCodeSuppressions.cs` suppresses RS0030 in that one generated namespace only. An `.editorconfig` or `.globalconfig` section cannot do it: generator output has no directory in its path, and a global section must be an exact absolute path.

## Alternatives considered

- **Stay on .NET 10 until .NET 12:** gains nothing in support, and moves the same churn later.
- **Switch RS0030 off for the API project:** the ban on `DateTime` matters most there ([ADR 0029 (NodaTime for time)](0029-nodatime-for-time.md)).
- **Union types (`union`):** nothing in the code is a "one of several unrelated types" value yet. Revisit for ceremony outcomes in the API.

## Consequences

- `Microsoft.Extensions.Validation` changed: `ValidateContext` takes a `ServiceProvider` instead of a `ValidationContext`, and each error is a `ValidationError` with an `ErrorMessage`. Only `PayloadValidator` changed. The library is no longer experimental, so the project's `ASP0029` suppression is gone.
- `TextWriter.WriteLineAsync` has overloads that take a token, and MA0040 requires them.
- Roslynator 5.0.1 and Meziantou 3.0.317 ran cleanly on C# 15 code, but neither is documented to understand `union` or `closed`. Check when one is first used.
- `closed` on `SeedValue` and `PendingPasskeyChange` is the next change, and is separate from the upgrade.
