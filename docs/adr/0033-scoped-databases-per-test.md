# ADR 0033: A scoped database per test

- Status: Proposed
- Date: 2026-11-02

## Context

Tests insert rows and then delete them in a `finally` ([ADR 0032, constructor injection in tests](0032-constructor-injection-in-tests.md)). A test that forgets, or a suite that is killed, leaves rows that break the next run. Tests also share one database, so they cannot run in parallel, and a request still in flight when a test ends can write into the next test's data. End-to-end tests (Playwright) have the same problem, and more of it, because the browser and the web server make calls the test does not wait for.

[ADR 0013 (one server, template databases)](0013-single-postgres-server-with-template-databases.md) and [ADR 0014 (YAML seed layers)](0014-hierarchical-seed-layers-in-yaml.md) already give us the pieces: layered seeds and `CREATE DATABASE ... TEMPLATE`.

## Decision

- **Every test that touches the database gets its own database**, cloned from the template of the seed layer it names and dropped when the test's scope ends. Tests no longer track or reverse what they change.
- **Bake** is the verb for turning migrations and seeds into templates. It builds a schema template from the migrations, then for each layer, parent first, clones the schema template, applies the layer's whole chain, and saves the result as that layer's template. (Cloning the parent's template and applying only the layer's delta gives the same rows; it would need `UPDATE` and `DELETE` writers beside the `INSERT` one, so it is left as an optimization.) A template's name carries a hash of the migrations and the layer chain's source, so baking is a no-op when nothing changed and stale templates can be dropped. Templates are marked `ALLOW_CONNECTIONS false`. A clone that fails with error 55006 (the source is briefly in use) is retried.
- **Fixtures come from the baked template** (see [ADR 0035, typed values and relative times](0035-typed-seed-values-and-relative-times.md)), and a scope's clock is the template's anchor, so tests see the same time as the data.
- **The seed tool** is a .NET project, `JoshOlson.Database` (beside the `migrations/`, `provisioning/`, and `seeds/` it works on), with a library and a command-line entry point, and its own colocated tests. It is reachable three ways, all through the same library:
  - the test run: an assembly-level step bakes before the first test, and each test's scope asks the library for a clone;
  - `mise run`: `seeds:bake`, `seed <layer>` (destroy and recreate the dev database from that layer's template), `seed:test <id> <layer>` (stand up a scoped database by hand), `seed:clean`, `seeds:list`, and `seeds:migrate`, `seeds:accept`, and `seeds:check` ([ADR 0034, lock file and branch-safe migration](0034-seed-lock-file-and-branch-safe-migration.md));
  - Playwright's global setup, which runs `mise run seeds:bake`.
- **`seed:clean` drops every database with the tool's test prefix**, never the dev database, for the case where a run was killed before its scopes were disposed. The tool keeps its other safety checks from `seeding.md`: its own prefix, a `CREATEDB` role without superuser, and an allowlisted host.
- **A scope ID selects the database for a request.** A test scope has an opaque ID (the 42 in `seed:test 42 stress-test`). The test sends it in a header on every request. The API resolves it to a data source for that database. The ID is not tied to Postgres: anything per-instance (a Redis key prefix, if we add Redis) resolves from the same ID.
- **Production cannot honor the header.** The code that reads it and the registry that maps IDs to databases are registered only by a test composition, never by the production one, and the production host fails at startup if that composition is present. The header carries an ID, never a connection string, and an unknown ID is rejected.
- **Same path for both kinds of test.** API tests use the in-memory host and send the header like any client. Playwright sets it on the browser context, and the web server forwards it on its own calls to the API. The header, not a cookie, because it is easy to keep out of production and a cookie would reach the web server and the API alike.
- Background work in the API has no request and always uses the default database.

## Alternatives considered

- **Transactions rolled back at the end of each test.** Cheap, but the API opens its own connection per request ([ADR 0024](0024-scoped-open-connection-per-request.md)), commits inside services, and an end-to-end test spans several processes. A rollback cannot cover those.
- **Truncate and reseed between tests.** Slower than a file copy for larger seeds, and a missed table leaks state.
- **A container per test.** Heavier and gains no isolation we need ([ADR 0013](0013-single-postgres-server-with-template-databases.md)).
- **A cookie for the scope ID.** Reaches the web server and the API without a CORS preflight, but is harder to gate to test mode.

## Consequences

- Each test pays for a database clone. The dev compose file raises `max_connections` and turns off `fsync`, `synchronous_commit`, and `full_page_writes`; test data sources use a small pool and are disposed with the scope, with the pool cleared before the database is dropped (`DROP DATABASE ... WITH (FORCE)`).
- Tests can run in parallel, because each has its own database. A call still running after its test ends fails against a dropped database, not against the next test's data.
- The test role cannot create databases, so the tool uses a separate `CREATEDB` role. Provisioning ([ADR 0018, DML-only API role](0018-dml-only-api-role.md)) runs before the schema template is baked, so every clone carries the grants.
- Seeds for Playwright include a signed-in session, since the passkey ceremony is not driven from the browser.
- Dapper expands any `IEnumerable` parameter, so passing arrays of composite types needs a small `ICustomQueryParameter` wrapper that builds the `NpgsqlParameter` itself. That is separate from this decision and is recorded when it is built.
- Supersedes the "tests delete what they create" line in ADR 0032 (constructor injection in tests) once tests move to scoped databases.
