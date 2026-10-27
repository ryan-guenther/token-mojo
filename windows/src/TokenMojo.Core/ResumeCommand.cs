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
        RequireSessionId(sessionId);
        string folder = Folders(cwd, LinuxFolder.RepositoryRoot)
            .FirstOrDefault(candidate => directoryExists(WslPaths.SharePath(distro, candidate, wslShareRoot)))
            ?? LinuxFolder.Home;
        // tm by its install path: a login shell does not see a PATH that only .bashrc sets. The tilde is left
        // for bash to expand and there are no quotes, so wt.exe and wsl.exe have nothing to unescape.
        return new ResumeCommand("wt.exe", NewWindowArguments(distro, user, folder, TmResume(sessionId)));
    }

    /// <summary>The same for a session that ran on Windows itself, with `tm` run by Git Bash.</summary>
    /// <param name="bash">Git's bin\bash.exe.</param>
    /// <param name="cwd">The session's Windows folder, which may be a worktree that has since been removed.</param>
    /// <param name="home">The user profile, where the window opens when neither the folder nor its repository is left.</param>
    public static ResumeCommand BuildNative(string bash, string sessionId, string? cwd, Func<string, bool> directoryExists, string home)
    {
        RequireSessionId(sessionId);
        string folder = Folders(cwd, WindowsFolder.RepositoryRoot)
            .Where(candidate => WindowsFolder.IsValid(candidate) && !candidate.StartsWith(WindowsFolder.Home, StringComparison.Ordinal))
            .FirstOrDefault(directoryExists)
            ?? home;
        return new ResumeCommand("wt.exe", NativeWindowArguments(bash, folder, TmResume(sessionId)));
    }

    private static string TmResume(string sessionId) => $"~/.local/bin/tm --resume {sessionId}";

    private static void RequireSessionId(string sessionId)
    {
        if (!SessionUuid().IsMatch(sessionId))
            throw new ArgumentException($"Not a session id: '{sessionId}'", nameof(sessionId));
    }

    // The folder itself, then the repository root when it was a worktree, which `claude -w` creates under it.
    private static IEnumerable<string> Folders(string? cwd, Func<string, string> repositoryRoot)
    {
        if (string.IsNullOrEmpty(cwd)) yield break;
        yield return cwd;
        string root = repositoryRoot(cwd);
        if (root != cwd) yield return root;
    }

    [GeneratedRegex(@"\A[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\z")]
    private static partial Regex SessionUuid();
}
