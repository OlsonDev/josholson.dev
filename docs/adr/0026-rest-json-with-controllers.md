# ADR 0026: REST and JSON with Controllers

- Status: Accepted
- Date: 2026-10-28

## Context

`web` calls `api` over the `compose` network to render pages, and browsers call `api` directly for data ([ADR 0027](0027-passkey-auth-and-direct-browser-to-api.md)). The API needs one way to expose its operations and one shape for its errors. A custom RPC library may replace the transport later, so the payload types and the layers behind the controllers should not depend on HTTP details.

## Decision

- **REST and JSON through MVC Controllers**, not minimal APIs: `[ApiController]`, attribute routing, async actions taking a `CancellationToken`, and no logic in the action. It calls a service and returns an `ActionResult`.
- **Dependencies.** The constructor takes what most actions use. A dependency one action needs is a `[FromServices]` parameter on that action.
- **Payloads** are hand-written request and response records per feature in `{Feature}.Payloads.cs` ([ADR 0023](0023-api-feature-folders-and-colocated-tests.md)). There is no mapping library, and a repository returns the response type directly until a query needs a row type.
- **Writes do not echo the data back.** Create returns `201 Created` with a `Location` header and no body. Update and delete return `204 No Content`, or `404` when the row does not exist. A body is returned only when the server produces something the client cannot compute.
- **Errors are `ProblemDetails`.** `PostgresExceptionHandler` maps unique and foreign key violations to `409` and check violations (including domain checks) to `400`, without the database message, which names constraints and values. Services and controllers have no try/catch for these.
- **Validation.** Database constraints are the backstop, not the user-facing validation: a failed check or unique index cannot name the field or say what was expected, so `PostgresExceptionHandler` only turns a violation that slips through into a generic `409` or `400`. User-facing validation is a `Validate{Operation}Async` method on the service, taking the same arguments as the operation. It reports every error, not just the first, and the controller calls it before the operation. A validation failure is `422 Unprocessable Content` with `ValidationProblemDetails`; a request that cannot be parsed or bound (bad JSON, wrong types) stays `400`. Common rules (required, length, pattern, range, enum membership) are DataAnnotations attributes on the payload properties, plus small attributes mirroring our database domains (`[Trimmed]`, `[KebabCase]`, `[HexColor]`). Only rules attributes cannot express (uniqueness, cross-field) are code. `PayloadValidator` runs the attributes with `Microsoft.Extensions.Validation`, which is experimental in .NET 10, so it is wrapped in that one class. It handles nested objects and list items, and reports every error keyed by JSON name (`bars[0].label`). Payload types are marked `[ValidatableType]`, and a type without it throws instead of passing silently. MVC's own attribute validation is turned off so the rules run once, and a request that cannot be parsed is still `400`.
- **OpenAPI.** When `web/` is created, the API publishes an OpenAPI document with the built-in `Microsoft.AspNetCore.OpenApi`, and `web/` generates its TypeScript types from it. A `mise` task regenerates them and a second task fails on drift, like `schema:doc` and `schema:check`. Nothing is built yet.

## Alternatives considered

- Minimal APIs: they suit prototypes, and Controllers give a place for filters, conventions, and one class per resource.
- Returning the full object from create and update: wasteful, because the client already holds what it sent, and it invites the response to drift from what is stored.

## Consequences

- FluentValidation (a validator class per payload, no OpenAPI schema from the rules) and Validly (its own attribute set instead of DataAnnotations, regex patterns with a backslash break its generated code, and list items and inherited properties were not validated in a spike) were considered and not adopted. If `Microsoft.Extensions.Validation` is removed or changed, only `PayloadValidator` and the `[ValidatableType]` markers are touched.

- Open: pagination (keyset against offset, tied to the stable `ORDER BY` rule), versioning (probably none yet), and the OpenAPI tasks, which wait for `web/`.
- A client that wants the stored object after a write makes a `GET` on the `Location` URL.
