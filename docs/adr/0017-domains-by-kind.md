# ADR 0017: Domains for repeated validation, one schema per kind

- Status: Accepted
- Date: 2026-10-21

## Context

The same rules were repeated as column checks: a slug regex copied into every table with a slug, non-empty and trimmed checks on most text columns, hash length checks, and loose email and URL checks. Free-form `TEXT` also allowed `''` in nullable columns, so "absent" had two representations.

## Decision

Repeated validation lives in domains, in a schema named for the kind of value ([ADR 0007](0007-year-optional-month-domain.md) set the precedent). Every `TEXT` column uses the trimmed-text domain unless a stricter domain or its own check applies, and arrays use its array form. A domain check passes `NULL`, so nullability stays on the column. Domain checks apply to array elements but not to `NULL` elements, so array columns also carry a `_no_nulls` check.

The current set of domains is listed in [sql.md](../../.claude/rules/sql.md#domains), the maintained reference.

## Alternatives considered

- Column checks everywhere: the status quo, dozens of near-identical lines.
- One `types` schema: says nothing about its contents.
- A schema named after a type keyword such as `json`: `JSON` is a keyword since Postgres 16 and does not parse unquoted as a schema-qualified type.

## Consequences

- The API must trim input before inserting; the database rejects untrimmed text instead of fixing it.
- The URL domain is a safety net, not a parser. It rejects `javascript:` URLs but not every malformed one; the API still validates with `Uri.TryCreate`.
- Changing a domain re-validates every column that uses it.
- A schema name can also be a type name (`text`); schemas are only ever qualified in type position.
