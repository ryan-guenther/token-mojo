using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using TokenMojo.Core;

namespace TokenMojo;

/// <summary>Carries out what <see cref="SessionActions"/> decides for a session: focus its window, resume it, open its folder. Failures are shown, never thrown.</summary>
internal static class SessionControl
{
    private const string Caption = "TokenMojo";
    private const string ConfirmResumeText =
        "This session looks like it's still running, but its window wasn't found. Resume in a new terminal anyway?";
    private const string UnavailableText =
        "This session's terminal window wasn't found. Only sessions started with tm can be focused, and only while their tab is the active one in its window. Resuming works for WSL sessions only.";

    /// <summary>The click on a session row. True when a window was brought forward or a resume was started.</summary>
    public static bool Activate(string sessionId, SessionStatus status, string? distro, string? cwd)
    {
        TerminalWindow? window = FindWindow(sessionId);
        switch (SessionActions.Decide(status, distro, windowFound: window is not null))
        {
            case SessionAction.Focus:
                return TerminalWindows.Focus(window!.Handle);
            case SessionAction.ConfirmResume:
                return Confirm(ConfirmResumeText) && Resume(sessionId, distro!, cwd);
            case SessionAction.Resume:
                return Resume(sessionId, distro!, cwd);
            default:
                Warn(UnavailableText);
                return false;
        }
    }

    public static bool HasWindow(string sessionId) => FindWindow(sessionId) is not null;

    /// <summary>False when no window carries the tag, or Windows would not switch to it.</summary>
    public static bool Focus(string sessionId) =>
        FindWindow(sessionId) is { } window && TerminalWindows.Focus(window.Handle);

    public static bool IsForeground(string sessionId) => SessionWindows.Matches(TerminalWindows.ForegroundTitle(), sessionId);

    public static bool Resume(string sessionId, string distro, string? cwd)
    {
        try
        {
            ResumeCommand command = ResumeCommand.Build(distro, sessionId, cwd, Directory.Exists);
            ProcessStartInfo startInfo = new(command.FileName) { UseShellExecute = false };
            foreach (string argument in command.Arguments)
                startInfo.ArgumentList.Add(argument);
            Process.Start(startInfo)?.Dispose();
            return true;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or ArgumentException)
        {
            Trace.TraceWarning("TokenMojo: cannot resume session {0}: {1}", sessionId, e.Message);
            Warn($"Could not open Windows Terminal to resume the session: {e.Message}");
            return false;
        }
    }

    public static bool OpenFolder(string distro, string cwd)
    {
        string path = WslPaths.SharePath(distro, cwd);
        if (!Directory.Exists(path))
        {
            Warn($"The folder no longer exists: {cwd}");
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

    private static TerminalWindow? FindWindow(string sessionId)
    {
        IReadOnlyList<TerminalWindow> windows = TerminalWindows.List();
        return SessionWindows.Find(windows.Select(window => window.Title).ToList(), sessionId) is { } index ? windows[index] : null;
    }

    private static bool Confirm(string text) =>
        MessageBox.Show(text, Caption, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;

    private static void Warn(string text) => MessageBox.Show(text, Caption, MessageBoxButton.OK, MessageBoxImage.Warning);
}
