# ADR 0013: One Postgres server, template databases

- Status: Accepted
- Date: 2026-10-21

## Context

Seeding and tests need many databases quickly, in any developer's environment.

## Decision

Use the single compose Postgres server and create databases on it. Cache a template database per schema version and per seed layer, and clone with `CREATE DATABASE ... TEMPLATE`. The seed tool talks to Postgres only, over the published loopback port.

## Alternatives considered

- A container per seed: slower, heavier, and gains nothing for isolation we need.

## Consequences

- A template must have no active connections, and the role needs `CREATEDB`.
- On Postgres 15 and later, check the `STRATEGY` option for larger templates.
- Extensions are copied with the template, so install them first.
- Many parallel test databases can hit `max_connections`; the dev compose file raises it.
