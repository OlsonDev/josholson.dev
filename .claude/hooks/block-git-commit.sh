#!/usr/bin/env bash
# PreToolUse (Bash): the user makes every commit themselves, signed with a hardware key.
# While the untracked marker file .claude/allow-commit exists, commits are allowed (see ADR 0002).
[ -e "${CLAUDE_PROJECT_DIR:-.}/.claude/allow-commit" ] && exit 0
command=$(jq -r '.tool_input.command // empty')
pattern='(^|[;&|(]|[[:space:]])git([[:space:]]+(-C[[:space:]]+[^[:space:]]+|-c[[:space:]]+[^[:space:]]+|--[a-z-]+(=[^[:space:]]*)?))*[[:space:]]+commit([[:space:]]|$)'
if printf '%s' "$command" | grep -Eq "$pattern"; then
  cat >&2 <<'MSG'
Blocked: do not run `git commit`. The user commits themselves; commits are signed with a Token2 hardware key that needs their touch.
Give the user a commit message instead (see CLAUDE.md, "Commits"):
- 1-2 gitmoji (https://gitmoji.dev), one space, then at least 5 characters
- no emojis after the first space, no leading or trailing whitespace
- never add a Co-Authored-By trailer or any other attribution line
- only if the user explicitly told you to make commits: create the marker .claude/allow-commit, commit, and delete the marker as soon as you are done
- validate it first: printf '%s' "<message>" > <scratchpad>/msg.txt && .githooks/commit-msg <scratchpad>/msg.txt (exit 0 means valid)
MSG
  exit 2
fi
exit 0