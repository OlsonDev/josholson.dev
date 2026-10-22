# ADR 0001: Gitmoji commit messages enforced by a hook

- Status: Accepted
- Date: 2026-10-21

## Context

We want a history that reads at a glance: each commit says what kind of change it is.

## Decision

Commit subjects start with 1-2 emojis ([gitmoji](https://gitmoji.dev)), one space, then a message of at least 5 characters. No emojis in the message, and no leading or trailing whitespace. `.githooks/commit-msg` enforces it. It is a .NET file-based app (`dotnet run`), with unit tests in `.githooks/commit-msg.tests.cs`. Each clone enables it with `dotnet run setup.cs`, which sets `core.hooksPath`.

## Alternatives considered

- Convention only: nothing stops a slip.
- Conventional Commits (`feat:`, `fix:`): not the style we want.
- A Node-based linter such as commitlint: needs its own configuration, and the validator is simple enough to keep in the stack's own language.

## Consequences

- Every commit runs a .NET app, which takes a few seconds. The first run also downloads the `EmojiToolkit` package.
- The hook checks that the emojis are real emojis, not that they come from the gitmoji list. Merge commits are skipped.