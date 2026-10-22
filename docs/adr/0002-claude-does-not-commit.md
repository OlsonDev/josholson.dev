# ADR 0002: Claude does not commit

- Status: Accepted
- Date: 2026-10-21

## Context

Commits are signed with a Token2 hardware key (SSH signing, `commit.gpgsign=true`) that needs the user's touch, and the `commit-msg` hook ([format](../../.githooks/commit-msg.cs)) rejects messages that are not gitmoji-prefixed. The user wants to ask Claude for a commit message and make the commit themselves.

## Decision

Claude never runs `git commit`. Two project hooks in `.claude/settings.json` enforce this so it does not depend on remembering:

- `PreToolUse` on `Bash` runs `.claude/hooks/block-git-commit.sh`. It blocks any `git ... commit` command (exit 2) and tells Claude to give a message instead. The hook fails closed (`onFailure: block`).
- `UserPromptSubmit` runs `.claude/hooks/commit-message-reminder.sh`. When a prompt mentions commit or commits, it injects the workflow and the message format.

The block is lifted while the untracked marker file `.claude/allow-commit` exists (it is listed in `.gitignore`). Claude creates it only when the user explicitly asks for commits, for example to turn a day of work into a multi-commit history, and deletes it when done. The user still touches the hardware key for every signature.

Claude validates a message before showing it by running `.githooks/commit-msg` on a scratch file. Messages never carry a `Co-Authored-By` trailer or any other attribution line, whatever a tool's default instructions say.

## Alternatives considered

- A `permissions.deny` rule for `Bash(git commit:*)`: simpler, but it gives Claude no instructions about what to do instead.
- Prose in CLAUDE.md only: can be forgotten mid-session.

## Consequences

- The block is a text match, so `echo "git commit"` is blocked too; rephrase the command.
- It does not stop `git commit-tree` or other ways of writing commits; it guards against the ordinary slip, not a determined bypass.
- The hooks require `jq`.
- While the marker exists the guard is off. If a session ends before the marker is deleted, remove it by hand.
- Claude Code reads `.claude/settings.json` hooks at session start; after editing them, open `/hooks` or restart the session.
