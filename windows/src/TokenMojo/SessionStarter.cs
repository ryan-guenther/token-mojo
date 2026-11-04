using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using TokenMojo.Core;

namespace TokenMojo;

/// <summary>
/// Starts a session in a new terminal window, for the launcher and for tokenmojo:// links alike: where sessions can run,
/// which Linux user runs them, and the last checks before the terminal opens. Every method blocks on the WSL share or
/// wsl.exe, so call them off the UI thread. Failures are returned as text to show, never thrown.
/// </summary>
internal static class SessionStarter
{
    /// <summary>Where sessions can be started now (<see cref="LaunchTargets.Targets"/>), and the Claude Code folders found.</summary>
    public static (IReadOnlyList<string> Targets, IReadOnlyList<ClaudeSource> Sources) Locate(IWslDistros wsl, ClaudeDirLocator locator)
    {
        IReadOnlyList<string> running = wsl.Running();
        IReadOnlyList<ClaudeSource> sources = locator.Locate();
        bool windowsHasClaude = Directory.Exists(Path.Combine(SessionControl.WindowsHome, ".claude"));
        return (LaunchTargets.Targets(running, sources, windowsHasClaude), sources);
    }

    /// <summary>The Linux user to start as in the distro, asking the distro about each candidate's tm.</summary>
    public static LaunchUser ChooseUser(IReadOnlyList<ClaudeSource> sources, string distro)
    {
        WslTmInspector inspector = new();
        return LaunchTargets.ChooseUser(sources, distro, source => inspector.Inspect(distro, source.User!));
    }

    /// <summary>Starts the session in WSL. Returns what to show when it could not be started, else null.</summary>
    public static string? TryLaunch(LaunchRequest request)
    {
        try
        {
            // Checked again: it was inspected when the target was chosen, which may be a while ago.
            if (SessionControl.TmProblem(request.Distro, request.User, request.Home) is { } problem) return problem;
            string? start = LinuxFolder.Expand(request.Folder, request.Home);
            if (start is null || !Directory.Exists(WslPaths.SharePath(request.Distro, start)))
                return $"The folder does not exist in {request.Distro}: {request.Folder}";
            SessionLauncher.Launch(request, new WslPromptHandoff(), TerminalProcess.Start);
            return null;
        }
        catch (Exception e) when (e is Win32Exception or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Trace.TraceWarning("TokenMojo: cannot start a session: {0}", e.Message);
            return $"Could not start the session: {e.Message}";
        }
    }

    /// <summary>Claude Code on Windows itself: the prompt file goes straight into Git Bash's home and Git Bash runs tm.</summary>
    public static string? TryLaunchNative(string folder, string? worktree, string prompt)
    {
        try
        {
            // Checked again: Git Bash or tm may have changed since the target was chosen.
            (string? bash, string? problem) = SessionControl.NativeCheck();
            if (problem is not null) return problem;
            string home = SessionControl.WindowsHome;
            string start = WindowsFolder.Expand(folder, home);
            if (!Directory.Exists(start)) return $"The folder does not exist: {folder}";
            LaunchRequest request = new(LaunchTargets.Windows, "", start, worktree, prompt, home);
            SessionLauncher.LaunchNative(request, bash!, new LocalPromptHandoff(SessionControl.GitBashHome), TerminalProcess.Start);
            return null;
        }
        catch (Exception e) when (e is Win32Exception or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Trace.TraceWarning("TokenMojo: cannot start a session: {0}", e.Message);
            return $"Could not start the session: {e.Message}";
        }
    }
}
