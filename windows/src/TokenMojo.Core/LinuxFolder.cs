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
        // A quote or backslash would be read by wt.exe's and wsl.exe's argument parsing before it reaches the shell.
        && !folder.Any(character => char.IsControl(character) || character is '"' or '\\');

    /// <summary>The absolute path of a folder; null when it starts with a tilde and there is no known home folder.</summary>
    public static string? Expand(string folder, string? homeFolder)
    {
        if (folder != Home && !folder.StartsWith("~/", StringComparison.Ordinal)) return folder;
        return homeFolder is null ? null : homeFolder + folder[Home.Length..];
    }

    /// <summary>The same folder written with a tilde when it is in the home folder.</summary>
    public static string Abbreviate(string folder, string? homeFolder)
    {
        if (homeFolder is null) return folder;
        if (folder == homeFolder) return Home;
        return folder.StartsWith(homeFolder + "/", StringComparison.Ordinal) ? Home + folder[homeFolder.Length..] : folder;
    }

    /// <summary>The repository root for a folder inside a worktree that `claude -w` made; other folders as they are.</summary>
    public static string RepositoryRoot(string folder)
    {
        int marker = folder.IndexOf(WorktreeMarker, StringComparison.Ordinal);
        return marker > 0 ? folder[..marker] : folder;
    }
}
