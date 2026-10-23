#!/usr/bin/env bash
# cctokmon uninstaller

set -euo pipefail

BIN_DIR="$HOME/.local/bin"
CLAUDE_DIR="$HOME/.claude"
SETTINGS_FILE="$CLAUDE_DIR/settings.json"
STATUSLINE_DEST="$CLAUDE_DIR/cctokmon-statusline.sh"
HOOK_DEST="$CLAUDE_DIR/cctokmon-hook.sh"
HOOK_EVENTS='["SessionStart","UserPromptSubmit","PreToolUse","PostToolUse","Notification","Stop","SessionEnd"]'
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

# Remove symlink, or the launcher install.sh writes on Windows
if [[ -L "${BIN_DIR}/cctokmon" ]] || grep -qx '# cctokmon launcher' "${BIN_DIR}/cctokmon" 2>/dev/null; then
    rm "${BIN_DIR}/cctokmon"
    ok "Removed: ${BIN_DIR}/cctokmon"
fi

# Remove statusline script
if [[ -f "$STATUSLINE_DEST" ]]; then
    rm "$STATUSLINE_DEST"
    ok "Removed: ${STATUSLINE_DEST}"
fi

# Remove session status hook script
if [[ -f "$HOOK_DEST" ]]; then
    rm "$HOOK_DEST"
    ok "Removed: ${HOOK_DEST}"
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

# Remove our hook entries from settings.json, keeping everyone else's
if [[ -f "$SETTINGS_FILE" ]] && command -v jq >/dev/null 2>&1 \
    && jq -e --arg cmd "$HOOK_DEST" '[.hooks[]?[]?.hooks[]?.command] | index($cmd)' "$SETTINGS_FILE" >/dev/null 2>&1; then
    local_tmp=$(mktemp)
    jq --arg cmd "$HOOK_DEST" --argjson events "$HOOK_EVENTS" '
        reduce $events[] as $e (.;
            if .hooks[$e] == null then .
            else .hooks[$e] |= [.[]
                | (any(.hooks[]?; .command == $cmd)) as $ours
                | if $ours then .hooks |= map(select(.command != $cmd)) else . end
                | select(($ours | not) or (.hooks | length) > 0)]
            end
            | if .hooks[$e] == [] then del(.hooks[$e]) else . end)
        | if .hooks == {} then del(.hooks) else . end
    ' "$SETTINGS_FILE" > "$local_tmp"
    mv "$local_tmp" "$SETTINGS_FILE"
    ok "Removed session status hooks from ${SETTINGS_FILE}"
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
