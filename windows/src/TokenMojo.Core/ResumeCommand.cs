using System.Text.RegularExpressions;

namespace TokenMojo.Core;

/// <summary>The wt.exe invocation that resumes a session in a new Windows Terminal window.</summary>
public sealed partial record ResumeCommand(string FileName, IReadOnlyList<string> Arguments)
{
    private const string WorktreeMarker = "/.claude/worktrees/";
    private const string HomeFolder = "~";

    /// <param name="cwd">The session's folder as Linux sees it, which may be a worktree that has since been removed.</param>
    /// <param name="directoryExists">Asked about the folder's path on the distro share.</param>
    public static ResumeCommand Build(
        string distro, string sessionId, string? cwd, Func<string, bool> directoryExists, string wslShareRoot = WslPaths.DefaultShareRoot)
    {
        if (!SafeSessionId().IsMatch(sessionId))
            throw new ArgumentException($"Not a session id: '{sessionId}'", nameof(sessionId));
        string folder = Folders(cwd)
            .FirstOrDefault(candidate => directoryExists(WslPaths.SharePath(distro, candidate, wslShareRoot)))
            ?? HomeFolder;
        string[] arguments =
        [
            "-w", "new", "wsl.exe", "-d", distro, "--cd", folder, "--", "bash", "-lc", $"tm --resume {sessionId}",
        ];
        return new ResumeCommand("wt.exe", arguments.Select(EscapeForWindowsTerminal).ToList());
    }

    // The folder itself, then the repository root when it was a worktree, which `claude -w` creates under it.
    private static IEnumerable<string> Folders(string? cwd)
    {
        if (string.IsNullOrEmpty(cwd)) yield break;
        yield return cwd;
        int marker = cwd.IndexOf(WorktreeMarker, StringComparison.Ordinal);
        if (marker > 0) yield return cwd[..marker];
    }

    // wt.exe reads an unescaped semicolon as the start of its next command.
    private static string EscapeForWindowsTerminal(string argument) => argument.Replace(";", @"\;");

    [GeneratedRegex("^[0-9A-Za-z-]+$")]
    private static partial Regex SafeSessionId();
}
