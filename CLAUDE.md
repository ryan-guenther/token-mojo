# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

`cctokmon` — a zero-dependency Bash terminal dashboard showing live Claude Code token usage, cost, and context window data. Full specification is in `claude-token-monitor-spec.md`.

**Stack:** Pure Bash 4.0+ · `jq` (required) · `tput` · `awk` · no other dependencies.

TokenMojo is the GUI on top of the same bridge files: a macOS menu bar app (`menubar/`, Swift) and a Windows tray app (`windows/`, C# / .NET 10 WPF).

## File structure

```
cctokmon.sh              # Main monitor — entry point
cctokmon-statusline.sh   # Companion statusline script (called by Claude Code on every interaction)
cctokmon-hook.sh         # Claude Code hook (called on session events) that appends each session's events to a log
cctokmon-worktree-init.sh  # SessionStart hook: initialises submodules in a new `.claude/worktrees/` worktree
cctokmon-tm.sh           # `tm`: starts or resumes Claude Code in the current tab and tags the tab title for TokenMojo
install.sh               # Installer: deps check, symlink, statusline and hook config
uninstall.sh             # Clean removal
config.example           # Config template copied to ~/.config/cctokmon/config on install
CLAUDE.md                # This file
claude-token-monitor-spec.md  # Original build spec (source of truth for design decisions)
menubar/
  TokenMojo.swift        # macOS menu bar app (AppKit + SwiftUI, single file)
  build.sh               # swiftc → ~/Applications/TokenMojo.app
windows/
  build.sh               # tests, then dotnet publish → %LOCALAPPDATA%\Programs\TokenMojo\TokenMojo.exe
  TokenMojo.Windows.slnx
  src/TokenMojo.Core/    # net10.0, no UI: bridge parsing, display rules, session loading, source discovery
  src/TokenMojo/         # net10.0-windows WPF tray app: icon, flyout, pop-out, launcher, Launch at Login
  tests/TokenMojo.Core.Tests/  # xUnit tests for Core (run on any OS)
```

## Running / testing

```bash
bash cctokmon.sh --check     # validate deps and data sources
bash cctokmon.sh --help
bash cctokmon.sh             # bridge mode (default)
bash cctokmon.sh --mode polling
bash install.sh              # or: cctokmon --setup
```

No build step — scripts run directly with bash.

Windows app (needs the .NET 10 SDK; Core tests run on Linux/macOS too, the app builds from any OS):

```bash
dotnet test windows/tests/TokenMojo.Core.Tests
bash windows/build.sh [target-dir]
```

## Key architecture

### Two operating modes

- **Bridge mode (default):** Claude Code calls `cctokmon-statusline.sh` on every interaction → writes JSON atomically to `~/.claude/cctokmon-state.json` → `cctokmon.sh` polls that file every `REFRESH_INTERVAL` seconds. Billing-accurate data.
- **Polling mode:** Reads `~/.claude/stats-cache.json` directly. No setup beyond install; shows daily/all-time totals only.

### Data source rules (critical)

- **Statusline JSON** — billing-accurate. Use `cost.total_cost_usd` directly in bridge mode; do not recalculate from tokens.
- **`stats-cache.json`** — accurate cumulative/daily totals.
- **JSONL `usage.input_tokens` / `usage.output_tokens`** — unreliable streaming placeholders (off by 100–174×). Do not use.

### Rendering

- `tput cup 0 0` for in-place refresh — no `tput clear` per frame (causes flicker). Clear only on first run and SIGWINCH.
- `tput ed` after each frame to erase stale content below (e.g. after resize).
- All float math via `awk`. No `bc`.
- Single `jq` call per data read with `mapfile -t arr` into indexed array.
- `SCRIPT_DIR` resolved via `readlink -f` at startup so `--setup` works through symlinks.
- `stat` mtime: macOS uses `stat -f %m`, Linux uses `stat -c %Y` — detected at runtime via `uname -s`.
- Always prefix `printf` with `LC_NUMERIC=C` when formatting floats (e.g. `LC_NUMERIC=C printf '$%.4f'`). Without it, locales using `,` as the decimal separator cause `printf: invalid number` and crash the script under `set -u`.
- Variables used across conditional branches must be declared before the `if` block. Declaring inside only one branch and referencing outside it triggers `set -u` and kills the process. (Historical example: `bar_w` was declared inside an `else` branch but used afterwards for the rate limit bar.)

### Installer (`install.sh`)

- Checks all deps (bash 4+, jq, tput, awk) before touching anything.
- If deps are missing: detects package manager (brew / apt / dnf / yum / winget), shows what it will install, prompts once, then re-verifies.
- Symlinks `cctokmon.sh` to `~/.local/bin/cctokmon`. Under Git Bash on Windows (`uname -s` is `MINGW*`/`MSYS*`/`CYGWIN*`) it writes a small launcher script instead: `ln -s` there silently copies the file unless Developer Mode is on, and a copy cannot find `install.sh` for `--setup`. `uninstall.sh` removes either form.
- On Windows only jq needs installing (Git Bash ships tput and awk); winget's links dir is added to `PATH` for the re-verify, since the running shell predates it.
- Copies `cctokmon-statusline.sh` to `~/.claude/cctokmon-statusline.sh`.
- Optionally sets `statusLine` in `~/.claude/settings.json` via `jq`. The correct format is an object: `{"type": "command", "command": "/path/to/script"}` — not a plain string (Claude Code's `/doctor` will flag a plain string as invalid).
- Links `cctokmon-tm.sh` as `~/.local/bin/tm` the same way (a launcher marked `# tm launcher` under Git Bash). A `tm` it did not write is left alone, and `uninstall.sh` removes only ours.
- Copies `cctokmon-hook.sh` to `~/.claude/` and, prompting once, appends `{"hooks":[{"type":"command","command":"<dest>"}]}` to each of `SessionStart`, `UserPromptSubmit`, `PreToolUse`, `PostToolUse`, `PostToolUseFailure`, `Notification`, `Stop` and `SessionEnd` in `settings.json`. Idempotent, and other hooks are left alone. `uninstall.sh` removes only entries whose command is ours.
- Copies `cctokmon-worktree-init.sh` the same way and registers it, in the same prompt and idempotently, for `SessionStart` only, as `{"matcher":"startup","hooks":[{"type":"command","command":"<dest>","timeout":600}]}`. An install that already has the eight status hooks is still offered it. `uninstall.sh` removes both commands from `settings.json` before it removes either script.

### Worktree submodule hook (`cctokmon-worktree-init.sh`)

- A separate script, never part of `cctokmon-hook.sh`, which must stay a fast append: a submodule clone can take minutes, hence the 600 second timeout.
- It acts only when the payload's `cwd` contains `/.claude/worktrees/`, `.gitmodules` exists there, and `git submodule status` lists an uninitialised (`-`) entry; then it runs `git submodule update --init --recursive` with `GIT_TERMINAL_PROMPT=0`. Windows-style `cwd` backslashes are turned into slashes first.
- Like the status hook it must never write to stdout (`SessionStart` stdout is added to Claude's context): `exec >&2` at the top. It always exits 0.

### Session status hook (`cctokmon-hook.sh`)

- The hook only appends. For each of the eight events it writes one line, `{"t":<epoch>,"e":"<event>"}` plus `"a"` (agent_id), `"n"` (notification_type) or `"s"` (source) when present, to `~/.claude/cctokmon-sessions/<session_id>.events` with a single `>>`, with the same `session_id` guard as the bridge. It takes no lock and reads no earlier state: a read-modify-write file lost events when hooks overlapped. Lines stay far below 4 KB so each append is one write. The `.events` extension keeps it out of the `*.json` scan, and the 7-day cleanup removes old ones.
- It must never write to stdout (`SessionStart` and `UserPromptSubmit` stdout is added to Claude's context) and must always exit 0. Nothing from prompts or tool input is logged.
- The status is worked out by the reader, `SessionEvents.Reduce`, applying lines in order and skipping any that are not complete events. `SessionStart` (not `source` `compact`) → ready; `UserPromptSubmit` → working, starting a turn only from ready or no status (a subagent's result arrives as a second prompt mid-turn, so while working or needing input it keeps the turn start); `PreToolUse` → working, which clears needs input unless it is a subagent's (`a` set), since a denied prompt is followed by the main agent's next tool call; `PostToolUse` and `PostToolUseFailure` → working, clearing needs input whether or not `a` is set; `Notification` with `n` of `permission_prompt`, `elicitation_dialog`, `elicitation_url_dialog` or `agent_needs_input` → needs input; `Stop` → ready; `SessionEnd` → ended. A tool event or a needs-input notification from ready or no status sets the turn start to its own time (a background wake, or a turn whose start fell outside the 64 KB window), so an old turn start never raises a finished alert. Ended changes only on `SessionStart`, not on a prompt, because a resume sends `SessionStart` first.
- Session ids are deduplicated once, in `SessionStore.LoadSessions` (newest `Updated`, ties broken by the events file's write time), so rows, the waiting count and alerts see one copy.
- `SessionStore` reads the last 64 KB of the file (dropping the partial first line). No file means no status; an unreadable file skips the session for that refresh.

### Config file

`~/.config/cctokmon/config` (or `$CCTOKMON_CONFIG`). Sourced after CLI args are parsed, so CLI args take precedence. Key settings: `REFRESH_INTERVAL`, `MODE`, `STATE_FILE`, `STATS_CACHE`, `THEME`.

### Pricing table

`declare -A PRICE_INPUT PRICE_OUTPUT` near top of `cctokmon.sh`. Used **only** in polling mode for cost estimation. In bridge mode `cost.total_cost_usd` from the statusline JSON is authoritative.

### `tm` (`cctokmon-tm.sh`)

- `tm [--wt NAME] [--prompt-file PATH] [--] [prompt...]` generates a session id, prints the OSC 0 title `TM <name> <id8>` (id8 is the first 8 characters of the id; name is `--wt` or the folder), then `exec`s `claude --session-id <id> -n <name> [-w NAME] [-- prompt]`. `tm --resume <id>` tags the tab the same way and runs `claude --resume <id>`.
- It exports `CLAUDE_CODE_DISABLE_TERMINAL_TITLE=1`, because Claude Code otherwise rewrites the title; this also turns off Claude's own title updates and terminal status reports in that session.
- `--prompt-file` reads the prompt, deletes the file, and fails if it is missing. Bad arguments (unknown flag, missing value, a worktree name outside `[A-Za-z0-9._-]`) print usage to stderr and exit 2.
- Resuming does not depend on the folder: `claude --resume <id>` finds the session from the repository root, or any folder, after its worktree was removed (it continues without the worktree).

### Windows tray app (`windows/`)

- Shares everything outside the UI with the Mac app: the same bridge script, installer and file contract (`cctokmon-state.json`, `cctokmon-sessions/`, `CCTOKMON_STATE_FILE` / `CCTOKMON_SESSIONS_DIR`). Do not add a Windows-specific bridge; under native Windows, Claude Code runs the bash bridge through Git Bash.
- Display rules mirror `menubar/TokenMojo.swift` and must stay in step with it: `livePercent` (null once `resets_at` has passed), severity thresholds 50/80, the 12-hour session window, "active" within 10 minutes, at most 10 session rows, reset/percent/cost wording. They live in `TokenMojo.Core` (`Gauges`, `Dashboard`, `SessionStore`) so they are unit-tested; the WPF project only binds to `DashboardModel`.
- Sources: `ClaudeDirLocator` returns `%USERPROFILE%\.claude` plus `\\wsl.localhost\<distro>\home\*\.claude` (and `/root`) for distros listed by `wsl.exe --list --running --quiet` (UTF-16LE output). Only running distros are read, because touching a stopped distro's share boots it. Discovery reruns every minute; the bridge files are polled every 5 seconds off the UI thread.
- Session status differs from the Mac app: the Windows app reads the hook's `.events` log (`SessionEvents`, `StatusAlerts`), so a row is needs input / ready / working / ended. Only a session with no events file falls back to the Mac app's 10-minute "active" rule. The list orders needs input, ready, working, no status, ended, then newest first, before the 10-row cap.
- Alerts: a balloon tip on entering needs input, and on any change into ready after a turn of at least `longTurnSeconds` (default 30) from `%APPDATA%\TokenMojo\settings.json`. The first load is a baseline and alerts nothing. An existing settings file that is invalid is shown in the flyout and defaults are not used; when the file is absent and cannot be created, defaults are used. The tray ring's centre shows the waiting count (needs input plus ready) in place of the percentage.
- Click-to-focus and resume: `SessionWindows.Find` matches the `tm` tag's id8 as a whole token in the title of a Windows Terminal window (class `CASCADIA_HOSTING_WINDOW_CLASS`, whose title is the active tab's), `SessionActions.Decide` picks focus, confirm-resume, resume or unavailable, and `ResumeCommand` builds the `wt.exe` argument list (a list, so paths with spaces survive; `-u` the session's Linux user; the folder is `workspace.project_dir`, else `current_dir`, then the repository root when the worktree is gone, then `~`; `tm` is called as `~/.local/bin/tm`, unquoted, since a login shell may not have it on PATH; the session id must be a UUID). Anything that touches the WSL share runs under `Task.Run`, never on the UI thread. All of it is in Core; `SessionControl`, `TerminalWindows` and `Native` only execute it. Sources carry their `Distro`; a Windows-source session cannot be resumed. A balloon is skipped when the foreground window's title matches the session, and clicking one focuses (or resumes) the first session it lists.
- Launcher (`LauncherWindow`, its own window because the flyout closes on deactivate): from the tray menu, the flyout menu and the "+" in the Sessions header; one window is reused. Core decides, WPF only executes. `LaunchHints.From` reads the first Linear issue or GitHub pull request URL (worktree `<team>-<n>` lowercase or `pr-<n>`; folder from `linearTeams`, or the scanned repo whose `origin` is `<owner>/<repo>`), `RepoScanner` lists repos one level under each `repoRoots` entry (a `.git` file is not a repo root; it never throws), `FolderList` orders default, recent folders, repos, and `LaunchCommand.Build` makes the `wt.exe` arguments through the same `TerminalCommand` base as `ResumeCommand`. A hint only fills fields the user has not chosen.
- The launch command is `wt.exe -w new wsl.exe -d <distro> -u <user> --cd <folder> -- bash -lc "~/.local/bin/tm [--wt NAME] [--prompt-file ~/.claude/cctokmon-launch/<guid>.txt]"`. The shell string must never hold a `;` (wt.exe splits its command there; arguments are escaped, but this one has nothing to escape) or a quote. The prompt goes through a file written by `LaunchPrompt` (UTF-8, no BOM, LF) that `tm` deletes; `SessionLauncher` deletes it if the terminal fails to start. The folder passes to `--cd` as given (`~`, `~/x`, `/x`), validated by `LinuxFolder.IsValid`; the worktree name by `tm`'s own rule. The user is the one whose `~/.claude` is in the distro (`LaunchTargets.UserFor`). Settings fields `distro`, `defaultFolder`, `repoRoots`, `linearTeams` are validated on load like the rest of the file.
- The current state is the most recently written `cctokmon-state.json` across sources (rate limits are per account). Sessions from every source are merged into one list, newest first.
- Failures stay per source: a distro that stops or denies access mid-read is skipped, never allowed to blank the others or throw out of the timer tick. Bridge files are opened with `FileShare.Delete` so the bridge's `mv` can rename over a file TokenMojo is reading on Windows.
- Tray icon: one ring for the worst gauge, drawn by the same `RingGauge.Draw` as the dashboard rings and wrapped as a PNG `.ico`; taskbar light/dark comes from `SystemUsesLightTheme`, app light/dark from `AppsUseLightTheme`.
- `build.sh` publishes self-contained single-file without compression: compression halves the file size but roughly doubles memory, and the app runs all day.
- C# style is enforced by `windows/.editorconfig` with `EnforceCodeStyleInBuild` and warnings as errors: explicit types, never `var` (`IDE0008`); `new()` when the type is already written (`IDE0090`).
