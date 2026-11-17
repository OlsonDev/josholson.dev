# ADR 0042: The OpenAPI document, generated TypeScript types, and enums as numbers with names

- Status: Accepted
- Date: 2026-11-16

## Context

[ADR 0026 (REST and JSON with controllers)](0026-rest-json-with-controllers.md) said the API would publish an OpenAPI document and `web/` would generate its TypeScript types from it once `web/` existed. `web/` now exists, and its `Tag` type was written by hand, so a change to `TagResponse` would reach the front end as a runtime surprise.

The wire format of enums was open. [ADR 0012 (enum-like columns as `SMALLINT`)](0012-enum-like-columns-as-smallint.md) fixes the database side: 1-based numbers, never reused. `PasskeyTransport` was already in a response payload, written as numbers (`[1, 2]`), because that is what System.Text.Json does by default.

## Decision

- **The document** comes from `Microsoft.AspNetCore.OpenApi`, with no UI and no runtime endpoint. `dotnet run --project JoshOlson.Api -- openapi <path>` writes it. It builds the host and never starts it, so it needs no database (the connection string is a placeholder). The document is an intermediate: it is not committed.
- **The types** are `web/src/lib/api/schema.ts`, generated from the document by `web/scripts/generate-api-types.ts` (on `openapi-typescript`), with every schema also exported at the root (`import type { TagResponse } from '#lib/api/schema.ts'`). `web/` derives its types from it, so an API change is a type error in `web/`. The file holds the payloads and enums the web sends and receives, and no route table: that is the API's, it would be two thirds of the file, and the generated client reads routes from the document.
- **`schema.ts` is committed, and nothing checks it.** `web/` builds without .NET, so the file has to be in the repository. A PR shows the API's surface in a few lines, however many controllers and validators produced it, and two branches that change it incompatibly meet in this file. `mise api:openapi` regenerates it, and a controller or payload change is followed by running it. A drift check was built and removed: the file is a view for the web, and what is stale in it fails to type-check where it is used. The generator prints 4 spaces and no trailing commas, so `mise api:openapi` formats the file with `bun fmt` like the rest of `web/`, and the formatter check covers it. It is marked generated in `.gitattributes` and excluded from the linter.
- **A library type is opaque.** The passkey endpoints take and return the Fido2 library's WebAuthn types, and the web relays them between the API and the browser without reading inside. `ContractSchemaTransformer` empties the schema of a type that is neither ours, the framework's, nor NodaTime's, so the document has an empty schema for each (`unknown` in TypeScript) and none for their internals (47 schemas became 20). An endpoint the web will not use is left out of the document with ASP.NET's own `[ApiExplorerSettings(IgnoreApi = true)]`, and whatever only it reached goes with it.
- **One set of JSON settings**, `ApiJson.Configure`, goes to MVC (what the API sends and reads) and to `ConfigureHttpJsonOptions` (what the OpenAPI generator reads, which is a separate options object), so the document cannot describe something the API does not do.
- **Numbers are read strictly** (`JsonNumberHandling.Strict`). The web defaults also read `"5"` as an int, which made the generator describe every integer as "an integer or a string of digits" (`number | string` in TypeScript). A number sent as a string is a client's mistake, so it is a `400`, and the document says `integer`.
- **A schema transformer** (`ContractSchemaTransformer`) corrects what the built-in generator still gets wrong: a NodaTime `Instant` is described as an empty object (it is an ISO 8601 string), and an enum as any integer (see below). A global `Produces("application/json")` filter keeps `text/plain` and `text/json` out of every response.
- **Enums are their numbers on the wire**, the same numbers the database stores, which is what System.Text.Json writes by default, so there is no converter. The transformer lists an enum of ours as `enum: [1, 2, ...]` with `x-enum-varnames`, and `openapi-typescript` turns that into a real TypeScript enum (`PasskeyTransport.Usb`), so the front end reads names and sends numbers. An enum from a library keeps the library's own format.
- **A number that is not one of the enum's values** (`0`, `99`) is for a validation attribute on the payload property to refuse (`[EnumDataType]`, the "enum membership" rule of ADR 0026), as a `422` that names the field. Nothing refuses it at the JSON reader. No request payload takes an enum yet, so the first one adds the attribute and the test that proves it.
- **Drift between an enum and its column** is caught by a test for each enum stored in a column, in the shape of `PasskeyTransportTests`: every value is stored and read back, and the next number is refused by the range check. The table in [docs/database.md](../database.md) stays a hand-written mirror.

## Alternatives considered

- Names on the wire (`"smartCard"`): readable in a browser's network tab, and the generated TypeScript is a union of words. It was built and then replaced, because a number is smaller on the wire and matches the database, and `x-enum-varnames` gives the TypeScript its names anyway. It also needed a converter of ours, and the OpenAPI generator drops a type with a converter it does not know (the reference to it disappeared from the document), so the built-in `JsonStringEnumConverter` was the only kind that worked, and applied to every enum it broke Fido2's `COSE.Algorithm`, which WebAuthn needs as a number.
- A reader that refuses undefined numbers (a converter of ours): the same problem with the OpenAPI generator, and a payload attribute reports the field.
- A document served at runtime: nothing needs it there yet, and it publishes the API's map.

## Consequences

- A client sends `3` for `Ble`, not a name, so a request in a terminal or a log needs the table in `docs/database.md` or the enum's declaration to read.
- Reordering or renaming a C# enum member does not change the wire format, only changing its number does, and that is the database migration ADR 0012 already requires.
- Every action is documented as `200` for now. The real statuses (`201`, `204`, `404`, `401`, and `422` with `ValidationProblemDetails`) are not in the document, so the generated types do not describe errors. `[ProducesResponseType]` on the actions, or a convention that adds them, is the next step.
- The web sees the WebAuthn options and responses as `unknown`, so the passkey page casts once, where it hands them to the browser (`PublicKeyCredential.parseCreationOptionsFromJSON` and its siblings). TypeScript's own `PublicKeyCredentialCreationOptionsJSON` and the like are close to the library's shapes but not the same (members differ, and `challenge` and `response` are required in the DOM types and optional in the API's), so they were not mapped on.
- Forgetting to run `mise api:openapi` after a payload change goes unnoticed until the stale type fails somewhere.
- The attributes that validate payloads (`[Trimmed]`, `[KebabCase]`, `[HexColor]`, `[EnumDataType]`) are not in the document, so the generated types allow what the API then rejects with a `422`.
- Pagination is still open (see the consequences of [ADR 0026 (REST and JSON with controllers)](0026-rest-json-with-controllers.md)).
