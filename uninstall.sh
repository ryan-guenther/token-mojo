#!/usr/bin/env bash
# cctokmon uninstaller

set -euo pipefail

BIN_DIR="$HOME/.local/bin"
CLAUDE_DIR="$HOME/.claude"
SETTINGS_FILE="$CLAUDE_DIR/settings.json"
STATUSLINE_DEST="$CLAUDE_DIR/cctokmon-statusline.sh"
HOOK_DEST="$CLAUDE_DIR/cctokmon-hook.sh"
# Quoted inside the command string, since Claude Code runs it through a shell and HOME may contain spaces
HOOK_COMMAND="\"$HOOK_DEST\""
HOOK_EVENTS='["SessionStart","UserPromptSubmit","PreToolUse","PostToolUse","PostToolUseFailure","Notification","Stop","SessionEnd"]'
WORKTREE_INIT_DEST="$CLAUDE_DIR/cctokmon-worktree-init.sh"
WORKTREE_INIT_COMMAND="\"$WORKTREE_INIT_DEST\""
STATE_FILE="$CLAUDE_DIR/cctokmon-state.json"
SESSIONS_DIR="$CLAUDE_DIR/cctokmon-sessions"

if tput colors &>/dev/null 2>&1 && [[ "$(tput colors)" -ge 8 ]]; then
    R=$(tput sgr0); BOLD=$(tput bold)
    GREEN=$(tput setaf 2); YELLOW=$(tput setaf 3)
else
    R=""; BOLD=""; GREEN=""; YELLOW=""
fi

ok()   { printf '%s✓%s %s\n' "${GREEN}" "${R}" "$*"; }
warn() { printf '%s⚠%s  %s\n' "${YELLOW}" "${R}" "$*"; }

printf '\n%scctokmon uninstaller%s\n%s\n\n' "${BOLD}" "${R}" "────────────────────"

# Remove our hook entries from settings.json first, keeping everyone else's. A settings file jq cannot
# process stops the uninstall here, with the file and the hook script both left as they are. Without
# jq the entries cannot be checked, so the script stays too: they must never point at a missing file.
keep_hook_script=0
if [[ -f "$SETTINGS_FILE" ]] && ! command -v jq >/dev/null 2>&1; then
    warn "jq not found, so ${SETTINGS_FILE} was not checked for cctokmon hooks; keeping ${HOOK_DEST} and ${WORKTREE_INIT_DEST}."
    printf '  Remove their entries from %s by hand, then delete the scripts.\n' "$SETTINGS_FILE"
    keep_hook_script=1
fi
if [[ -f "$SETTINGS_FILE" ]] && command -v jq >/dev/null 2>&1; then
    jq_error=$(mktemp)
    if ! has_hooks=$(jq -r --arg cmd "$HOOK_COMMAND" --arg init "$WORKTREE_INIT_COMMAND" '
        [.hooks[]?[]?.hooks[]?.command] | (index($cmd) != null) or (index($init) != null)
    ' "$SETTINGS_FILE" 2>"$jq_error"); then
        printf 'Cannot process %s, so nothing was removed:\n' "$SETTINGS_FILE" >&2
        cat "$jq_error" >&2
        rm -f "$jq_error"
        exit 1
    fi
    # A native jq.exe under Git Bash ends its lines with CRLF
    has_hooks=${has_hooks%$'\r'}
    if [[ "$has_hooks" == "true" ]]; then
        local_tmp=$(mktemp)
        if ! jq --arg cmd "$HOOK_COMMAND" --arg init "$WORKTREE_INIT_COMMAND" --argjson events "$HOOK_EVENTS" '
            def ours_cmd: . == $cmd or . == $init;
            reduce $events[] as $e (.;
                if .hooks[$e] == null then .
                else .hooks[$e] |= [.[]
                    | (any(.hooks[]?; .command | ours_cmd)) as $ours
                    | if $ours then .hooks |= map(select(.command | ours_cmd | not)) else . end
                    | select(($ours | not) or (.hooks | length) > 0)]
                end
                | if .hooks[$e] == [] then del(.hooks[$e]) else . end)
            | if .hooks == {} then del(.hooks) else . end
        ' "$SETTINGS_FILE" > "$local_tmp" 2>"$jq_error"; then
            printf 'Cannot process %s, so nothing was removed:\n' "$SETTINGS_FILE" >&2
            cat "$jq_error" >&2
            rm -f "$jq_error" "$local_tmp"
            exit 1
        fi
        mv "$local_tmp" "$SETTINGS_FILE"
        ok "Removed session status hooks from ${SETTINGS_FILE}"
    fi
    rm -f "$jq_error"
fi

# Remove symlink, or the launcher install.sh writes on Windows
if [[ -L "${BIN_DIR}/cctokmon" ]] || grep -qx '# cctokmon launcher' "${BIN_DIR}/cctokmon" 2>/dev/null; then
    rm "${BIN_DIR}/cctokmon"
    ok "Removed: ${BIN_DIR}/cctokmon"
fi

# Remove the tm symlink or launcher, unless that name belongs to something else
tm_path="${BIN_DIR}/tm"
if [[ -L "$tm_path" ]]; then
    if [[ "$(readlink "$tm_path")" == */cctokmon-tm.sh ]]; then
        rm "$tm_path"
        ok "Removed: ${tm_path}"
    fi
elif grep -qx '# tm launcher' "$tm_path" 2>/dev/null; then
    rm "$tm_path"
    ok "Removed: ${tm_path}"
fi

# Remove statusline script
if [[ -f "$STATUSLINE_DEST" ]]; then
    rm "$STATUSLINE_DEST"
    ok "Removed: ${STATUSLINE_DEST}"
fi

# Remove session status hook script
if [[ -f "$HOOK_DEST" && "$keep_hook_script" -eq 0 ]]; then
    rm "$HOOK_DEST"
    ok "Removed: ${HOOK_DEST}"
fi

# Remove worktree submodule hook script
if [[ -f "$WORKTREE_INIT_DEST" && "$keep_hook_script" -eq 0 ]]; then
    rm "$WORKTREE_INIT_DEST"
    ok "Removed: ${WORKTREE_INIT_DEST}"
fi

# Remove state file
if [[ -f "$STATE_FILE" ]]; then
    rm "$STATE_FILE"
    ok "Removed: ${STATE_FILE}"
fi

# Remove per-session state written for the menu bar app
if [[ -d "$SESSIONS_DIR" ]]; then
    rm -rf "$SESSIONS_DIR"
    ok "Removed: ${SESSIONS_DIR}"
fi

# Remove statusLine from settings.json
if [[ -f "$SETTINGS_FILE" ]] && command -v jq >/dev/null 2>&1; then
    sl=$(jq -r '(.statusLine | if type == "object" then .command else . end) // empty' "$SETTINGS_FILE" 2>/dev/null || true)
    if [[ "$sl" == *"cctokmon"* ]]; then
        local_tmp=$(mktemp)
        jq 'del(.statusLine)' "$SETTINGS_FILE" > "$local_tmp"
        mv "$local_tmp" "$SETTINGS_FILE"
        ok "Removed statusLine from ${SETTINGS_FILE}"
    fi
fi

# Ask about config
if [[ -d "$HOME/.config/cctokmon" ]]; then
    printf 'Remove config dir ~/.config/cctokmon? [y/N] '
    read -r ans
    if [[ "${ans,,}" == "y" ]]; then
        rm -rf "$HOME/.config/cctokmon"
        ok "Removed: ~/.config/cctokmon"
    else
        warn "Kept: ~/.config/cctokmon"
    fi
fi

printf '\n%sDone.%s\n\n' "${BOLD}${GREEN}" "${R}"
