# ADR 0041: Records for parameter groups and returned tuples, and `Result<T, TErrors>` unions for failures a caller handles

- Status: Accepted
- Date: 2026-11-15

## Context

A survey of every signature in the repository found three things that share a cause.

- **Wide signatures.** 51 of 697 production signatures take five or more parameters, 44 of them in `JoshOlson.Database`. A few groups account for most: an owner connection in 40 signatures, a `List<string> problems` collector in 21, and `LayerTree tree, SeedSchema schema` in 10, often with a layer name, an anchor, and an `editing` flag. Tests declare few wide methods but call these ones directly, so they feel every parameter.
- **Tuple returns.** 26 signatures return a value tuple. Some are private and deconstructed where they are called, which reads well. Others are in public types, carry three collections, or return the same values another method takes as arguments.
- **Exceptions used as return values.** The seed tool throws `SeedFormatException` 113 times. About 53 of those are fatal: only `SeedApp.HandleException` sees them. The rest feed 13 `catch (SeedFormatException)` sites that turn the message into an item in a list and keep going, and several of the throwing methods had collected a list themselves and joined it into text in order to throw it. The list is flattened, carried by an exception, and built again as text.

C# 15 has `union` types, which [ADR 0037 (.NET 11 and C# 15)](0037-dotnet-11-and-csharp-15.md) left unused until a value was "one of several unrelated types". A value or the problems that kept it from being made is that.

## Decision

### Parameter groups

- Values that travel together through several signatures become one type, a record named for what the group is. More than five parameters on a method is the signal to look for one. A constructor that lists injected dependencies is not a group, and stays a list.
- When the same values are one method's arguments and another's return, they are one record, used for both.
- An object that lives for one run (`MigrationRun`) holds the run's values in fields, the `CancellationToken` included. Everywhere else the token stays the last parameter of every public async method. There is no ambient token.

### Tuples

- A value tuple is fine as the return of a private method (or a test helper) when it has two members and the caller deconstructs it at the call.
- Anything else is a record: a tuple in a public or internal signature, in a property, in a collection's type argument, or with three or more members.
- A tuple of a value and a nullable problem (`(Foo? Value, string? Problem)`) is a `Result` (below). A tuple of a flag and its message (`(bool Success, string? Error)`) is the message alone, null when there is none.

### Failures

- **A failure that some caller handles is returned. A failure that only the top-level handler sees is thrown.** A method whose caller catches its exception to keep going returns a union. A method whose failure ends the run throws, as now.
- The union is `Result<T, TErrors>`, a `readonly union` of the value and the errors, in `JoshOlson.Shared`. Each project supplies its own `TErrors`; the seed tool's holds the list of problem messages.
- `TErrors` is a plain value, never an exception. It implements `IErrors<TSelf>`, whose one member is `static abstract Exception ToException(TSelf errors)`. `Result.OrThrow()` gives the value or throws what `ToException` builds, at that moment, so the stack trace starts at the call that gave up and no exception is made that may never be thrown. A caller with nothing to recover calls `OrThrow()`.
- Unions stay inside the libraries for now. A command calls `OrThrow()`, and `SeedApp.HandleException` remains the one place that prints a failed run and exits 1. A method's return type can change later and the union be passed further up.
- A recursive-descent parser that stops at the first fault (`SchemaLockParser`, `YamlNodes`, the shape checks in `LayerParser`) keeps throwing inside: C# has no operator that passes a failure up, so a returned failure would need a check after every call. If a caller handles the parser's failure, its public method catches once and returns a `Result`.
- A method with several unrelated outcomes, of which a failure is only one kind, gets a union of its own with a case type per outcome, in place of an enum paired with a nullable value.
- Code that branches on a union is a `switch` expression with no `_` arm, like a `closed` hierarchy. A union is never left `default`: its contents are then `null`, and the compiler does not warn.

## Alternatives considered

- **`Result<T>` with one fixed errors type:** shorter to write, but the API's errors are keyed by field (`ValidationErrors`) and the seed tool's are a flat list, and `OrThrow()` in `JoshOlson.Shared` could not name each project's exception.
- **An exception as the errors type (`Result<T, SeedFormatException>`):** `OrThrow()` would be `throw failure`, but an exception would be built for every failure whether or not it is thrown.
- **A per-project name that hides the second type argument (`SeedResult<T>`):** C# has no generic alias, so it would be a separate union that converts to `Result<T, TErrors>` only through a member written for it. The second argument is written out.
- **A constructor constraint on `TErrors`:** `new()` is the only one C# has. A static abstract interface member does the job.
- **Unions to the boundary of the library:** every command would branch on its result, and every fatal check would need converting. Signatures would say more, at the cost of most of the 69 tests that assert a thrown `SeedFormatException`. Left open as a later step, which the decision above does not block.
- **Unions inside the parsers:** see the parser bullet above.

## Consequences

- None of this is built yet. The work is phased: `Result<T, TErrors>`, `IErrors<TSelf>`, and the seed tool's errors type first, with no change in behavior; then records for the tuples; then the parameter groups; then the union returns, one method at a time from the bottom up. The API's outcome union is separate and small, and is the first use: `PasskeyService.StartRemovalAsync` returns `RemovalResult` (the pending change, `PasskeyNotFound`, or `LastPasskey`) in place of the `RemovalOutcome` enum and a nullable change.
- `UnexpectedAaguidException` stays an exception. Nothing catches it: a real AAGUID means attestation or a browser changed, which is a decision for a person and not an outcome a caller handles, so by the rule above it is thrown. `WebAuthnService.VerifyRegistrationAsync` keeps returning null when a response does not verify, which is one outcome and needs no union.
- A spike built with this repository's settings showed Roslynator and Meziantou run on `union` declarations, which answers the open point in ADR 0037 (.NET 11 and C# 15). IDE0250 requires `readonly union`, which the compiler accepts (the struct it makes carries `IsReadOnlyAttribute`) although the language reference only ever writes `union`; if a later SDK disagrees, the choice is that rule or the modifier. IDE1006 requires type parameters to start with `T`, and MA0048 treats a union as any other type.
- A value-type case is boxed (`Result<int, TErrors>`). Nothing here is hot enough to care.
- `SeedFormatException` stays, as what the seed tool's errors become when thrown and as the parsers' way out.
- The two git hook validators return `string?` in place of `(bool Success, string? Error)`. `SeedValueReader.Read`, which returns a problem and gives the value through `out`, stays as it is: it is internal with two callers.
