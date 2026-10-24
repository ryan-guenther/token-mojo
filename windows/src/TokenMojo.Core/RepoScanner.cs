using System.Diagnostics;
using System.Text.RegularExpressions;

namespace TokenMojo.Core;

/// <summary>Finds the git repositories directly under the configured folders of a distro, through its share, or of Windows itself.</summary>
public static partial class RepoScanner
{
    /// <summary>Never throws: a root or repository that cannot be read is skipped, since the distro may stop mid-scan.</summary>
    /// <param name="home">The home folder a tilde root means; null skips the roots that start with a tilde.</param>
    public static IReadOnlyList<RepoInfo> Scan(
        string distro, string? home, IEnumerable<string> roots, string wslShareRoot = WslPaths.DefaultShareRoot)
    {
        List<RepoInfo> repos = new();
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (string root in roots)
        {
            if (LinuxFolder.Expand(root, home) is not { } folder) continue;
            string linuxRoot = folder.TrimEnd('/');
            string rootOnShare = Path.Combine(wslShareRoot, distro, linuxRoot.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            foreach (RepoInfo repo in ScanRoot(rootOnShare, linuxRoot, name => $"{linuxRoot}/{name}"))
            {
                if (seen.Add(repo.Path)) repos.Add(repo);
            }
        }
        return repos;
    }

    /// <summary>The same for Claude Code on Windows itself: the roots are Windows folders, read from the local disk.</summary>
    /// <param name="home">The user profile, which a tilde root means.</param>
    public static IReadOnlyList<RepoInfo> ScanWindows(string home, IEnumerable<string> roots)
    {
        List<RepoInfo> repos = new();
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (string root in roots)
        {
            string folder = WindowsFolder.Expand(root, home);
            string windowsRoot = folder.Length > 3 ? folder.TrimEnd('\\') : folder;
            foreach (RepoInfo repo in ScanRoot(windowsRoot, windowsRoot, name => Path.Combine(windowsRoot, name)))
            {
                if (seen.Add(repo.Path)) repos.Add(repo);
            }
        }
        return repos;
    }

    /// <param name="onDisk">The root as this process reads it.</param>
    /// <param name="root">The root as sessions see it, for messages.</param>
    /// <param name="pathOf">A repository's path as sessions see it, from its folder name.</param>
    private static List<RepoInfo> ScanRoot(string onDisk, string root, Func<string, string> pathOf)
    {
        List<RepoInfo> repos = new();
        try
        {
            foreach (string directory in Directory.EnumerateDirectories(onDisk).Order(StringComparer.Ordinal))
            {
                if (TryReadRepo(directory, pathOf) is { } repo) repos.Add(repo);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("TokenMojo: cannot scan {0}: {1}", root, e.Message);
        }
        return repos;
    }

    // A worktree or submodule has a .git file, not a directory, and is not a root to start sessions in.
    private static RepoInfo? TryReadRepo(string directory, Func<string, string> pathOf)
    {
        try
        {
            string git = Path.Combine(directory, ".git");
            if (!Directory.Exists(git)) return null;
            return new RepoInfo(pathOf(Path.GetFileName(directory)), ReadRemotes(Path.Combine(git, "config")));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("TokenMojo: cannot read repository {0}: {1}", directory, e.Message);
            return null;
        }
    }

    private static List<GitRemote> ReadRemotes(string configPath)
    {
        List<GitRemote> remotes = new();
        if (!File.Exists(configPath)) return remotes;
        string? remoteName = null;
        foreach (string line in File.ReadLines(configPath).Select(line => line.Trim()))
        {
            if (line.StartsWith('['))
                remoteName = RemoteSection().Match(line) is { Success: true } section ? section.Groups[1].Value : null;
            else if (remoteName is not null && UrlLine().Match(line) is { Success: true } url)
            {
                remotes.Add(new GitRemote(remoteName, url.Groups[1].Value));
                // A remote with several urls fetches from the first.
                remoteName = null;
            }
        }
        return remotes;
    }

    [GeneratedRegex(@"\A\[remote\s+""([^""]+)""\]\z")]
    private static partial Regex RemoteSection();

    [GeneratedRegex(@"\Aurl\s*=\s*(\S.*)\z")]
    private static partial Regex UrlLine();
}
