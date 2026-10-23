#!/usr/bin/env bash
# cctokmon-hook.sh — Claude Code hook that records each session's status for TokenMojo
#
# Claude Code pipes the hook payload to stdin. We write the session's status to
# ~/.claude/cctokmon-sessions/<session_id>.status (atomically) for the tray app to read.
#
# Registered for SessionStart, UserPromptSubmit, PreToolUse, Notification, Stop and SessionEnd.
# (Run: install.sh  to register it automatically)
#
# SessionStart and UserPromptSubmit add stdout to Claude's context, so this never prints.
# It always exits 0, so a hook failure can never interrupt a session.

exec >/dev/null

SESSIONS_DIR="${CCTOKMON_SESSIONS_DIR:-$HOME/.claude/cctokmon-sessions}"

command -v jq >/dev/null 2>&1 || exit 0

input=$(cat)

# session_id becomes a filename, so accept only UUID-style characters. It is read
# first so the previous status can be passed into the single mapping call below.
sid=$(printf '%s' "$input" | jq -r '.session_id // empty' 2>/dev/null)
[[ "$sid" =~ ^[A-Za-z0-9-]+$ ]] || exit 0

status_file="$SESSIONS_DIR/$sid.status"
previous=""
[[ -f "$status_file" ]] && previous=$(cat "$status_file" 2>/dev/null)

# Maps the event to a status (nothing for events that change no status), keeping
# the turn start across the events inside one turn and the entry time while the
# status is unchanged.
mapped=$(printf '%s' "$input" | jq -r --arg now "$(date +%s)" --arg previous "$previous" '
    (try ($previous | fromjson) catch null) as $prev
    | (.hook_event_name // "") as $event
    | (if   $event == "SessionStart"     then "ready"
       elif $event == "UserPromptSubmit" then "working"
       elif $event == "PreToolUse"       then "working"
       elif $event == "Stop"             then "ready"
       elif $event == "SessionEnd"       then "ended"
       elif $event == "Notification" and ((.notification_type // "") as $type
            | ["permission_prompt", "elicitation_dialog", "elicitation_url_dialog", "agent_needs_input"]
            | index($type)) != null     then "needs_input"
       else empty end) as $status
    | ($now | tonumber) as $now
    | (if $event == "UserPromptSubmit" then $now
       elif $event == "SessionStart" or $event == "SessionEnd" then null
       else $prev.turn_started end) as $turn_started
    | (if $prev.status == $status and $event != "UserPromptSubmit" then $prev.since else $now end) as $since
    | {status: $status, since: $since, turn_started: $turn_started} | tojson
' 2>/dev/null)

[[ -n "$mapped" ]] || exit 0

mkdir -p "$SESSIONS_DIR" 2>/dev/null
tmp_file="$status_file.tmp"
printf '%s\n' "$mapped" > "$tmp_file" 2>/dev/null && mv "$tmp_file" "$status_file" 2>/dev/null

find "$SESSIONS_DIR" -name '*.status' -mtime +7 -delete 2>/dev/null

exit 0
