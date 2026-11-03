#!/usr/bin/env bash
# Builds TokenMojo.exe (tray only, no taskbar window) into %LOCALAPPDATA%\Programs\TokenMojo.
# Runs from Git Bash on Windows, or cross-compiles from Linux/WSL with a target directory.
# Adds a TokenMojo shortcut to the Start menu when Windows can be reached (Git Bash or WSL).
set -euo pipefail
cd "$(dirname "$0")"

if [[ $# -ge 1 ]]; then
    TARGET="$1"
elif [[ -n "${LOCALAPPDATA:-}" ]]; then
    TARGET="$(cygpath -u "$LOCALAPPDATA")/Programs/TokenMojo"
else
    echo "Usage: bash windows/build.sh <target-dir>   (LOCALAPPDATA is not set)" >&2
    exit 1
fi

command -v dotnet >/dev/null 2>&1 || { echo "The .NET 10 SDK is required: https://dotnet.microsoft.com/download" >&2; exit 1; }

if command -v cygpath >/dev/null 2>&1; then
    EXE="$(cygpath -w "$TARGET/TokenMojo.exe")"
elif command -v wslpath >/dev/null 2>&1; then
    EXE="$(wslpath -w "$TARGET/TokenMojo.exe")"
else
    EXE=""
fi
WINDOWS=""
if [[ -n "$EXE" ]] && command -v powershell.exe >/dev/null 2>&1; then WINDOWS=1; fi

# Encoded, so the path reaches PowerShell intact whatever it holds; inside single quotes only ' needs doubling.
run_powershell() {
    local script="\$ErrorActionPreference = 'Stop'
\$ProgressPreference = 'SilentlyContinue'
\$exe = '${EXE//\'/\'\'}'
$1"
    powershell.exe -NoProfile -NonInteractive -EncodedCommand "$(printf '%s' "$script" | iconv -f UTF-8 -t UTF-16LE | base64 -w0)"
}

dotnet test tests/TokenMojo.Core.Tests --nologo --verbosity quiet

# A running TokenMojo locks its exe, so stop the one installed here before replacing it.
if [[ -n "$WINDOWS" ]]; then
    STOPPED="$(run_powershell "\$running = @(Get-Process TokenMojo -ErrorAction SilentlyContinue | Where-Object { \$_.Path -eq \$exe })
\$running | Stop-Process -Force
\$running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
\$running.Count")"
    [[ "${STOPPED%$'\r'}" == "0" ]] || echo "Stopped the running TokenMojo"
fi

dotnet publish src/TokenMojo --nologo --verbosity quiet \
    --configuration Release --runtime win-x64 --self-contained \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none \
    --output "$TARGET"
echo "Built $TARGET/TokenMojo.exe"

if [[ -n "$WINDOWS" ]]; then
    LINK="$(run_powershell "\$link = Join-Path ([Environment]::GetFolderPath('Programs')) 'TokenMojo.lnk'
\$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut(\$link)
\$shortcut.TargetPath = \$exe
\$shortcut.WorkingDirectory = Split-Path \$exe
\$shortcut.Save()
\$link")"
    echo "Start menu shortcut: ${LINK%$'\r'}"
else
    echo "No Start menu shortcut: Windows is not reachable from here (needs Git Bash or WSL)."
fi
echo "Run: \"$TARGET/TokenMojo.exe\""
