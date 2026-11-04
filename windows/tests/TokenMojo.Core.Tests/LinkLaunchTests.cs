using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public sealed class LinkLaunchTests
{
    private static readonly LaunchLink Review = new("review", new PullRequestRef("octo", "widgets", 12));

    private static readonly Settings Configured = new()
    {
        Actions = new Dictionary<string, string> { ["review"] = "Review {url}", ["merge"] = "Merge main into {url}, then check {url} again" },
        AllowedOwners = ["octo"],
    };

    private static readonly IReadOnlyList<RepoInfo> Repos =
    [
        new("/home/alice/gadgets", [new GitRemote("origin", "https://github.com/octo/gadgets.git")]),
        new("/home/alice/widgets", [new GitRemote("origin", "git@github.com:octo/widgets.git")]),
    ];

    [Fact]
    public void Plan_AllowedOwnerAndKnownAction_StartsInTheClonedRepoWithAWorktreeAndTheTemplatePrompt()
    {
        // Act
        LinkLaunchResult result = LinkLaunch.Plan(Review, Configured, Repos, onWindows: false);

        // Assert
        Assert.Null(result.Problem);
        Assert.Equal(new LinkLaunchPlan("/home/alice/widgets", "pr-12", "Review https://github.com/octo/widgets/pull/12"), result.Plan);
    }

    [Fact]
    public void Plan_TemplateWithSeveralPlaceholders_ReplacesEveryOne()
    {
        // Arrange
        LaunchLink merge = Review with { Action = "merge" };

        // Act
        LinkLaunchResult result = LinkLaunch.Plan(merge, Configured, Repos, onWindows: false);

        // Assert
        Assert.Equal(
            "Merge main into https://github.com/octo/widgets/pull/12, then check https://github.com/octo/widgets/pull/12 again",
            result.Plan!.Prompt);
    }

    [Fact]
    public void Plan_OwnerInADifferentCase_IsAllowed()
    {
        // Arrange
        LaunchLink link = Review with { PullRequest = new PullRequestRef("Octo", "widgets", 12) };

        // Act
        LinkLaunchResult result = LinkLaunch.Plan(link, Configured, Repos, onWindows: false);

        // Assert
        Assert.Null(result.Problem);
        Assert.Equal("Review https://github.com/Octo/widgets/pull/12", result.Plan!.Prompt);
    }

    [Fact]
    public void Plan_OwnerNotAllowed_IsRefusedNamingTheOwnerAndTheSetting()
    {
        // Arrange
        LaunchLink link = Review with { PullRequest = new PullRequestRef("other", "widgets", 12) };

        // Act
        LinkLaunchResult result = LinkLaunch.Plan(link, Configured, Repos, onWindows: false);

        // Assert
        Assert.Null(result.Plan);
        Assert.Equal("Links cannot start sessions for other's pull requests: other is not in allowedOwners in settings.json.", result.Problem);
    }

    [Fact]
    public void Plan_NoAllowedOwners_RefusesEveryLink()
    {
        // Arrange
        Settings settings = Configured with { AllowedOwners = [] };

        // Act
        LinkLaunchResult result = LinkLaunch.Plan(Review, settings, Repos, onWindows: false);

        // Assert
        Assert.Equal("Links cannot start sessions for octo's pull requests: octo is not in allowedOwners in settings.json.", result.Problem);
    }

    [Fact]
    public void Plan_UnknownAction_IsRefusedNamingTheAction()
    {
        // Arrange
        LaunchLink link = Review with { Action = "deploy" };

        // Act
        LinkLaunchResult result = LinkLaunch.Plan(link, Configured, Repos, onWindows: false);

        // Assert
        Assert.Null(result.Plan);
        Assert.Equal("There is no action named deploy in actions in settings.json.", result.Problem);
    }

    [Theory]
    [InlineData(false, "No repository for octo/sprockets under repoRoots.")]
    [InlineData(true, "No repository for octo/sprockets under windowsRepoRoots.")]
    public void Plan_NoClonedRepo_IsRefusedNamingTheRootsOfTheTarget(bool onWindows, string expected)
    {
        // Arrange
        LaunchLink link = Review with { PullRequest = new PullRequestRef("octo", "sprockets", 3) };

        // Act
        LinkLaunchResult result = LinkLaunch.Plan(link, Configured, Repos, onWindows);

        // Assert
        Assert.Null(result.Plan);
        Assert.Equal(expected, result.Problem);
    }

    [Fact]
    public void Plan_OnWindows_UsesTheWindowsRepoFolder()
    {
        // Arrange
        IReadOnlyList<RepoInfo> repos = [new(@"C:\code\widgets", [new GitRemote("origin", "https://github.com/octo/widgets")])];

        // Act
        LinkLaunchResult result = LinkLaunch.Plan(Review, Configured, repos, onWindows: true);

        // Assert
        Assert.Equal(@"C:\code\widgets", result.Plan!.Folder);
    }

    [Fact]
    public void Plan_ForkWithTheRepoAsUpstream_UsesItWhenNoCloneHasItAsOrigin()
    {
        // Arrange
        IReadOnlyList<RepoInfo> repos =
        [
            new("/home/alice/widgets-fork",
                [new GitRemote("origin", "https://github.com/alice/widgets"), new GitRemote("upstream", "https://github.com/octo/widgets")]),
        ];

        // Act
        LinkLaunchResult result = LinkLaunch.Plan(Review, Configured, repos, onWindows: false);

        // Assert
        Assert.Equal("/home/alice/widgets-fork", result.Plan!.Folder);
    }
}
