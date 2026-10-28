# ADR 0024: One open connection per request

- Status: Accepted
- Date: 2026-10-28

## Context

Repositories first opened a connection from `NpgsqlDataSource` on every call. That repeats open and dispose code in each method, and two repository calls can never share a transaction, because each runs on its own connection in autocommit mode.

## Decision

- `AddDatabase` registers a scoped `NpgsqlConnection`, opened when first resolved. Repositories take it in their constructor and neither open nor dispose it. The container disposes it at the end of the request.
- A service that needs several statements to be atomic calls `BeginTransactionAsync` on the connection, and `CommitAsync` when done. Disposing the transaction without committing rolls it back. Repositories using the same connection join the open transaction with no extra plumbing (covered by `TagRepository.Tests.cs`).
- The connection string gets `Minimum Pool Size=10`. The site is low traffic, so the default pool would prune idle connections, and a team of people opening the site together would otherwise pay for cold connects.
- Postgres takes no row locks for a plain `SELECT`, even in a transaction. To keep data stable between a read and a write, use `SELECT ... FOR UPDATE`, a stricter isolation level, or put the condition in the write itself and check the affected row count.

## Alternatives considered

- **Open per call from `NpgsqlDataSource`**: shortest hold on a connection, and parallel queries work, but it needs boilerplate everywhere and has no transaction story.
- **Scoped but unopened**: Dapper opens and closes per call, so it behaves like the previous option with less code, and a transaction needs an explicit open.
- **An async wrapper that starts opening in its constructor**, to overlap the open with model binding: not needed while the pool is warm. Revisit if measurements show it matters.

## Consequences

- One connection cannot run two commands at once, so `Task.WhenAll` over repository calls in one request fails. Parallel work needs its own connection.
- The connection is opened when anything depends on it, even if the action never queries.
- `AddScoped` opens synchronously, which blocks the thread for the duration of a pool hit. That is microseconds while the pool is warm.
- Code outside a request (a background job) has to create a scope to get a connection.
- Each `WebApplicationFactory` in the tests warms its own pool of 10, so the dev server's `max_connections` must allow a few dozen.
