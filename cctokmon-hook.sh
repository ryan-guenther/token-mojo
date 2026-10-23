#!/usr/bin/env bash
# cctokmon-hook.sh — Claude Code hook that logs session events for TokenMojo
#
# Claude Code pipes the hook payload to stdin. We append one line per event to
# ~/.claude/cctokmon-sessions/<session_id>.events, and the tray app works out each
# session's status from that log. Appending needs no lock and never reads earlier
# state, so overlapping hooks cannot lose each other's events.
#
# Registered for SessionStart, UserPromptSubmit, PreToolUse, PostToolUse, PostToolUseFailure,
# Notification, SubagentStart, SubagentStop, Stop and SessionEnd. (Run: install.sh  to register it automatically)
#
# SessionStart and UserPromptSubmit add stdout to Claude's context, so this never prints.
# It always exits 0, so a hook failure can never interrupt a session.

# Bash 4+ is needed below (mapfile). Checked first, in syntax that bash 3.2 (stock macOS) can still parse.
if [ "${BASH_VERSINFO[0]:-0}" -lt 4 ]; then
    echo "cctokmon-hook: bash 4+ required, found ${BASH_VERSION:-unknown}; event not logged" >&2
    exit 0
fi

exec >/dev/null

SESSIONS_DIR="${CCTOKMON_SESSIONS_DIR:-$HOME/.claude/cctokmon-sessions}"

command -v jq >/dev/null 2>&1 || exit 0

input=$(cat)

# One jq call prints the session id, then the event line (nothing for events we do not log).
# Each optional field is cut to 100 characters, keeping a line far below the 4 KB that one
# append writes atomically.
mapfile -t parsed < <(printf '%s' "$input" | jq -r --arg now "$(date +%s)" '
    (.hook_event_name // "") as $event
    | select(["SessionStart", "UserPromptSubmit", "PreToolUse", "PostToolUse", "PostToolUseFailure", "Notification", "SubagentStart", "SubagentStop", "Stop", "SessionEnd"]
        | index($event))
    | def field($key; $name): if (.[$key] // "") != "" then {($name): (.[$key] | tostring | .[0:100])} else {} end;
    (.session_id // "" | tostring),
    ({t: ($now | tonumber), e: $event}
        + field("agent_id"; "a") + field("notification_type"; "n") + field("source"; "s") | tojson)
' 2>/dev/null)

# A native jq.exe under Git Bash ends its lines with CRLF, which mapfile -t leaves as a trailing CR
parsed=("${parsed[@]%$'\r'}")

# Exactly two lines: a session_id with a newline in it would add more.
[[ ${#parsed[@]} -eq 2 ]] || exit 0

# session_id becomes a filename, so accept only UUID-style characters
[[ "${parsed[0]}" =~ ^[A-Za-z0-9-]+$ ]] || exit 0

mkdir -p "$SESSIONS_DIR" 2>/dev/null
printf '%s\n' "${parsed[1]}" >> "$SESSIONS_DIR/${parsed[0]}.events" 2>/dev/null

find "$SESSIONS_DIR" -name '*.events' -mtime +7 -delete 2>/dev/null

exit 0
