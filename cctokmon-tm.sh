#!/usr/bin/env bash
# cctokmon-tm.sh — start or resume a Claude Code session in this terminal tab and tag the tab
#
# The tab title becomes "TM <name> <id8>", where id8 is the first 8 characters of the session id.
# TokenMojo finds a session's window by that tag, so a click on its row can bring the window forward.
# Claude Code normally rewrites the terminal title; tm turns that off (CLAUDE_CODE_DISABLE_TERMINAL_TITLE),
# which also turns off Claude's own title updates and terminal status reports in this session.
#
# Run: tm --help

set -euo pipefail

usage() {
    cat <<'EOF'
Usage:
  tm [--wt NAME] [--prompt-file PATH] [--] [prompt...]
  tm --resume ID

Starts Claude Code in this terminal tab, tagging the tab title so TokenMojo can find it.

Options:
  --wt NAME            Run in a Claude Code worktree called NAME (letters, digits, . _ -)
  --prompt-file PATH   Use the contents of PATH as the prompt, then delete the file
  --resume ID          Resume session ID, tagging this tab the same way
  --                   End of options; everything after it is the prompt
  -h, --help           Show this help
EOF
}

fail() {
    printf 'tm: %s\n' "$*" >&2
    usage >&2
    exit 2
}

new_session_id() {
    local id=""
    if [[ -r /proc/sys/kernel/random/uuid ]]; then
        id=$(</proc/sys/kernel/random/uuid)
    elif command -v uuidgen >/dev/null 2>&1; then
        id=$(uuidgen)
    else
        printf 'tm: cannot generate a session id: no /proc/sys/kernel/random/uuid and no uuidgen\n' >&2
        exit 1
    fi
    printf '%s' "${id,,}"
}

# Names come from the folder or --wt; control characters would let them end the title escape early.
set_title() {
    local title=${1//[[:cntrl:]]/}
    if [[ -t 1 ]]; then
        printf '\033]0;%s\007' "$title"
    elif [[ -t 2 ]]; then
        printf '\033]0;%s\007' "$title" >&2
    fi
}

worktree=""
prompt_file=""
resume_id=""
prompt_words=()

while [[ $# -gt 0 ]]; do
    case "$1" in
        -h|--help) usage; exit 0 ;;
        --wt|--prompt-file|--resume)
            [[ $# -ge 2 ]] || fail "$1 needs a value"
            case "$1" in
                --wt) worktree=$2 ;;
                --prompt-file) prompt_file=$2 ;;
                --resume) resume_id=$2 ;;
            esac
            shift 2
            ;;
        --) shift; prompt_words+=("$@"); break ;;
        -*) fail "unknown option: $1" ;;
        *) prompt_words+=("$1"); shift ;;
    esac
done

if [[ -n "$worktree" ]]; then
    [[ "$worktree" =~ ^[A-Za-z0-9._-]+$ && "$worktree" != -* && "$worktree" != . && "$worktree" != .. ]] ||
        fail "invalid worktree name '$worktree': use letters, digits, '.', '_' and '-'"
fi

if [[ -n "$resume_id" ]]; then
    [[ "$resume_id" =~ ^[0-9A-Za-z-]+$ ]] || fail "invalid session id '$resume_id'"
    [[ -z "$worktree" && -z "$prompt_file" && ${#prompt_words[@]} -eq 0 ]] ||
        fail "--resume cannot be combined with --wt, --prompt-file or a prompt"
fi

command -v claude >/dev/null 2>&1 || { printf 'tm: claude not found in PATH\n' >&2; exit 1; }

folder=${PWD##*/}
name=${worktree:-${folder:-session}}

# A prompt file is read and deleted before claude starts, so it is never left behind or reused.
prompt=""
if [[ -n "$prompt_file" ]]; then
    [[ -f "$prompt_file" ]] || { printf 'tm: prompt file not found: %s\n' "$prompt_file" >&2; exit 1; }
    prompt=$(<"$prompt_file") || { printf 'tm: cannot read prompt file: %s\n' "$prompt_file" >&2; exit 1; }
    rm -f -- "$prompt_file" || { printf 'tm: cannot delete prompt file: %s\n' "$prompt_file" >&2; exit 1; }
elif [[ ${#prompt_words[@]} -gt 0 ]]; then
    prompt="${prompt_words[*]}"
fi

export CLAUDE_CODE_DISABLE_TERMINAL_TITLE=1

if [[ -n "$resume_id" ]]; then
    set_title "TM $name ${resume_id:0:8}"
    exec claude --resume "$resume_id"
fi

session_id=$(new_session_id)
set_title "TM $name ${session_id:0:8}"

claude_args=(--session-id "$session_id" -n "$name")
if [[ -n "$worktree" ]]; then claude_args+=(-w "$worktree"); fi
if [[ -n "$prompt" ]]; then
    claude_args+=(-- "$prompt")
fi
exec claude "${claude_args[@]}"
