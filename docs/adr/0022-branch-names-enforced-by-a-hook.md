# ADR 0022: Branch names enforced by a hook

- Status: Accepted
- Date: 2026-10-22

## Context

Branch names are the first thing a pull request or `git branch` listing shows. We want the kind of work visible from the name, the same way [ADR 0001](0001-gitmoji-commit-messages.md) does for commits.

## Decision

A branch is `main`, or a prefix with a trailing slash and a name: `mob/`, `feature/`, `fix/`, `hotfix/`, `docs/`, or `test/`. The name starts with a letter and has only letters, digits, and hyphens (`feature/foo-bar`). `.githooks/pre-commit` enforces it, as a .NET file-based app like `commit-msg`, with unit tests in `.githooks/pre-commit.tests.cs`. The hook skips validation while a rebase is in progress.

## Alternatives considered

- Convention only: nothing stops a slip.
- Checking in `commit-msg` instead: it runs later in the commit, and `pre-commit` is where git expects checks of the working state.

## Consequences

- Committing on a detached `HEAD` is rejected.
- `main` is allowed for now. Protecting it, so commits only arrive through branches, is a later change.
- Every commit runs a second .NET app.
