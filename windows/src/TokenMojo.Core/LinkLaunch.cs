namespace TokenMojo.Core;

/// <param name="Folder">The repository to start in, Linux or Windows like the repositories it was found among.</param>
/// <param name="Worktree">The worktree name for `claude -w`.</param>
/// <param name="Prompt">The action's template with the pull request's URL filled in.</param>
public sealed record LinkLaunchPlan(string Folder, string Worktree, string Prompt);

/// <param name="Plan">What to start; null when <paramref name="Problem"/> is set.</param>
/// <param name="Problem">Why the link does not start a session.</param>
public sealed record LinkLaunchResult(LinkLaunchPlan? Plan, string? Problem);

/// <summary>Decides what a launch link starts: only for an allowed owner, with a configured action, in a cloned repository.</summary>
public static class LinkLaunch
{
    public const string UrlPlaceholder = "{url}";

    /// <param name="repos">The repositories under the target's roots: repoRoots in WSL, windowsRepoRoots on Windows.</param>
    public static LinkLaunchResult Plan(LaunchLink link, Settings settings, IReadOnlyList<RepoInfo> repos, bool onWindows)
    {
        PullRequestRef pullRequest = link.PullRequest;
        if (!settings.AllowedOwners.Contains(pullRequest.Owner, StringComparer.OrdinalIgnoreCase))
            return Refused($"Links cannot start sessions for {pullRequest.Owner}'s pull requests: "
                + $"{pullRequest.Owner} is not in allowedOwners in settings.json.");
        if (!settings.Actions.TryGetValue(link.Action, out string? template))
            return Refused($"There is no action named {link.Action} in actions in settings.json.");
        if (LaunchHints.RepositoryFolder(repos, pullRequest.Owner, pullRequest.Repo) is not { } folder)
            return Refused($"No repository for {pullRequest.Owner}/{pullRequest.Repo} under {(onWindows ? "windowsRepoRoots" : "repoRoots")}.");
        return new LinkLaunchResult(
            new LinkLaunchPlan(folder, $"pr-{pullRequest.Number}", template.Replace(UrlPlaceholder, pullRequest.Url, StringComparison.Ordinal)),
            null);
    }

    private static LinkLaunchResult Refused(string problem) => new(null, problem);
}
