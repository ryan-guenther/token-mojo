using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using TokenMojo.Core;

namespace TokenMojo;

/// <summary>The session a click is about: where it runs and the folder to resume or open.</summary>
internal sealed record SessionTarget(string Id, SessionStatus Status, string? Distro, string? User, string? Home, string? Folder)
{
    public static SessionTarget From(SessionRowModel row) => new(row.Id, row.Status, row.Distro, row.User, row.Home, row.Cwd);

    public static SessionTarget From(SessionSnapshot session) =>
        new(session.Id, session.Status, session.Distro, session.User, session.Home, session.ResumeFolder);
}

/// <summary>
/// Carries out what <see cref="SessionActions"/> decides for a session: focus its window, resume it, open its folder.
/// Failures are shown, never thrown. Anything that touches the WSL share runs off the UI thread.
/// </summary>
internal static class SessionControl
{
    // Sessions with a focus or resume under way, so a second click on the same one is ignored. UI thread only.
    private static readonly HashSet<string> InFlight = new();

    private const string Caption = "TokenMojo";
    private const string ConfirmResumeText =
        "This session looks like it's still running, but its window wasn't found. Resume in a new terminal anyway?";
    private const string UnavailableText =
        "This session's terminal window wasn't found. Only sessions started with tm can be focused, and only while their tab is the active one in its window. Resuming works for WSL sessions only.";
    private const string FocusRefusedText =
        "Windows did not allow switching to that window. Switch to it from the taskbar.";
    private const string WindowGoneText = "That session's terminal window is no longer open.";

    /// <summary>The click on a session row. True when a window was brought forward or a resume was started.</summary>
    public static Task<bool> ActivateAsync(SessionTarget target) => Guarded(target.Id, () => ActivateUnguardedAsync(target));

    public static Task<bool> ResumeAsync(SessionTarget target) => Guarded(target.Id, () => ResumeUnguardedAsync(target));

    private static async Task<bool> ActivateUnguardedAsync(SessionTarget target)
    {
        TerminalWindow? window = FindWindow(target.Id);
        switch (SessionActions.Decide(target.Status, target.Distro, windowFound: window is not null))
        {
            case SessionAction.Focus:
                return FocusWindow(window!);
            case SessionAction.ConfirmResume:
                return Confirm(ConfirmResumeText) && await ResumeUnguardedAsync(target);
            case SessionAction.Resume:
                return await ResumeUnguardedAsync(target);
            default:
                Warn(UnavailableText);
                return false;
        }
    }

    public static bool HasWindow(string sessionId) => FindWindow(sessionId) is not null;

    /// <summary>False when no window carries the tag, or Windows would not switch to it; either is shown.</summary>
    public static bool Focus(string sessionId)
    {
        if (FindWindow(sessionId) is { } window) return FocusWindow(window);
        Warn(WindowGoneText);
        return false;
    }

    /// <summary>True when the foreground window is a terminal carrying the session's tag.</summary>
    public static bool IsForeground(string sessionId) =>
        TerminalWindows.ForegroundTitle() is { } title && SessionWindows.Matches(title, sessionId);

    private static async Task<bool> ResumeUnguardedAsync(SessionTarget target)
    {
        if (target.Distro is not { } distro) return false;
        if (await Task.Run(() => TmProblem(distro, target.User ?? "", target.Home)) is { } problem)
        {
            Warn(problem);
            return false;
        }
        try
        {
            ResumeCommand command = await Task.Run(
                () => ResumeCommand.Build(distro, target.User, target.Id, target.Folder, Directory.Exists));
            TerminalProcess.Start(command);
            return true;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or ArgumentException)
        {
            Trace.TraceWarning("TokenMojo: cannot resume session {0}: {1}", target.Id, e.Message);
            Warn($"Could not open Windows Terminal to resume the session: {e.Message}");
            return false;
        }
    }

    public static async Task<bool> OpenFolderAsync(SessionTarget target)
    {
        if (target.Distro is not { } distro || target.Folder is not { } folder) return false;
        string path = WslPaths.SharePath(distro, folder);
        if (!await Task.Run(() => Directory.Exists(path)))
        {
            Warn($"The folder no longer exists: {folder}");
            return false;
        }
        try
        {
            ProcessStartInfo startInfo = new("explorer.exe") { UseShellExecute = false };
            startInfo.ArgumentList.Add(path);
            Process.Start(startInfo)?.Dispose();
            return true;
        }
        catch (Win32Exception e)
        {
            Trace.TraceWarning("TokenMojo: cannot open {0}: {1}", path, e.Message);
            Warn($"Could not open Explorer: {e.Message}");
            return false;
        }
    }

    public static void CopyId(string sessionId)
    {
        try
        {
            Clipboard.SetText(sessionId);
        }
        // Another program can hold the clipboard open for a moment.
        catch (COMException e)
        {
            Trace.TraceWarning("TokenMojo: cannot copy to the clipboard: {0}", e.Message);
            Warn($"Could not copy to the clipboard: {e.Message}");
        }
    }

    private static async Task<bool> Guarded(string sessionId, Func<Task<bool>> action)
    {
        if (!InFlight.Add(sessionId)) return false;
        try
        {
            return await action();
        }
        finally
        {
            InFlight.Remove(sessionId);
        }
    }

    /// <summary>Why `tm` must not be run for the user, or null when it is cctokmon's. Asks the distro, so off the UI thread.</summary>
    internal static string? TmProblem(string distro, string user, string? home)
    {
        // Without a known user or home folder there is nothing to ask about, so the launch is left to fail visibly in the terminal.
        if (user.Length == 0 || home is null) return null;
        return TmInstall.ProblemText(new WslTmInspector().Inspect(distro, user), user, distro, home);
    }

    private static bool FocusWindow(TerminalWindow window)
    {
        if (TerminalWindows.Focus(window.Handle)) return true;
        Warn(FocusRefusedText);
        return false;
    }

    private static TerminalWindow? FindWindow(string sessionId)
    {
        IReadOnlyList<TerminalWindow> windows = TerminalWindows.List();
        return SessionWindows.Find(windows.Select(window => window.Title).ToList(), sessionId) is { } index ? windows[index] : null;
    }

    private static bool Confirm(string text) =>
        MessageBox.Show(text, Caption, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    private static void Warn(string text) => MessageBox.Show(text, Caption, MessageBoxButton.OK, MessageBoxImage.Warning);
}
