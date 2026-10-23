namespace TokenMojo.Core;

/// <summary>Folder names as Linux sees them: absolute, or relative to the user's home with a leading tilde.</summary>
public static class LinuxFolder
{
    public const string Home = "~";
    public const string WorktreeMarker = "/.claude/worktrees/";

    /// <summary>A folder wsl.exe can start in: the home folder, a path under it, or an absolute path.</summary>
    public static bool IsValid(string? folder) =>
        folder is not null
        && (folder == Home || folder.StartsWith("~/", StringComparison.Ordinal) || folder.StartsWith('/'))
        && !folder.Any(char.IsControl);

    /// <summary>The absolute path of a folder; null when it starts with a tilde and there is no known user.</summary>
    public static string? Expand(string folder, string? user)
    {
        if (folder != Home && !folder.StartsWith("~/", StringComparison.Ordinal)) return folder;
        return user is null ? null : WslPaths.HomeFolder(user) + folder[Home.Length..];
    }

    /// <summary>The same folder written with a tilde when it is in the user's home folder.</summary>
    public static string Abbreviate(string folder, string? user)
    {
        if (user is null) return folder;
        string home = WslPaths.HomeFolder(user);
        if (folder == home) return Home;
        return folder.StartsWith(home + "/", StringComparison.Ordinal) ? Home + folder[home.Length..] : folder;
    }

    /// <summary>The repository root for a folder inside a worktree that `claude -w` made; other folders as they are.</summary>
    public static string RepositoryRoot(string folder)
    {
        int marker = folder.IndexOf(WorktreeMarker, StringComparison.Ordinal);
        return marker > 0 ? folder[..marker] : folder;
    }
}
