# ADR 0018: DML-only API role

- Status: Accepted
- Date: 2026-10-21

## Context

Migrations run as the database owner. The API needs to read and write rows but never change structure, so a bug or SQL injection should not be able to drop or alter anything.

## Decision

The API connects as `josholson_api`, created by `Database/Provisioning/api_role.sql`. It has `CONNECT`, `USAGE` on every application schema, `SELECT, INSERT, UPDATE, DELETE` on tables, and `USAGE` on sequences. It owns nothing and cannot create objects. `ALTER DEFAULT PRIVILEGES` covers tables the owner creates later.

The script is a provisioning step, not a migration, because it needs a password. Run it as the owner, after migrations, with `psql -v api_password=... -f Database/Provisioning/api_role.sql`. It is idempotent and must be re-run when a migration adds a schema.

## Alternatives considered

- A migration: would put a secret in version control.
- Connecting as the owner: simplest, but gives the API DDL rights.

## Consequences

- Code that truncates or alters tables, such as the seed tool, connects as the owner.
- Default privileges only apply to objects created by the role that ran the script.
- In dev, `mise run db:provision` runs the script against the dev container, and the API tests connect as this role. A missing grant, such as access to a new schema, fails a test instead of production. `ApiRoleTests` asserts the role can change rows but not create, alter, drop, or truncate anything.
