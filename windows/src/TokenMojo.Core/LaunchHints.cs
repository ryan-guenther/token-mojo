using System.Text.RegularExpressions;

namespace TokenMojo.Core;

/// <param name="Folder">The Linux folder the link points at; null when it matches none of the configured ones.</param>
/// <param name="Worktree">The worktree name to suggest; null when the text holds no link.</param>
/// <param name="Description">What was detected, for the launcher's hint line.</param>
public sealed record LaunchHint(string? Folder, string? Worktree, string? Description)
{
    public static readonly LaunchHint None = new(null, null, null);

    /// <summary>Whether to tick the worktree box: only when the folder is known too, since a worktree needs the right repository.</summary>
    public bool UseWorktree => Worktree is not null && Folder is not null;
}

/// <summary>A git repository found on a distro: its folder as Linux sees it and the URL of its origin remote.</summary>
public sealed record RepoInfo(string Path, string? Origin);

/// <summary>Reads the first Linear issue or GitHub pull request link in a prompt and suggests where to work on it.</summary>
public static partial class LaunchHints
{
    public static LaunchHint From(string text, Settings settings, IReadOnlyList<RepoInfo> repos)
    {
        Match linear = LinearIssue().Match(text);
        Match pullRequest = GitHubPullRequest().Match(text);
        if (!linear.Success && !pullRequest.Success) return LaunchHint.None;
        // The first link in the text wins, whichever kind it is.
        return linear.Success && (!pullRequest.Success || linear.Index < pullRequest.Index)
            ? ForLinear(linear, settings)
            : ForPullRequest(pullRequest, repos);
    }

    private static LaunchHint ForLinear(Match issue, Settings settings)
    {
        string team = issue.Groups[1].Value.ToUpperInvariant();
        string number = issue.Groups[2].Value;
        string worktree = $"{team.ToLowerInvariant()}-{number}";
        settings.LinearTeams.TryGetValue(team, out string? folder);
        return new LaunchHint(folder, worktree, Describe($"Linear issue {team}-{number}", folder, worktree));
    }

    private static LaunchHint ForPullRequest(Match pullRequest, IReadOnlyList<RepoInfo> repos)
    {
        string repo = $"{pullRequest.Groups[1].Value}/{pullRequest.Groups[2].Value}";
        string number = pullRequest.Groups[3].Value;
        string worktree = $"pr-{number}";
        string? folder = repos.FirstOrDefault(candidate => SameRepository(candidate.Origin, repo))?.Path;
        return new LaunchHint(folder, worktree, Describe($"Pull request {repo}#{number}", folder, worktree));
    }

    private static string Describe(string source, string? folder, string worktree) =>
        $"{source}: {(folder is null ? "no matching folder" : $"folder {folder}")}, worktree {worktree}";

    private static bool SameRepository(string? origin, string ownerAndRepo)
    {
        if (origin is null || GitHubOrigin().Match(origin.Trim()) is not { Success: true } match) return false;
        return string.Equals($"{match.Groups[1].Value}/{match.Groups[2].Value}", ownerAndRepo, StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"https://linear\.app/[^/\s]+/issue/([A-Za-z][A-Za-z0-9]*)-(\d+)(?!\w)", RegexOptions.IgnoreCase)]
    private static partial Regex LinearIssue();

    [GeneratedRegex(@"https://github\.com/([^/\s]+)/([^/\s]+)/pull/(\d+)(?!\w)", RegexOptions.IgnoreCase)]
    private static partial Regex GitHubPullRequest();

    // https, ssh:// and scp-style remotes of github.com, with or without a user, ".git" or a trailing slash.
    [GeneratedRegex(@"\A(?:(?:https?|ssh)://(?:[^@/]+@)?github\.com/|[^@/:]+@github\.com:)([^/]+)/([^/]+?)(?:\.git)?/?\z", RegexOptions.IgnoreCase)]
    private static partial Regex GitHubOrigin();
}
