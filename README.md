# TokenMojo

**Mo tokens, mo mojo.** A macOS menu bar app and Windows tray app that keeps your Claude Code rate limits, context window and cost in view — with a dashboard of every recent session and a pop-out window that stays on top. See [TokenMojo: macOS menu bar app](#tokenmojo-macos-menu-bar-app) or [TokenMojo: Windows tray app](#tokenmojo-windows-tray-app) to install it.

TokenMojo is built on [cctokmon](https://github.com/ramon-webdevpro-nl/cctokmon) (MIT), whose statusline bridge supplies its data. The cctokmon terminal dashboard is documented below and still works as before.

---

# cctokmon

**Live token usage, cost, and context window monitor for Claude Code.**

Runs in a dedicated terminal pane alongside your Claude Code session. Data comes directly from Claude Code's billing engine — not from JSONL logs, which are known to undercount tokens by up to 174×.

```
┌──────────────────────────────────────────────────────┐
│  CCTOKMON · Live                    ↻ 10s  04/08 09:14 │
├──────────────────────────────────────────────────────┤
│  Model: Sonnet 4.6                  Session: 56m 12s │
│  Project: ~/Documents/myapp                          │
├──────────────────────────────────────────────────────┤
│  CONTEXT WINDOW                                      │
│  █████████████████████████░░░░░░░░░░░░░░░░  63%     │
│  Input:   8,500 tok       Output:  1,200 tok         │
│  Cache R: 2,000 tok       Cache W: 5,000 tok         │
│  Total:   126,700 / 200,000 tok                      │
├──────────────────────────────────────────────────────┤
│  COST & USAGE                                        │
│  Session cost:    $5.3621                            │
│  Lines: +1,842 / -437                               │
│  Burn rate:       $5.66/hr                           │
├──────────────────────────────────────────────────────┤
│  RATE LIMIT (5hr window)                             │
│  ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░  0.0%   │
├──────────────────────────────────────────────────────┤
│  ▲ Updated 3s ago                                    │
└──────────────────────────────────────────────────────┘
```

---

## Why cctokmon

Most Claude Code usage monitors (ccusage, Claude-Code-Usage-Monitor) parse the JSONL session logs in `~/.claude/projects/`. This works for session history and activity counts, but the token fields in those logs are **unreliable**:

- `usage.input_tokens` in JSONL is a streaming placeholder — roughly 75% of entries contain `0` or `1`, never updated after streaming completes
- Input tokens are undercounted by **100–174×**
- Output tokens are undercounted by **10–17×**
- The `costUSD` field was removed from JSONL entirely as of Claude Code v1.0.9 for Max plan users

cctokmon avoids this by hooking into Claude Code's **statusline API** — a feature that sends a full billing-accurate JSON payload to a companion script on every interaction. Cost and token counts come from the same source Claude Code uses for its own billing display.

**Polling mode** (no setup required) reads `~/.claude/stats-cache.json`, which contains accurate aggregated totals that Claude Code maintains separately from the JSONL logs.

---

## Requirements

| Dependency | Version | Notes |
|---|---|---|
| Bash | 4.0+ | macOS ships with Bash 3 — see below |
| jq | any | JSON parsing |
| tput | any | Terminal control (ncurses) — standard on Linux/macOS |
| awk | any | Float arithmetic |

**macOS note:** The system Bash on macOS is 3.2 (GPLv2 licensing restriction). Install a current version:
```bash
brew install bash
```
Under Bash 3 the scripts say so instead of failing quietly. `cctokmon`, `install.sh`, `uninstall.sh` and `tm` print an error and exit with status 1. The hook scripts print one line to stderr, log nothing and exit 0, so a session is never interrupted. The statusline script needs only Bash 3.2, so it keeps working under the system Bash on macOS. Claude Code runs the hooks with whichever `bash` is first in its `PATH`, so on macOS make sure that is the Homebrew one.

jq and tput are available via Homebrew if missing:
```bash
brew install jq ncurses
```

**Linux:** jq may not be pre-installed:
```bash
sudo apt install jq        # Debian/Ubuntu
sudo dnf install jq        # Fedora/RHEL
```

**Windows:** run everything from Git Bash ([Git for Windows](https://gitforwindows.org/)), which ships Bash, tput and awk. Claude Code runs the statusline through Git Bash when it is installed (otherwise PowerShell, which cannot run the bridge). The installer offers to install jq with winget:
```bash
winget install --exact --id jqlang.jq
```
Restart Claude Code afterwards so it picks up jq on its PATH. If you run Claude Code inside WSL instead, follow the Linux steps inside WSL.

---

## Installation

```bash
git clone https://github.com/mohammadriazi/token-mojo.git
cd token-mojo
bash install.sh
```

The installer will:

1. Check all dependencies and offer to install any that are missing via your system package manager (apt / dnf / brew / winget)
2. Copy your config to `~/.config/cctokmon/config`
3. Symlink `cctokmon` to `~/.local/bin/cctokmon` (on Windows, a small launcher script, since Git Bash cannot reliably symlink)
4. Copy the statusline companion script to `~/.claude/cctokmon-statusline.sh` (creating `~/.claude` if Claude Code has not yet)
5. Optionally configure the statusline bridge in `~/.claude/settings.json`
6. Link `tm` to `~/.local/bin/tm` (a launcher on Windows; an existing `tm` that is not cctokmon's is left alone), see [Starting sessions with tm](#starting-sessions-with-tm)
7. Copy the session status hook to `~/.claude/cctokmon-hook.sh` and optionally register it in `~/.claude/settings.json` (used by the TokenMojo Windows app, see [Session status](#session-status))
8. Copy the worktree submodule hook to `~/.claude/cctokmon-worktree-init.sh` and register it with the same prompt, see [Worktrees and submodules](#worktrees-and-submodules)

If `~/.local/bin` is not in your PATH, add this to your `~/.bashrc` or `~/.zshrc`:
```bash
export PATH="$HOME/.local/bin:$PATH"
```

Each prompt shows its default as the capital letter in `[Y/n]` or `[y/N]`. When there is no one to answer (input closed, for example `bash install.sh </dev/null`), the installer takes the default and carries on. Changes to `~/.claude/settings.json` are written through a temp file next to it, so the file's permissions are kept, a symlinked `settings.json` stays a symlink (the real file behind it is updated), and a failed edit leaves the original untouched.

---

## Usage

Open cctokmon in a separate terminal pane while Claude Code runs in another:

```bash
cctokmon                      # bridge mode (default, billing-accurate)
cctokmon --mode polling       # polling mode (stats-cache.json, no setup needed)
cctokmon --refresh 5          # refresh every 5 seconds
cctokmon --theme minimal      # reduced color output
cctokmon --check              # verify setup and data sources
cctokmon --setup              # re-run the installer
```

Press `Ctrl+C` to exit cleanly.

### Options

| Flag | Default | Description |
|---|---|---|
| `-m, --mode MODE` | `bridge` | `bridge` or `polling` |
| `-r, --refresh SECS` | `10` | Seconds between refreshes |
| `-t, --theme THEME` | `default` | `default`, `minimal`, or `none` |
| `-s, --scope SCOPE` | `session` | `session`, `daily`, or `cumulative` |
| `-c, --config FILE` | `~/.config/cctokmon/config` | Path to config file |
| `--check` | | Validate dependencies and data sources |
| `--setup` | | Run the installer |

---

## Modes

### Bridge mode (recommended)

The installer configures `cctokmon-statusline.sh` as your Claude Code statusline script by setting `statusLine` in `~/.claude/settings.json`. Claude Code calls this script on every interaction, passing a JSON payload via stdin. The script writes it atomically to `~/.claude/cctokmon-state.json`. cctokmon reads that file every `REFRESH_INTERVAL` seconds. The script's path is quoted inside the `command` string, so a home folder with spaces works. Re-running the installer rewrites an older unquoted entry to the quoted form without asking, and leaves a quoted one as it is.

Data displayed in bridge mode:
- Session cost (exact, from billing engine)
- Context window usage with per-request token breakdown
- Input / output / cache read / cache write tokens for the current request
- Session duration and burn rate ($/hr)
- Rate limit window usage (5-hour rolling)
- Lines added / removed

The statusline script also outputs a compact one-line summary to Claude Code's own status bar: `$0.053 | ctx 63%`.

### Polling mode

Reads `~/.claude/stats-cache.json` directly. No bridge configuration needed — works out of the box. Useful if you prefer not to configure a statusline, or as a fallback.

Data displayed in polling mode:
- Today's message count, session count, and tool call count
- All-time token totals per model (input, output, cache read, cache write)
- Estimated cost (calculated from the embedded pricing table)

Polling mode shows aggregated history, not per-request live data.

---

## Configuration

Edit `~/.config/cctokmon/config`:

```bash
# Refresh interval in seconds
REFRESH_INTERVAL=10

# Mode: bridge or polling
MODE=bridge

# Color theme: default, minimal, none
THEME=default

# Cost display scope: session, daily, cumulative
COST_SCOPE=session
```

CLI flags take precedence over the config file. The config path can be overridden with the `CCTOKMON_CONFIG` environment variable.

---

## How the statusline bridge works

```
Claude Code
    │  (on every interaction)
    │  pipes JSON billing payload to stdin
    ▼
cctokmon-statusline.sh
    │  writes atomically to
    ▼
~/.claude/cctokmon-state.json
    │  read every REFRESH_INTERVAL seconds by
    ▼
cctokmon.sh  →  terminal dashboard
```

The statusline script must complete in under 100ms (Claude Code requirement). It reads stdin, writes to a temp file, and atomically renames it. It also keeps a per-session copy in `~/.claude/cctokmon-sessions/<session_id>.json` (pruned after 7 days) so the menu bar app can list every recent session.

### Starting sessions with tm

`tm` starts Claude Code in the current terminal tab and names the tab `TM <name> <id8>`, where `<id8>` is the first 8 characters of the session id. The TokenMojo Windows app finds a session's window by that tag.

```bash
tm                                   # start Claude Code here
tm fix the login bug                 # start with a prompt
tm --wt fix-login fix the login bug  # in a Claude Code worktree called fix-login
tm --prompt-file task.txt            # use the file as the prompt, then delete it
tm --resume <session-id>             # resume a session, tagging the tab again
tm -- -starts-with-a-dash            # everything after -- is the prompt
```

`--wt NAME` accepts letters, digits, `.`, `_` and `-`. `tm` sets `CLAUDE_CODE_DISABLE_TERMINAL_TITLE=1`, since Claude Code would otherwise rewrite the tab title; the cost is that Claude's own title updates and terminal status reports are off in that session.

### Worktrees and submodules

`tm --wt NAME` runs Claude Code with `claude -w NAME`, which creates `<repo>/.claude/worktrees/NAME` on a branch called `worktree-NAME`, branched from the main checkout's current commit. Git leaves a new worktree's submodules empty, so the installer also offers a second hook, `cctokmon-worktree-init.sh`. It runs only when a session starts (`SessionStart`, matcher `startup`) in a folder under `.claude/worktrees/` that has a `.gitmodules` file and uninitialised submodules, and runs `git submodule update --init --recursive` there. It is registered with a 600 second timeout, since a clone can take minutes, and kept apart from `cctokmon-hook.sh`, which must stay a fast append. It prints only to stderr and always exits 0, and it never waits on a prompt (git's, the credential manager's, or ssh's host key and password questions, with any ssh command you configured kept), so a submodule that needs credentials it cannot get fails with a message rather than hanging.

Worktrees show up as untracked files in the main checkout. To keep them out of every repository's `git status`, add them to your global ignore file once (git reads `~/.config/git/ignore` when `core.excludesFile` is not set):

```bash
mkdir -p ~/.config/git && echo '.claude/worktrees/' >> ~/.config/git/ignore
```

### Session status

`cctokmon-hook.sh` is a separate Claude Code hook that logs what each session is doing, one line per event, in `~/.claude/cctokmon-sessions/<session_id>.events` (event name and time only, never your prompts or tool input). TokenMojo reads the end of that log to get the status. The installer offers to register it for ten events, adding an entry beside any hooks you already have:

| Event | Status |
|---|---|
| `SessionStart` (except after a compact), `Stop` | ready |
| `UserPromptSubmit`, `PreToolUse`, `PostToolUse`, `PostToolUseFailure` | working |
| `SubagentStart`, `SubagentStop`, and any event from a subagent | keep the session working after the main agent's `Stop`, until the last subagent is done |
| `Notification` for a permission prompt or other question | needs input |
| `SessionEnd` | ended |

The hook never prints anything and always exits 0, so it cannot interrupt a session. Restart running sessions after registering it. The Windows app uses these files; the macOS app and `cctokmon` do not.

A background subagent keeps working after the main agent's `Stop`, and long model calls fire no events. The main agent's own events set its state; subagents (events with an agent id) never start or extend a turn. While the main agent is ready and a subagent is live, the row shows "working" and says how many subagents. A subagent is live from its first event until its `SubagentStop`, and any event it sends (including its tool calls) refreshes it. A subagent that sends nothing for 10 minutes is treated as finished, so a lost `SubagentStop` cannot pin the session. The "Finished" alert fires when the row goes back to ready, measured from the start of the main agent's turn. It fires at most once per turn, even if a subagent goes quiet, reappears and stops again. Re-running the installer adds the two subagent events to an install that has the original eight.

Each prompt has an owner: the subagent that raised it, or the main agent. The main agent's prompt clears when the main agent next starts or finishes a tool, which is what follows an approval or a denial; a subagent's tool events do not clear it. A subagent's prompt clears only when that same subagent finishes a tool, stops, or goes quiet for 10 minutes, and the row returns to what it was doing before. A session that has ended stays ended until it starts again, and a prompt alone does not revive it.

Limits: with parallel tool calls, another tool finishing can clear needs input while a prompt is still open. If a second prompt arrives while one is open, the newest is shown, and when it clears the earlier one is not shown again. Pressing Esc to interrupt a turn fires no hook, so the session shows "working" until your next prompt, and that prompt continues the old turn's timer: a short reply after a long idle gap can raise a "Finished" alert. A subagent that crashes without a `SubagentStop` keeps the row "working" for at most 10 minutes after its last event. A session that crashes never sends `SessionEnd` and keeps its last status until it leaves the 12-hour list.

---

## TokenMojo: macOS menu bar app

`menubar/` contains TokenMojo, a native menu bar companion (macOS 13+). The menu bar shows ring gauges for the 5-hour limit, 7-day limit and context window plus session cost; clicking it opens a dashboard with the active session's details and a list of every session from the last 12 hours. **Pop Out Window** opens the same dashboard in a floating window that stays on top.

It reads the bridge files, so complete the [Installation](#installation) with the statusline bridge enabled first. Then build it (needs the Xcode command line tools: `xcode-select --install`):

```bash
bash menubar/build.sh        # builds ~/Applications/TokenMojo.app
open ~/Applications/TokenMojo.app
```

Enable **Launch at Login** from its menu to start it automatically. Re-run `build.sh` after pulling updates.

If you already use your own statusline script, keep it and chain the bridge from inside it instead of replacing it:

```bash
input=$(cat)
printf '%s' "$input" | "$HOME/.claude/cctokmon-statusline.sh" >/dev/null 2>&1
# ...rest of your statusline, reading "$input"
```

---

## TokenMojo: Windows tray app

`windows/` contains the same dashboard for Windows 10 and 11, as a notification-area (system tray) app. It follows the Windows light/dark theme.

It reads the same bridge files as the Mac app, so complete the [Installation](#installation) with the statusline bridge enabled first, wherever Claude Code runs:

- **Native Windows** — run `install.sh` from Git Bash (see the Windows note under [Requirements](#requirements)). The bridge writes to `%USERPROFILE%\.claude`.
- **WSL** — run `install.sh` inside WSL. TokenMojo reads `~/.claude` in every running WSL distro over `\\wsl.localhost`; stopped distros are never started just to be read.

If you use both, sessions from all of them appear in one list. If you already have your own statusline script, chain the bridge from it as shown in the [macOS section](#tokenmojo-macos-menu-bar-app) instead of replacing it.

### Build

Needs the [.NET 10 SDK](https://dotnet.microsoft.com/download) on the machine that builds it. The result is self-contained, so the machine that runs it needs no .NET runtime. From Git Bash:

```bash
bash windows/build.sh        # runs the tests, builds %LOCALAPPDATA%\Programs\TokenMojo\TokenMojo.exe
"$LOCALAPPDATA/Programs/TokenMojo/TokenMojo.exe" &
```

Or from WSL, with the .NET 10 SDK installed there, passing the Windows folder to build into:

```bash
bash windows/build.sh "/mnt/c/Users/<you>/AppData/Local/Programs/TokenMojo"
"/mnt/c/Users/<you>/AppData/Local/Programs/TokenMojo/TokenMojo.exe" &
```

Quit TokenMojo before re-running `build.sh` after pulling updates.

### Using it

Windows hides new tray icons under the **^** arrow beside the clock at first; drag the ring onto the taskbar to keep it visible.

- **The icon** is one ring for whichever of the 5-hour limit, 7-day limit and context window is highest, with that percentage inside: green under 50%, orange from 50%, red from 80%. `!` means 100% or more, and `–` means there is no data yet. A limit whose window has reset shows "Reset — awaiting update" in the dashboard until your next Claude Code interaction refreshes it.
- **Hover** for all three gauges plus the session cost.
- **Left-click** opens the dashboard beside the taskbar; click elsewhere to close it. It shows the current session's model, name and folder, the 5-hour and 7-day rings with their reset times, the context window, cost, lines changed and tokens, and every session from the last 12 hours. In that list the highlighted row is the current session, and the dot shows its [status](#session-status): red needs input, teal ready for your next prompt, green working, grey ended. Sessions that never reported a status show green if active in the last 10 minutes and grey otherwise. Sessions needing you are listed first.
- **Click a session row** to bring its terminal window forward. If there is no window, an ended session is resumed in a new Windows Terminal window, and one that still looks like it is running asks first ("Resume in a new terminal anyway?"). **Right-click** a row for Focus, Resume in new terminal, Open folder (in Explorer, through the WSL share) and Copy session ID; items that do not apply are greyed out. After a focus or resume the flyout closes.
- **Alerts** appear as a notification when a session needs input, and when a turn that took `longTurnSeconds` or more finishes (30 by default, see [Settings](#settings)). While any session needs input or is ready, the ring's centre shows how many instead of the percentage (`9+` above nine) and the hover text starts with "N waiting". The first refresh after TokenMojo starts raises no alerts. A notification is skipped when that session's window is already in front, and clicking one brings the session's window forward (or offers to resume it).
- **New Session…** (the right-click menu, the dashboard menu, or the **+** in the Sessions header) opens the [launcher](#starting-a-session-from-tokenmojo).
- **Pop Out Window** opens the same dashboard in a window that stays on top; closing it remembers where it was.
- **Launch at Login** (in the dashboard or the right-click menu) starts it automatically. **Quit** is in both too.

The 5-hour and 7-day rings appear only for Claude Pro and Max subscribers; with an API key there are no rate limits, so only the context window shows.

Limits of click-to-focus and resume:

- Focus works only for sessions started with `tm` (see [Starting sessions with tm](#starting-sessions-with-tm)), and only while that tab is the active tab in its Windows Terminal window, because the window title is the active tab's.
- Resume is for WSL sessions. A session from the Windows profile can be focused but not resumed.
- Resume runs as the Linux account that owns the home folder holding the session's files. TokenMojo asks each running distro who that is (one command per distro, once a minute), so an account whose name differs from its home folder works. If the distro cannot be asked, the answer from an earlier minute is reused; the home folder name is used as the user name only when it has never answered. `tm` must be installed there (`install.sh` in that distro) and must be cctokmon's: a `tm` that `install.sh` left alone because it is not cctokmon's is never run, and resume says so instead.
- If the session's folder (for example a removed worktree) no longer exists, resume starts in the repository root, or in your home folder when that is gone too.

### Starting a session from TokenMojo

**New Session…** opens a small window that starts Claude Code in a new Windows Terminal window running [`tm`](#starting-sessions-with-tm), so the session is tagged and appears in the list like any other. It is for WSL only. Run `install.sh` in the distro first, and make sure `wt.exe` (Windows Terminal) is installed.

- **Prompt** is sent to Claude unchanged, quotes, semicolons, `$` and line breaks included. Enter starts the session; Shift+Enter adds a line. Leave it empty to start an idle session. The prompt travels in a temporary file (`~/.claude/cctokmon-launch/<id>.txt`, mode 600) that is created as the Linux user the session runs as, by a `wsl.exe -u` command that reads the prompt from standard input (never from a command line), and that `tm` reads and deletes before anything else can stop it (a file it cannot delete is only a warning); if the terminal cannot be started, the launcher deletes the file itself, and `tm` removes any launch file older than a day on each run.
- **Folder** is where the session starts. Pick from the list or type a path (`~`, `~/my-app` or `/srv/code`; double quotes and backslashes are not accepted). The list holds `defaultFolder`, then the folders of recent sessions (a worktree is listed as its repository), then the git repositories found directly under each of `repoRoots`. **Browse…** opens a folder dialog at your distro home; only folders inside the distro can be used.
- **Worktree** starts the session in `claude -w NAME`, as described under [Worktrees and submodules](#worktrees-and-submodules). Names take letters, digits, `.`, `_` and `-`.
- **Links in the prompt** pre-fill the folder and worktree, using the first link found. A Linear issue link (`https://linear.app/<workspace>/issue/ENG-42/...`) suggests the worktree `eng-42` and the folder mapped to `ENG` in `linearTeams`. A GitHub pull request link (`https://github.com/octo/widgets/pull/12`) suggests the worktree `pr-12` and the repository with a remote at `octo/widgets` (https or ssh, with or without `.git`): one whose `origin` matches comes first, otherwise the first with any matching remote, such as a fork whose `upstream` is `octo/widgets`. The worktree box is ticked only when the folder was found too; with a link but no matching folder the name is suggested and the box left unticked. The line under the prompt says what was detected. Anything you set yourself is left alone, and the prompt itself is never changed.
- **WSL distro** appears only when more than one distro is running.

The session runs as the distro's default user when that user has a `~/.claude` with a cctokmon `tm`; otherwise as the first user that has both, and the window says so under the prompt. If nobody has a cctokmon `tm` (none installed, or a `tm` that is not cctokmon's), nothing is started and the window says which; if the distro did not answer it says it could not check, and a `tm` that exists but cannot be read, or a check that could not run as that user, is reported as such. Pressing Start asks again when no user was found. While a session is being started, Cancel and closing the window wait for it to finish. Limits: users and their `tm` are read when the window opens, so reopen it after installing or removing `tm`; accounts are found only for homes under `/home` and `/root`; a home shared by two accounts goes to the default user, else the first in the account list. Repositories are listed one level below each root only, and not when `.git` is a file (a worktree or submodule). A repository that cannot be read is skipped. The **+** button is in the Sessions card, which shows once at least one session has been seen; the menu entries are always there.

### Settings

`%APPDATA%\TokenMojo\settings.json` is created on first run with every setting at its default:

```json
{
  "longTurnSeconds": 30,
  "distro": null,
  "defaultFolder": "~",
  "repoRoots": ["~"],
  "linearTeams": {}
}
```

| Setting | Meaning |
|---|---|
| `longTurnSeconds` | How long a turn must take before finishing it raises an alert. |
| `distro` | The WSL distro the launcher uses. `null` uses the first running one; a distro that is not running is ignored. |
| `defaultFolder` | The launcher's first folder. `~`, `~/...` or an absolute path. |
| `repoRoots` | Folders whose git repositories the launcher offers, same form as `defaultFolder`. |
| `linearTeams` | Linear team key to the folder its issues are worked in, for example `{"ENG": "~/my-app"}`. Keys are upper case letters and digits, starting with a letter. |

The file is read on every refresh. If it is not valid JSON or has a bad value, the dashboard shows the error instead of falling back to defaults.

### Troubleshooting

- **"No bridge data yet"** — the bridge has not written anything. Check that `statusLine` is set in `~/.claude/settings.json` (re-run `install.sh` if not), then send any message in Claude Code; the dashboard updates within 5 seconds. For WSL, the distro must be running, and a newly started distro is picked up within a minute.
- **Native Windows only:** Claude Code needs Git Bash to run the bridge, and needs restarting after jq is installed so it finds jq.

---

## Uninstall

```bash
bash uninstall.sh
```

Removes the symlinks (or Windows launchers) at `~/.local/bin/cctokmon` and `~/.local/bin/tm` (a `tm` that is not cctokmon's is kept), the statusline script at `~/.claude/cctokmon-statusline.sh`, the state file at `~/.claude/cctokmon-state.json`, the per-session files in `~/.claude/cctokmon-sessions/`, the hook scripts at `~/.claude/cctokmon-hook.sh` and `~/.claude/cctokmon-worktree-init.sh`, and the `statusLine` entry (only when it is cctokmon's own script, not a custom one) and cctokmon's hook entries from `~/.claude/settings.json` (other hooks are left alone). Optionally removes `~/.config/cctokmon`.

On Windows, turn off **Launch at Login** and quit TokenMojo, then delete `%LOCALAPPDATA%\Programs\TokenMojo`, `%LOCALAPPDATA%\TokenMojo` (the saved pop-out position) and `%APPDATA%\TokenMojo` (settings).

---

## Comparison with other tools

| | cctokmon | ccusage | Claude-Code-Usage-Monitor |
|---|---|---|---|
| Data source | Statusline API / stats-cache | JSONL logs | JSONL logs |
| Token accuracy | Exact (billing engine) | ⚠ Unreliable | ⚠ Unreliable |
| Cost accuracy | Exact | ⚠ Estimated | ⚠ Estimated |
| Real-time | Yes (bridge mode) | No (historical) | Yes (polling JSONL) |
| Context window | Yes | No | Yes |
| Rate limit | Yes | No | Yes |
| Runtime | Bash + jq | Node.js | Python |
| Install | `bash install.sh` | `npm install -g` | `pip install` |

---

## Pricing table

The pricing table in `cctokmon.sh` is used in polling mode only. In bridge mode, `cost.total_cost_usd` from the statusline JSON is authoritative and no calculation is needed. Update the table near the top of `cctokmon.sh` when Anthropic changes pricing:

```bash
PRICE_INPUT["claude-sonnet-4-6"]=3.00   # USD per 1M input tokens
PRICE_OUTPUT["claude-sonnet-4-6"]=15.00  # USD per 1M output tokens
```

---

## License

MIT
