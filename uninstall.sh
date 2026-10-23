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

# Path of the real file behind PATH, following symlinks without GNU readlink -f (stock macOS lacks it)
resolve_symlinks() {
    local path="$1" link hops=0
    while [[ -L "$path" ]]; do
        (( ++hops <= 40 )) || { printf 'Too many symbolic links resolving %s\n' "$1" >&2; return 1; }
        link=$(readlink "$path")
        case "$link" in
            /*) path="$link" ;;
            *) path="$(dirname "$path")/$link" ;;
        esac
    done
    printf '%s' "$path"
}

# Rewrites settings.json with jq, passing it the given arguments. The result is built in a temp file next to
# the real file (so the final mv is a same-filesystem rename), cp -p gives it the original's mode, and a
# symlinked settings.json is written through, so the symlink stays. On any failure the original is untouched
# and the temp file is removed.
SETTINGS_TMP=""
trap '[[ -z "$SETTINGS_TMP" ]] || rm -f "$SETTINGS_TMP"' EXIT
write_settings() {
    local target
    target=$(resolve_symlinks "$SETTINGS_FILE") || return 1
    SETTINGS_TMP=$(mktemp "$(dirname "$target")/.settings.json.XXXXXX") || return 1
    if cp -p "$target" "$SETTINGS_TMP" && jq "$@" "$target" > "$SETTINGS_TMP" && mv "$SETTINGS_TMP" "$target"; then
        SETTINGS_TMP=""
        return 0
    fi
    rm -f "$SETTINGS_TMP"
    SETTINGS_TMP=""
    return 1
}

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
        # shellcheck disable=SC2016 # the single-quoted argument is a jq program
        if ! write_settings --arg cmd "$HOOK_COMMAND" --arg init "$WORKTREE_INIT_COMMAND" --argjson events "$HOOK_EVENTS" '
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
        ' 2>"$jq_error"; then
            printf 'Cannot process %s, so nothing was removed:\n' "$SETTINGS_FILE" >&2
            cat "$jq_error" >&2
            rm -f "$jq_error"
            exit 1
        fi
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
    # A native jq.exe under Git Bash ends its lines with CRLF
    sl=${sl%$'\r'}
    if [[ "$sl" == *"cctokmon"* ]]; then
        write_settings 'del(.statusLine)' ||
            { printf 'Cannot update %s; it was left unchanged.\n' "$SETTINGS_FILE" >&2; exit 1; }
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
