#!/usr/bin/env bash
# UserPromptSubmit: when the prompt mentions committing, restate the commit workflow.
prompt=$(jq -r '.prompt // empty')
if printf '%s' "$prompt" | grep -Eiq '(^|[^a-z])commits?([^a-z]|$)'; then
  cat <<'MSG'
Commit workflow reminder (CLAUDE.md, "Commits"):
- Never run `git commit`. The user commits with a Token2 hardware key. Give a commit message in a fenced code block.
- Format, enforced by .githooks/commit-msg: 1-2 gitmoji (https://gitmoji.dev), one space, then at least 5 characters. No emojis after the first space. No leading or trailing whitespace.
- Wrap code-like text in backticks: file names, commands, identifiers, types, and literal values. The hook does not check this.
- Never add a Co-Authored-By trailer or any other attribution line.
- Only if the user explicitly tells you to make commits: create .claude/allow-commit to lift the block, commit, and delete the marker when done.
- Base the message on `git status` and `git diff --staged` (or `git diff` if nothing is staged).
- Validate before showing it: printf '%s' "<message>" > <scratchpad>/msg.txt && .githooks/commit-msg <scratchpad>/msg.txt (exit 0 means valid).
MSG
fi
exit 0
