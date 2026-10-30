# ADR 0028: `.editorconfig` enforces C# style; `Program.cs` uses top-level statements

- Status: Accepted
- Date: 2026-10-29

## Context

C# style lived in prose (`.claude/rules/csharp.md`), where nothing checked it. `dotnet format` can enforce formatting, language style, and naming from `.editorconfig`. Analyzers are a separate, later decision.

## Decision

- **`[*.cs]` in `.editorconfig` is the source of truth** for everything `dotnet format` supports. Every option we chose is set to `:warning`, so `dotnet format` applies it and, with `EnforceCodeStyleInBuild` and `TreatWarningsAsErrors` set ([ADR 0030](0030-analyzers.md)), the build fails on it. Run `dotnet format --verify-no-changes --severity warn` from `api/`.
- **Style:** K&R braces, `else`/`catch`/`finally` on the same line, braces not enforced, operators at the start of a wrapped line, `var` everywhere, expression-bodied members, collection expressions, ternaries for simple assignments (not returns, which would flag guard clauses), no redundant parentheses, no `this.`, accessibility modifiers always explicit, `using` outside the namespace with `System` first and no blank line between groups, and no blank line after `{`, `?`, `:`, or `=>`.
- **Naming:** the rules in `csharp.md`, plus `Async` on `async` methods (public methods in `*Controller.cs`, and everything in `*.Tests.cs`, are exempt) and PascalCase local constants. MVC strips the `Async` suffix from action names, so a suffixed action would need `CreatedAtAction("Get", ...)` instead of `nameof(Get)`; public controller methods are exempt to avoid that.
- **Cleanup rules** (unused usings, casts, private members, unreachable code, and the like) are warnings.
- **Left off:** IDE0058 (unused expression values), IDE0046 (ternary for returns), IDE0290 (primary constructors on classes), IDE0073 (file headers), IDE0010 and IDE0072 (missing switch cases). Unused parameters are only a suggestion.
- **`Program.cs` uses top-level statements.** The xUnit hand-off described in [ADR 0023](0023-api-feature-folders-and-colocated-tests.md) is unchanged but now sits at the top of the file behind `#if INCLUDE_TESTS`, and `IsTestRun` is a local function. The generated `Program` class is still what `WebApplicationFactory<Program>` uses.

## Alternatives considered

- Prose conventions only: drift without anything checking them.
- Allman braces, the .NET default: more vertical space, and unlike the SQL embedded in the C#.

## Consequences

- A new style option goes in `.editorconfig`; `csharp.md` keeps only what `.editorconfig` cannot express.
- Experimental options (the `*_experimental` blank-line rules) may be renamed or removed by Roslyn; IDE2005 and IDE2006 already were.
- Naming severity inside a `dotnet_naming_rule` is not honored at build time, so `IDE1006` is also set by rule ID.
