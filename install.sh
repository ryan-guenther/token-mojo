#!/usr/bin/env bash
# cctokmon installer
# Installs cctokmon and optionally configures the Claude Code statusline bridge.

# Bash 4+ is needed below. Checked first, in syntax that bash 3.2 (stock macOS) can still parse.
if [ "${BASH_VERSINFO[0]:-0}" -lt 4 ]; then
    printf 'cctokmon installer: bash 4.0+ required, found %s.\n' "${BASH_VERSION:-unknown}" >&2
    printf 'macOS ships bash 3.2. Install a current one (brew install bash) and run this script with it.\n' >&2
    exit 1
fi

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
BIN_DIR="$HOME/.local/bin"
CLAUDE_DIR="$HOME/.claude"
CONFIG_DIR="$HOME/.config/cctokmon"
SETTINGS_FILE="$CLAUDE_DIR/settings.json"
STATUSLINE_DEST="$CLAUDE_DIR/cctokmon-statusline.sh"
# The path is quoted inside the command string so a home folder with spaces works; the bare form is still recognised as ours.
STATUSLINE_COMMAND="\"$STATUSLINE_DEST\""
HOOK_DEST="$CLAUDE_DIR/cctokmon-hook.sh"
# Quoted inside the command string, since Claude Code runs it through a shell and HOME may contain spaces
HOOK_COMMAND="\"$HOOK_DEST\""
HOOK_EVENTS='["SessionStart","UserPromptSubmit","PreToolUse","PostToolUse","PostToolUseFailure","Notification","SubagentStart","SubagentStop","Stop","SessionEnd"]'
# Initialises submodules in new worktrees; apart from the hook above, which must stay a fast append
WORKTREE_INIT_DEST="$CLAUDE_DIR/cctokmon-worktree-init.sh"
WORKTREE_INIT_COMMAND="\"$WORKTREE_INIT_DEST\""
# Seconds Claude Code waits for it: a submodule clone can take minutes
WORKTREE_INIT_TIMEOUT=600

# ── Colors ─────────────────────────────────────────────────────────────────────
if tput colors &>/dev/null 2>&1 && [[ "$(tput colors)" -ge 8 ]]; then
    R=$(tput sgr0)
    BOLD=$(tput bold)
    GREEN=$(tput setaf 2)
    YELLOW=$(tput setaf 3)
    RED=$(tput setaf 1)
    CYAN=$(tput setaf 6)
else
    R="" BOLD="" GREEN="" YELLOW="" RED="" CYAN=""
fi

ok()   { printf '%s✓%s %s\n' "${GREEN}" "${R}" "$*"; }
miss() { printf '%s✗%s %s\n' "${RED}" "${R}" "$*"; }
warn() { printf '%s⚠%s  %s\n' "${YELLOW}" "${R}" "$*"; }
err()  { printf '%s✗%s %s\n' "${RED}" "${R}" "$*" >&2; }
info() { printf '  %s\n' "$*"; }

# True for the symlink or the Windows launcher this installer writes as `tm`
is_our_tm() {
    local path="$1"
    if [[ -L "$path" ]]; then
        [[ "$(readlink "$path")" == */cctokmon-tm.sh ]]
    else
        grep -qx '# tm launcher' "$path" 2>/dev/null
    fi
}

# Path of the real file behind PATH, following symlinks without GNU readlink -f (stock macOS lacks it)
resolve_symlinks() {
    local path="$1" link hops=0
    while [[ -L "$path" ]]; do
        (( ++hops <= 40 )) || { err "Too many symbolic links resolving $1"; return 1; }
        link=$(readlink "$path")
        case "$link" in
            /*) path="$link" ;;
            *) path="$(dirname "$path")/$link" ;;
        esac
    done
    printf '%s' "$path"
}

# Permission bits of a file, as chmod takes them (stat differs between macOS and Linux)
file_mode() {
    if [[ "$(uname -s)" == "Darwin" ]]; then
        stat -f %Lp "$1"
    else
        stat -c %a "$1"
    fi
}

# Rewrites settings.json with jq, passing it the given arguments. The result is built in a temp file next to
# the real file (so the final mv is a same-filesystem rename). The original's mode is applied to it after jq
# has written it, so a read-only settings.json works. A symlinked settings.json is written through, so the
# symlink stays. On any failure the original is untouched and the temp file is removed.
SETTINGS_TMP=""
trap '[[ -z "$SETTINGS_TMP" ]] || rm -f "$SETTINGS_TMP"' EXIT
write_settings() {
    local target dir mode
    target=$(resolve_symlinks "$SETTINGS_FILE") || return 1
    # Name the real, normalised location in any message
    dir=$(cd "$(dirname "$target")" && pwd -P) || return 1
    target="$dir/$(basename "$target")"
    if [[ ! -w "$dir" ]]; then
        err "Cannot update ${target}: its directory ${dir} is not writable. Fix the permissions there."
        return 1
    fi
    mode=$(file_mode "$target") || return 1
    SETTINGS_TMP=$(mktemp "$dir/.settings.json.XXXXXX") || return 1
    if jq "$@" "$target" > "$SETTINGS_TMP" && chmod "$mode" "$SETTINGS_TMP" && mv "$SETTINGS_TMP" "$target"; then
        SETTINGS_TMP=""
        return 0
    fi
    rm -f "$SETTINGS_TMP"
    SETTINGS_TMP=""
    return 1
}

# Reads a prompt's answer into ans. End of input (no terminal, stdin closed) leaves ans empty, so the
# prompt's own default applies instead of `read` failing under set -e. A partial last line is kept.
read_answer() {
    ans=""
    if ! read -r ans && [[ -z "$ans" ]]; then
        printf '\n'
    fi
    # Input piped from Windows tools ends its lines with CRLF
    ans=${ans%$'\r'}
}

# ── Detect OS and package manager ──────────────────────────────────────────────
OS="$(uname -s)"
IS_WINDOWS=0
case "$OS" in MINGW*|MSYS*|CYGWIN*) IS_WINDOWS=1 ;; esac
PKG_MGR=""
if   [[ "$OS" == "Darwin" ]] && command -v brew    >/dev/null 2>&1; then PKG_MGR="brew"
elif [[ "$IS_WINDOWS" -eq 1 ]] && command -v winget >/dev/null 2>&1; then PKG_MGR="winget"
elif command -v apt-get >/dev/null 2>&1;                               then PKG_MGR="apt"
elif command -v dnf     >/dev/null 2>&1;                               then PKG_MGR="dnf"
elif command -v yum     >/dev/null 2>&1;                               then PKG_MGR="yum"
fi

# Package name map: tool → apt/dnf/brew package
declare -A PKG_APT PKG_BREW PKG_DNF PKG_WINGET
PKG_APT["jq"]="jq"
PKG_APT["tput"]="ncurses-bin"
PKG_APT["awk"]="gawk"
PKG_BREW["jq"]="jq"
PKG_BREW["tput"]="ncurses"
PKG_BREW["awk"]="gawk"
PKG_DNF["jq"]="jq"
PKG_DNF["tput"]="ncurses"
PKG_DNF["awk"]="gawk"
# Git Bash ships tput and awk; only jq needs installing on Windows
PKG_WINGET["jq"]="jqlang.jq"

pkg_name() {
    local tool="$1"
    case "$PKG_MGR" in
        brew) echo "${PKG_BREW[$tool]:-$tool}" ;;
        winget) echo "${PKG_WINGET[$tool]:-$tool}" ;;
        dnf|yum) echo "${PKG_DNF[$tool]:-$tool}" ;;
        *) echo "${PKG_APT[$tool]:-$tool}" ;;
    esac
}

# ── Dependency checks ──────────────────────────────────────────────────────────

printf '\n%scctokmon installer%s\n' "${BOLD}${CYAN}" "${R}"
printf '%s\n\n' "──────────────────"

ok "Bash ${BASH_VERSION}"

# winget installs jq into its links dir and adds that to the user PATH, which a shell opened
# before the install predates. Looking there first also lets a re-run find an earlier install.
if [[ "$IS_WINDOWS" -eq 1 && -n "${LOCALAPPDATA:-}" ]]; then
    PATH="$PATH:$(cygpath -u "$LOCALAPPDATA")/Microsoft/WinGet/Links"
fi

# Check remaining deps, collecting any that are missing
MISSING_TOOLS=()

for tool in jq tput awk; do
    if command -v "$tool" >/dev/null 2>&1; then
        case "$tool" in
            jq)   ok "jq $(jq --version)" ;;
            tput) ok "tput (ncurses)" ;;
            awk)  ok "awk" ;;
        esac
    else
        miss "${tool} — not found"
        MISSING_TOOLS+=("$tool")
    fi
done

# ── Offer to install missing deps ──────────────────────────────────────────────

if [[ ${#MISSING_TOOLS[@]} -gt 0 ]]; then
    printf '\n%sMissing: %s%s\n' "${BOLD}" "${MISSING_TOOLS[*]}" "${R}"

    if [[ -z "$PKG_MGR" ]]; then
        err "No supported package manager found (apt, dnf, yum, brew, winget)."
        info "Install manually and re-run:"
        for tool in "${MISSING_TOOLS[@]}"; do
            info "  ${tool}"
        done
        exit 1
    fi

    # Build the list of package names for the detected manager
    PKG_LIST=()
    for tool in "${MISSING_TOOLS[@]}"; do
        PKG_LIST+=("$(pkg_name "$tool")")
    done

    case "$PKG_MGR" in
        brew)
            printf '  Install with Homebrew (%s)? [Y/n] ' "${PKG_LIST[*]}"
            read_answer
            if [[ "${ans,,}" == "n" ]]; then
                info "Run manually: brew install ${PKG_LIST[*]}"; exit 1
            fi
            brew install "${PKG_LIST[@]}"
            ;;
        apt)
            printf '  Install with apt (%s)? [Y/n] ' "${PKG_LIST[*]}"
            read_answer
            if [[ "${ans,,}" == "n" ]]; then
                info "Run manually: sudo apt install ${PKG_LIST[*]}"; exit 1
            fi
            sudo apt-get install -y "${PKG_LIST[@]}"
            ;;
        dnf|yum)
            printf '  Install with %s (%s)? [Y/n] ' "$PKG_MGR" "${PKG_LIST[*]}"
            read_answer
            if [[ "${ans,,}" == "n" ]]; then
                info "Run manually: sudo $PKG_MGR install ${PKG_LIST[*]}"; exit 1
            fi
            sudo "$PKG_MGR" install -y "${PKG_LIST[@]}"
            ;;
        winget)
            printf '  Install with winget (%s)? [Y/n] ' "${PKG_LIST[*]}"
            read_answer
            if [[ "${ans,,}" == "n" ]]; then
                info "Run manually: winget install --exact --id ${PKG_LIST[*]}"; exit 1
            fi
            for pkg in "${PKG_LIST[@]}"; do
                winget install --exact --id "$pkg"
            done
            ;;
    esac

    # Re-verify after install
    printf '\n'
    for tool in "${MISSING_TOOLS[@]}"; do
        if command -v "$tool" >/dev/null 2>&1; then
            ok "${tool} installed"
        else
            err "${tool} still not found — install manually and re-run."
            exit 1
        fi
    done
fi

# ── Install files ──────────────────────────────────────────────────────────────

# Config dir
mkdir -p "$CONFIG_DIR"
if [[ ! -f "${CONFIG_DIR}/config" ]]; then
    cp "${SCRIPT_DIR}/config.example" "${CONFIG_DIR}/config"
    ok "Config created: ${CONFIG_DIR}/config"
else
    ok "Config exists:  ${CONFIG_DIR}/config  (not overwritten)"
fi

# Bin dir, and the Claude dir that the scripts below are copied into (absent until Claude Code first runs)
mkdir -p "$BIN_DIR" "$CLAUDE_DIR"

# Symlink main script
chmod +x "${SCRIPT_DIR}/cctokmon.sh"
if [[ "$IS_WINDOWS" -eq 1 ]]; then
    # Git Bash's ln -s silently copies without Developer Mode, and a copy cannot
    # find install.sh for --setup, so install a launcher instead
    printf '#!/usr/bin/env bash\n# cctokmon launcher\nexec bash %q "$@"\n' "${SCRIPT_DIR}/cctokmon.sh" > "${BIN_DIR}/cctokmon"
    chmod +x "${BIN_DIR}/cctokmon"
    ok "Launcher: ${BIN_DIR}/cctokmon → ${SCRIPT_DIR}/cctokmon.sh"
else
    ln -sf "${SCRIPT_DIR}/cctokmon.sh" "${BIN_DIR}/cctokmon"
    ok "Symlinked: ${BIN_DIR}/cctokmon → ${SCRIPT_DIR}/cctokmon.sh"
fi

# Copy statusline companion
chmod +x "${SCRIPT_DIR}/cctokmon-statusline.sh"
cp "${SCRIPT_DIR}/cctokmon-statusline.sh" "$STATUSLINE_DEST"
chmod +x "$STATUSLINE_DEST"
ok "Statusline: ${STATUSLINE_DEST}"

# Copy session status hook
chmod +x "${SCRIPT_DIR}/cctokmon-hook.sh"
cp "${SCRIPT_DIR}/cctokmon-hook.sh" "$HOOK_DEST"
chmod +x "$HOOK_DEST"
ok "Hook:       ${HOOK_DEST}"

# Copy worktree submodule hook
chmod +x "${SCRIPT_DIR}/cctokmon-worktree-init.sh"
cp "${SCRIPT_DIR}/cctokmon-worktree-init.sh" "$WORKTREE_INIT_DEST"
chmod +x "$WORKTREE_INIT_DEST"
ok "Worktree:   ${WORKTREE_INIT_DEST}"

# tm starts a tagged Claude Code session; leave a `tm` that is not ours alone
chmod +x "${SCRIPT_DIR}/cctokmon-tm.sh"
if [[ -e "${BIN_DIR}/tm" || -L "${BIN_DIR}/tm" ]] && ! is_our_tm "${BIN_DIR}/tm"; then
    warn "${BIN_DIR}/tm already exists and is not cctokmon's; leaving it alone."
elif [[ "$IS_WINDOWS" -eq 1 ]]; then
    printf '#!/usr/bin/env bash\n# tm launcher\nexec bash %q "$@"\n' "${SCRIPT_DIR}/cctokmon-tm.sh" > "${BIN_DIR}/tm"
    chmod +x "${BIN_DIR}/tm"
    ok "Launcher: ${BIN_DIR}/tm → ${SCRIPT_DIR}/cctokmon-tm.sh"
else
    ln -sf "${SCRIPT_DIR}/cctokmon-tm.sh" "${BIN_DIR}/tm"
    ok "Symlinked: ${BIN_DIR}/tm → ${SCRIPT_DIR}/cctokmon-tm.sh"
fi

# PATH notice
if ! command -v cctokmon >/dev/null 2>&1; then
    warn "${BIN_DIR} is not in your PATH."
    info "Add this to your ~/.bashrc or ~/.zshrc:"
    info "  export PATH=\"\$HOME/.local/bin:\$PATH\""
fi

# ── Configure Claude Code statusline ──────────────────────────────────────────

printf '\n%sConfigure Claude Code statusline bridge?%s\n' "${BOLD}" "${R}"
printf 'This lets cctokmon receive real-time data from Claude Code.\n'
printf 'It sets "statusLine" in ~/.claude/settings.json.\n\n'

# Check if already configured: ours (quoted, or bare as earlier installs wrote it) or someone else's
if [[ -f "$SETTINGS_FILE" ]] && command -v jq >/dev/null 2>&1; then
    existing_sl=$(jq -r '.statusLine // empty' "$SETTINGS_FILE" 2>/dev/null || true)
    existing_cmd=$(jq -r '(.statusLine | if type == "object" then .command else . end) // empty' "$SETTINGS_FILE" 2>/dev/null || true)
    # A native jq.exe under Git Bash ends its lines with CRLF
    existing_sl=${existing_sl%$'\r'}
    existing_cmd=${existing_cmd%$'\r'}
    if [[ "$existing_cmd" == "$STATUSLINE_COMMAND" ]]; then
        ok "statusLine already configured in ${SETTINGS_FILE}"
        configure_sl=0
    elif [[ "$existing_cmd" == "$STATUSLINE_DEST" ]]; then
        info "Updating the statusLine to the quoted form, so a path with spaces works."
        configure_sl=1
    elif [[ -n "$existing_sl" ]]; then
        warn "A statusLine is already configured: ${existing_sl}"
        printf '  Overwrite with cctokmon? [y/N] '
        read_answer
        if [[ "${ans,,}" != "y" ]]; then
            info "Skipped. To configure manually:"
            info "  Set \"statusLine\": {\"type\": \"command\", \"command\": \"\\\"${STATUSLINE_DEST}\\\"\"} in ~/.claude/settings.json"
            configure_sl=0
        else
            configure_sl=1
        fi
    else
        printf '  Configure now? [Y/n] '
        read_answer
        configure_sl=1
        [[ "${ans,,}" == "n" ]] && configure_sl=0
    fi
else
    printf '  Configure now? [Y/n] '
    read_answer
    configure_sl=1
    [[ "${ans,,}" == "n" ]] && configure_sl=0
fi

if [[ "${configure_sl:-0}" -eq 1 ]]; then
    # Create settings.json if it doesn't exist
    if [[ ! -f "$SETTINGS_FILE" ]]; then
        printf '{}' > "$SETTINGS_FILE"
    fi

    # shellcheck disable=SC2016 # the single-quoted argument is a jq program
    write_settings --arg sl "$STATUSLINE_COMMAND" '.statusLine = {"type": "command", "command": $sl}' ||
        { err "Cannot update ${SETTINGS_FILE}; it was left unchanged."; exit 1; }
    ok "statusLine set in ${SETTINGS_FILE}"
fi

# ── Register Claude Code session status hooks ─────────────────────────────────

printf '\n%sRegister Claude Code session status hooks?%s\n' "${BOLD}" "${R}"
printf 'This lets TokenMojo show which sessions are working, ready or need input.\n'
hook_event_count=$(jq length <<<"$HOOK_EVENTS")
# jq.exe ends its lines with CRLF
hook_event_count=${hook_event_count%$'\r'}
printf 'It adds an entry for each of %s events to "hooks" in ~/.claude/settings.json\n' "$hook_event_count"
printf 'and leaves your existing hooks alone. It also adds a SessionStart hook that\n'
printf 'initialises submodules in worktrees started with tm --wt.\n\n'

configure_hooks=0
hooks_registered=0
worktree_init_registered=false
if [[ -f "$SETTINGS_FILE" ]]; then
    hooks_registered=$(jq --arg cmd "$HOOK_COMMAND" --argjson events "$HOOK_EVENTS" '
        [$events[] as $e | select([.hooks[$e][]?.hooks[]?.command] | index($cmd))] | length
    ' "$SETTINGS_FILE" 2>/dev/null || echo 0)
    worktree_init_registered=$(jq -r --arg cmd "$WORKTREE_INIT_COMMAND" '
        [.hooks.SessionStart[]?.hooks[]?.command] | index($cmd) != null
    ' "$SETTINGS_FILE" 2>/dev/null || echo false)
    # A native jq.exe under Git Bash ends its lines with CRLF
    hooks_registered=${hooks_registered%$'\r'}
    worktree_init_registered=${worktree_init_registered%$'\r'}
fi
if [[ "$hooks_registered" -eq "$hook_event_count" && "$worktree_init_registered" == "true" ]]; then
    ok "Hooks already registered in ${SETTINGS_FILE}"
else
    printf '  Register now? [Y/n] '
    read_answer
    configure_hooks=1
    [[ "${ans,,}" == "n" ]] && configure_hooks=0
fi

if [[ "$configure_hooks" -eq 1 ]]; then
    if [[ ! -f "$SETTINGS_FILE" ]]; then
        printf '{}' > "$SETTINGS_FILE"
    fi

    # shellcheck disable=SC2016 # the single-quoted argument is a jq program
    write_settings --arg cmd "$HOOK_COMMAND" --argjson events "$HOOK_EVENTS" \
        --arg init "$WORKTREE_INIT_COMMAND" --argjson init_timeout "$WORKTREE_INIT_TIMEOUT" '
        reduce $events[] as $e (.;
            if ([.hooks[$e][]?.hooks[]?.command] | index($cmd)) then .
            else .hooks[$e] = ((.hooks[$e] // []) + [{"hooks": [{"type": "command", "command": $cmd}]}])
            end)
        | if ([.hooks.SessionStart[]?.hooks[]?.command] | index($init)) then .
          else .hooks.SessionStart = ((.hooks.SessionStart // [])
              + [{"matcher": "startup", "hooks": [{"type": "command", "command": $init, "timeout": $init_timeout}]}])
          end
    ' || { err "Cannot update ${SETTINGS_FILE}; it was left unchanged."; exit 1; }
    ok "Hooks registered in ${SETTINGS_FILE}"
fi

# ── Done ───────────────────────────────────────────────────────────────────────

printf '\n%sInstallation complete!%s\n\n' "${BOLD}${GREEN}" "${R}"
printf 'Run the monitor:\n'
printf '  %scctokmon%s              # bridge mode (real-time, requires statusline)\n' "${BOLD}" "${R}"
printf '  %scctokmon --mode polling%s   # polling mode (stats-cache.json)\n' "${BOLD}" "${R}"
printf '  %scctokmon --check%s       # verify setup\n' "${BOLD}" "${R}"
printf '  %stm [prompt...]%s         # start Claude Code in this tab, findable from TokenMojo\n\n' "${BOLD}" "${R}"

if [[ "${configure_sl:-0}" -eq 1 ]]; then
    printf 'The statusline bridge is active. Claude Code will update cctokmon\n'
    printf 'on every interaction. Open cctokmon in a separate terminal pane.\n\n'
fi

if [[ "$configure_hooks" -eq 1 ]]; then
    printf 'Session status hooks are registered. Restart running Claude Code sessions\n'
    printf 'to pick them up.\n\n'
fi
