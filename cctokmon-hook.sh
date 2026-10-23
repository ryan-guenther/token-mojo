#!/usr/bin/env bash
# cctokmon-hook.sh — Claude Code hook that records each session's status for TokenMojo
#
# Claude Code pipes the hook payload to stdin. We write the session's status to
# ~/.claude/cctokmon-sessions/<session_id>.status (atomically) for the tray app to read.
#
# Registered for SessionStart, UserPromptSubmit, PreToolUse, PostToolUse, Notification,
# Stop and SessionEnd. (Run: install.sh  to register it automatically)
#
# SessionStart and UserPromptSubmit add stdout to Claude's context, so this never prints.
# It always exits 0, so a hook failure can never interrupt a session.

exec >/dev/null

SESSIONS_DIR="${CCTOKMON_SESSIONS_DIR:-$HOME/.claude/cctokmon-sessions}"
LOCK_WAIT_TRIES=20      # 0.1s apart: about 2s
LOCK_STALE_SECONDS=10

lock_dir=""
tmp_file=""

release() {
    [[ -n "$tmp_file" ]] && rm -f "$tmp_file" 2>/dev/null
    [[ -n "$lock_dir" ]] && rmdir "$lock_dir" 2>/dev/null
    return 0
}

# GNU stat first (Linux, Git Bash), then BSD stat (macOS). GNU stat -f prints
# filesystem details instead of failing, so only a plain number is accepted.
mtime_of() {
    local seconds
    seconds=$(stat -c %Y "$1" 2>/dev/null) || seconds=$(stat -f %m "$1" 2>/dev/null)
    [[ "$seconds" =~ ^[0-9]+$ ]] && printf '%s' "$seconds"
}

# Hook events for one session can overlap (PreToolUse of parallel tools), so the
# read-modify-write is serialised with mkdir, which is atomic on every platform.
# Sets lock_dir only once the lock is held.
take_lock() {
    local dir="$1" tries=0 mtime
    while ! mkdir "$dir" 2>/dev/null; do
        mtime=$(mtime_of "$dir")
        if [[ -n "$mtime" ]] && (( $(date +%s) - mtime > LOCK_STALE_SECONDS )); then
            rmdir "$dir" 2>/dev/null
        fi
        (( ++tries > LOCK_WAIT_TRIES )) && return 1
        sleep 0.1 2>/dev/null || sleep 1
    done
    lock_dir="$dir"
}

trap release EXIT
trap 'exit 0' HUP INT TERM

command -v jq >/dev/null 2>&1 || exit 0

input=$(cat)

# session_id becomes a filename, so accept only UUID-style characters. It is read
# first so the lock and previous status can be taken before the single mapping call.
sid=$(printf '%s' "$input" | jq -r '.session_id // empty' 2>/dev/null)
[[ "$sid" =~ ^[A-Za-z0-9-]+$ ]] || exit 0

mkdir -p "$SESSIONS_DIR" 2>/dev/null
status_file="$SESSIONS_DIR/$sid.status"
take_lock "$status_file.lock" || exit 0

previous=""
[[ -f "$status_file" ]] && previous=$(cat "$status_file" 2>/dev/null)

# Maps the event to a status (nothing for events that change no status), keeping
# the turn start across the events inside one turn and the entry time while the
# status is unchanged. A previous file that is not a JSON object counts as none.
# Subagent events (they carry an agent_id) never move a session out of needs_input,
# since the main agent is still waiting on the user.
mapped=$(printf '%s' "$input" | jq -r --arg now "$(date +%s)" --arg previous "$previous" '
    (try ($previous | fromjson) catch null) as $parsed
    | (if ($parsed | type) == "object" then $parsed else null end) as $prev
    | (.hook_event_name // "") as $event
    | (.agent_id // "" | tostring) as $agent
    | (if   $event == "SessionStart" and (.source // "") == "compact" then empty
       elif $event == "SessionStart"     then "ready"
       elif $event == "UserPromptSubmit" then "working"
       elif $event == "PreToolUse"       then "working"
       elif $event == "PostToolUse"      then "working"
       elif $event == "Stop"             then "ready"
       elif $event == "SessionEnd"       then "ended"
       elif $event == "Notification" and ((.notification_type // "") as $type
            | ["permission_prompt", "elicitation_dialog", "elicitation_url_dialog", "agent_needs_input"]
            | index($type)) != null     then "needs_input"
       else empty end) as $status
    | select($agent == "" or $prev.status != "needs_input")
    | ($now | tonumber) as $now
    | (if $event == "UserPromptSubmit" then $now
       elif $event == "SessionStart" or $event == "SessionEnd" then null
       else $prev.turn_started end) as $turn_started
    | (if $prev.status == $status and $event != "UserPromptSubmit" then $prev.since else $now end) as $since
    | {status: $status, since: $since, turn_started: $turn_started} | tojson
' 2>/dev/null)

if [[ -n "$mapped" ]]; then
    tmp_file="$status_file.$$.tmp"
    printf '%s\n' "$mapped" > "$tmp_file" 2>/dev/null && mv "$tmp_file" "$status_file" 2>/dev/null
fi

release
trap - EXIT

find "$SESSIONS_DIR" \( -name '*.status' -o -name '*.status.*.tmp' -o -name '*.status.lock' \) -mtime +7 -delete 2>/dev/null

exit 0
