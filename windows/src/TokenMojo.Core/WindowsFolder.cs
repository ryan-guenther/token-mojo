using System.Text.RegularExpressions;

namespace TokenMojo.Core;

/// <summary>
/// Folder names for sessions that run on Windows itself: a full path such as C:\code\app, or relative to the user
/// profile with a leading tilde (~, ~\app or ~/app).
/// </summary>
public static partial class WindowsFolder
{
    public const string Home = "~";
    private static readonly string[] WorktreeMarkers = [@"\.claude\worktrees\", "/.claude/worktrees/"];

    /// <summary>The profile folder, a path under it, or a full path with a drive or a UNC share.</summary>
    public static bool IsValid(string? folder) =>
        folder is not null
        && (folder == Home || folder.StartsWith(@"~\", StringComparison.Ordinal) || folder.StartsWith("~/", StringComparison.Ordinal)
            || FullPath().IsMatch(folder))
        // A quote would end the argument wt.exe reads the folder from.
        && !folder.Any(character => char.IsControl(character) || character is '"');

    /// <summary>The full path of a folder; a tilde is the user profile.</summary>
    public static string Expand(string folder, string home)
    {
        if (folder == Home) return home;
        if (folder.StartsWith(@"~\", StringComparison.Ordinal) || folder.StartsWith("~/", StringComparison.Ordinal))
            return home.TrimEnd('\\') + @"\" + folder[2..].Replace('/', '\\');
        return folder;
    }

    /// <summary>The same folder written with a tilde when it is in the user profile.</summary>
    public static string Abbreviate(string folder, string home)
    {
        string trimmedHome = home.TrimEnd('\\');
        if (string.Equals(folder.TrimEnd('\\'), trimmedHome, StringComparison.OrdinalIgnoreCase)) return Home;
        return folder.StartsWith(trimmedHome + @"\", StringComparison.OrdinalIgnoreCase) ? Home + folder[trimmedHome.Length..] : folder;
    }

    /// <summary>The repository root for a folder inside a worktree that `claude -w` made; other folders as they are.</summary>
    public static string RepositoryRoot(string folder)
    {
        foreach (string marker in WorktreeMarkers)
        {
            int at = folder.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (at > 0) return folder[..at];
        }
        return folder;
    }

    // Spelled out rather than Path.IsPathFullyQualified, which answers for the platform it runs on.
    [GeneratedRegex(@"\A(?:[A-Za-z]:[\\/]|\\\\[^\\/]+\\[^\\/]+)")]
    private static partial Regex FullPath();
}
