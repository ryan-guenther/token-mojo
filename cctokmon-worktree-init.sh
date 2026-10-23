#!/usr/bin/env bash
# cctokmon-worktree-init.sh — Claude Code hook that initialises submodules in a new worktree
#
# `claude -w NAME` makes <repo>/.claude/worktrees/NAME, and git leaves its submodules empty.
# On SessionStart, when the session starts in such a worktree whose submodules are not
# initialised, this runs `git submodule update --init --recursive` there. It can take a while,
# so install.sh registers it with a long timeout and apart from cctokmon-hook.sh, which must
# stay a fast append.
#
# Registered for SessionStart with the matcher "startup" only. (Run: install.sh  to register it)
#
# SessionStart stdout is added to Claude's context, so everything this prints goes to stderr.
# It always exits 0, so a failure can never stop a session from starting.

exec >&2

command -v jq >/dev/null 2>&1 || exit 0
command -v git >/dev/null 2>&1 || exit 0

input=$(cat)

cwd=$(printf '%s' "$input" | jq -r '.cwd // "" | tostring' 2>/dev/null)
# A native jq.exe under Git Bash ends its lines with CRLF, and Claude Code there reports Windows paths
cwd=${cwd%$'\r'}
cwd=${cwd//\\//}

[[ "$cwd" == *"/.claude/worktrees/"* ]] || exit 0
[[ -f "$cwd/.gitmodules" ]] || exit 0

# An uninitialised submodule is listed with a leading "-"
status=$(git -C "$cwd" submodule status 2>/dev/null) || exit 0
grep -q '^-' <<<"$status" || exit 0

printf 'cctokmon: initialising submodules in %s\n' "$cwd"
# A credential prompt would hang until the hook times out, and nothing can answer it
if ! GIT_TERMINAL_PROMPT=0 git -C "$cwd" submodule update --init --recursive; then
    printf 'cctokmon: submodule update failed in %s\n' "$cwd"
fi

exit 0
