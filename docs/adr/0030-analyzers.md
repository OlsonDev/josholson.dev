# ADR 0030: Analyzers: BannedApiAnalyzers, Meziantou.Analyzer, and Roslynator

- Status: Accepted
- Date: 2026-10-29

## Context

[ADR 0028](0028-editorconfig-enforces-csharp-style.md) puts style in `.editorconfig`. Style cannot catch banned APIs, or the mistakes a code reviewer would: culture-sensitive string comparison, blocking on tasks, a missed `CancellationToken` overload. The three packages are referenced from `JoshOlson.Api.csproj` with `PrivateAssets="all"`, and `TreatWarningsAsErrors` makes every warning fail the build.

## Decision

- **`Microsoft.CodeAnalysis.BannedApiAnalyzers`** reads `BannedSymbols.txt` (RS0030). The message of each entry names the replacement. A method ID with no parameter list matches only the parameterless overload, so every overload gets its own line; a type ban misses bare declarations of the type. Time is NodaTime ([ADR 0029](0029-nodatime-for-time.md)).
- **`Meziantou.Analyzer`** runs with its default severities, plus these changes in `.editorconfig`:
  - Off: MA0004 (`ConfigureAwait`, ASP.NET Core has no `SynchronizationContext`), MA0016 (collection abstraction, a performance cost for little gain), MA0026 (TODO comments).
  - On: MA0032, MA0121, MA0141, MA0142, MA0148, MA0149, MA0155.
  - MA0002 reports only APIs that are not ordinal by default (`Order()`, `OrderBy`), not `Dictionary<string, _>`.
  - MA0048 (file name must match type name) is off for `*.*s.cs`: a file holding several types is `{Name}.{Plural}.cs`, and tests are `{Name}.Tests.cs`.
- **`Roslynator.Analyzers`**: the `roslynator` category is set to `warning`, which applies only to rules Roslynator enables by default (about 130 of ~270). Rules that ship off, many of them option-driven style rules, stay off until a `dotnet_diagnostic.RCSnnnn.severity` line turns them on; no bulk switch does it (`roslynator_analyzers.enabled_by_default` works only in a user-wide `.roslynatorconfig`). Rules that contradict a decision here (braces, `var`, redundant parentheses, `//` comments, the ternary and `=> await` style, the `Async`-suffix and file-per-type exceptions) are disabled in `.editorconfig`.
- **Every rule has a line.** Every rule in the three packages has a `dotnet_diagnostic.<ID>.severity` line in `.editorconfig`, set to what it did when added. `mise run analyzers:check` (`scripts/analyzers.cs`) loads the restored packages, lists their rules, and fails if one has no line, so an upgrade that adds a rule forces a decision. `mise run analyzers:add` appends new rules under the `# analyzers:unreviewed` marker at their current behavior, to be edited in place. Non-configurable descriptors (Roslynator's `FadeOut` companions) are skipped. A new rule that is on by default still fails the build immediately through the category switches; the check also catches the ones that ship off.
- **`Roslynator.Formatting.Analyzers` is not used.** `.editorconfig` and `dotnet format` own formatting.

## Alternatives considered

- A curated Roslynator allowlist of about 25 rules: quieter and stable across releases, but it would hide rules we haven't looked at. Everything-on with an explicit disable list surfaces a new rule as a build failure to decide on.
- `AnalysisMode` all, or the .NET `CA` analyzers: not decided here. They are the next candidates.

## Consequences

- A Roslynator, Meziantou, or SDK upgrade can add warnings, which are errors. Fix them, or add the rule to the disable list with a reason.
- Duplicates between IDE, Meziantou, and Roslynator rules are possible and harmless, as `dotnet format` applies a fix once.
- Local analyzer use needs nothing beyond the build. `dotnet format --verify-no-changes --severity warn` also applies the fixes the packages provide.
- `.githooks/pre-commit` runs `analyzers:check` when a commit stages a package, project, or `.editorconfig` file (`.githooks/analyzer-check-trigger.cs`, tested in `pre-commit.tests.cs`), so an upgrade cannot be committed with an undecided rule. Other commits skip it.
- `analyzers:check` also runs by hand after any package upgrade. IDE style rules run only under `dotnet format`, not in the build, since `EnforceCodeStyleInBuild` is not set.
