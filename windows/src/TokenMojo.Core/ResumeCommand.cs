using System.Text.RegularExpressions;

namespace TokenMojo.Core;

/// <summary>The wt.exe invocation that resumes a session in a new Windows Terminal window.</summary>
public sealed partial record ResumeCommand(string FileName, IReadOnlyList<string> Arguments) : TerminalCommand(FileName, Arguments)
{
    /// <param name="user">The Linux user the session belongs to; null runs as the distro's default user.</param>
    /// <param name="cwd">The session's folder as Linux sees it, which may be a worktree that has since been removed.</param>
    /// <param name="directoryExists">Asked about the folder's path on the distro share.</param>
    public static ResumeCommand Build(
        string distro, string? user, string sessionId, string? cwd, Func<string, bool> directoryExists, string wslShareRoot = WslPaths.DefaultShareRoot)
    {
        if (!SessionUuid().IsMatch(sessionId))
            throw new ArgumentException($"Not a session id: '{sessionId}'", nameof(sessionId));
        string folder = Folders(cwd)
            .FirstOrDefault(candidate => directoryExists(WslPaths.SharePath(distro, candidate, wslShareRoot)))
            ?? LinuxFolder.Home;
        // tm by its install path: a login shell does not see a PATH that only .bashrc sets. The tilde is left
        // for bash to expand and there are no quotes, so wt.exe and wsl.exe have nothing to unescape.
        return new ResumeCommand("wt.exe", NewWindowArguments(distro, user, folder, $"~/.local/bin/tm --resume {sessionId}"));
    }

    // The folder itself, then the repository root when it was a worktree, which `claude -w` creates under it.
    private static IEnumerable<string> Folders(string? cwd)
    {
        if (string.IsNullOrEmpty(cwd)) yield break;
        yield return cwd;
        string root = LinuxFolder.RepositoryRoot(cwd);
        if (root != cwd) yield return root;
    }

    [GeneratedRegex(@"\A[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\z")]
    private static partial Regex SessionUuid();
}
