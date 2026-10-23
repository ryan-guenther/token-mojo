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

# Bash 4+ is needed below. Checked first, in syntax that bash 3.2 (stock macOS) can still parse.
if [ "${BASH_VERSINFO[0]:-0}" -lt 4 ]; then
    echo "cctokmon-worktree-init: bash 4+ required, found ${BASH_VERSION:-unknown}; submodules not initialised" >&2
    exit 0
fi

exec >&2

# winget installs jq into its links dir, which a Claude Code started before the install
# does not have on PATH yet
if ! command -v jq >/dev/null 2>&1 && [[ -n "${LOCALAPPDATA:-}" ]] && command -v cygpath >/dev/null 2>&1; then
    PATH="$PATH:$(cygpath -u "$LOCALAPPDATA")/Microsoft/WinGet/Links"
fi

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
# Nothing can answer a prompt, so one would hang until the hook times out. git's own and the credential
# manager's are switched off below. ssh's (host key or password) is switched off only for OpenSSH, and
# by adding to the command git would run anyway, found in git's own order: GIT_SSH_COMMAND, core.sshCommand,
# GIT_SSH, then ssh. Another program (plink, a wrapper) may not take the option, so it is left as it is.
ssh_settings=()
ssh_command=${GIT_SSH_COMMAND:-}
[[ -n "$ssh_command" ]] || ssh_command=$(git -C "$cwd" config core.sshCommand 2>/dev/null)
if [[ -n "$ssh_command" || -z "${GIT_SSH:-}" ]]; then
    : "${ssh_command:=ssh}"
    ssh_variant=${GIT_SSH_VARIANT:-$(git -C "$cwd" config ssh.variant 2>/dev/null)}
    ssh_program=${ssh_command%% *}
    ssh_program=${ssh_program##*[/\\]}
    if [[ "$ssh_variant" =~ ^(|ssh|auto)$ && "$ssh_program" =~ ^ssh(\.exe)?$ ]]; then
        ssh_settings=(GIT_SSH_COMMAND="$ssh_command -o BatchMode=yes")
    fi
fi
if ! env GIT_TERMINAL_PROMPT=0 GCM_INTERACTIVE=never "${ssh_settings[@]}" \
    git -C "$cwd" submodule update --init --recursive </dev/null; then
    printf 'cctokmon: submodule update failed in %s\n' "$cwd"
fi

exit 0
