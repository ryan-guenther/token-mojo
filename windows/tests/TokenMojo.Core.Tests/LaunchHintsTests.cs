using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public sealed class LaunchHintsTests
{
    private static readonly Settings Mapped = new()
    {
        LinearTeams = new Dictionary<string, string> { ["ENG"] = "~/my-app" },
    };

    private static readonly IReadOnlyList<RepoInfo> Repos =
    [
        new("/home/alice/gadgets", "https://github.com/octo/gadgets.git"),
        new("/home/alice/widgets", "git@github.com:octo/widgets.git"),
    ];

    [Fact]
    public void From_PlainText_HasNoHint()
    {
        // Act
        LaunchHint hint = LaunchHints.From("Fix the flaky login test", Mapped, Repos);

        // Assert
        Assert.Equal(LaunchHint.None, hint);
        Assert.False(hint.UseWorktree);
    }

    [Theory]
    [InlineData("Work on https://linear.app/acme/issue/ENG-42")]
    [InlineData("Work on https://linear.app/acme/issue/ENG-42/fix-the-login-test and report")]
    [InlineData("(https://linear.app/acme/issue/ENG-42/fix-login)")]
    [InlineData("https://linear.app/acme/issue/eng-42")]
    public void From_LinearIssueUrl_NamesTheWorktreeAfterTheIssueAndUsesTheTeamsFolder(string text)
    {
        // Act
        LaunchHint hint = LaunchHints.From(text, Mapped, Repos);

        // Assert
        Assert.Equal("eng-42", hint.Worktree);
        Assert.Equal("~/my-app", hint.Folder);
        Assert.True(hint.UseWorktree);
    }

    [Fact]
    public void From_LinearTeamWithoutAFolder_StillNamesTheWorktree()
    {
        // Act
        LaunchHint hint = LaunchHints.From("https://linear.app/acme/issue/OPS-7", Mapped, Repos);

        // Assert
        Assert.Equal("ops-7", hint.Worktree);
        Assert.Null(hint.Folder);
    }

    [Theory]
    [InlineData("https://linear.app/acme/issues/ENG-42")]
    [InlineData("https://linear.app/acme/issue/42")]
    [InlineData("https://linear.app/acme/project/ENG-42")]
    [InlineData("https://example.com/acme/issue/ENG-42")]
    public void From_UrlThatIsNotALinearIssue_HasNoHint(string text)
    {
        // Act / Assert
        Assert.Equal(LaunchHint.None, LaunchHints.From(text, Mapped, Repos));
    }

    [Theory]
    [InlineData("Review https://github.com/octo/widgets/pull/12")]
    [InlineData("Review https://github.com/octo/widgets/pull/12/files please")]
    [InlineData("Review https://github.com/Octo/Widgets/pull/12")]
    public void From_GitHubPullRequestUrl_UsesTheClonedRepoAndNamesTheWorktreeAfterThePullRequest(string text)
    {
        // Act
        LaunchHint hint = LaunchHints.From(text, Mapped, Repos);

        // Assert
        Assert.Equal("/home/alice/widgets", hint.Folder);
        Assert.Equal("pr-12", hint.Worktree);
        Assert.True(hint.UseWorktree);
    }

    [Theory]
    [InlineData("https://github.com/octo/widgets.git")]
    [InlineData("https://github.com/octo/widgets")]
    [InlineData("git@github.com:octo/widgets.git")]
    [InlineData("git@github.com:octo/widgets")]
    [InlineData("ssh://git@github.com/octo/widgets.git")]
    [InlineData("https://user@github.com/OCTO/Widgets.git")]
    [InlineData("https://github.com/octo/widgets/")]
    public void From_OriginInAnyCommonForm_MatchesThePullRequestsRepo(string origin)
    {
        // Arrange
        IReadOnlyList<RepoInfo> repos = [new("/home/alice/widgets", origin)];

        // Act
        LaunchHint hint = LaunchHints.From("https://github.com/octo/widgets/pull/3", Mapped, repos);

        // Assert
        Assert.Equal("/home/alice/widgets", hint.Folder);
    }

    [Theory]
    [InlineData("https://github.com/octo/widgets-extra.git")]
    [InlineData("https://github.com/other/widgets.git")]
    [InlineData("https://gitlab.com/octo/widgets.git")]
    [InlineData("https://example.com/github.com/octo/widgets.git")]
    [InlineData("")]
    [InlineData(null)]
    public void From_NoRepoHasThatOrigin_KeepsTheWorktreeNameWithoutAFolder(string? origin)
    {
        // Arrange
        IReadOnlyList<RepoInfo> repos = [new("/home/alice/widgets", origin)];

        // Act
        LaunchHint hint = LaunchHints.From("https://github.com/octo/widgets/pull/3", Mapped, repos);

        // Assert
        Assert.Null(hint.Folder);
        Assert.Equal("pr-3", hint.Worktree);
    }

    [Theory]
    [InlineData("https://github.com/octo/widgets/issues/3")]
    [InlineData("https://github.com/octo/widgets/pull/abc")]
    [InlineData("https://github.com/octo/widgets")]
    public void From_GitHubUrlThatIsNotAPullRequest_HasNoHint(string text)
    {
        // Act / Assert
        Assert.Equal(LaunchHint.None, LaunchHints.From(text, Mapped, Repos));
    }

    [Fact]
    public void From_SeveralUrls_UsesTheFirstOne()
    {
        // Arrange
        string text = "See https://github.com/octo/widgets/pull/9 and https://linear.app/acme/issue/ENG-42";

        // Act
        LaunchHint hint = LaunchHints.From(text, Mapped, Repos);

        // Assert
        Assert.Equal("pr-9", hint.Worktree);
    }

    [Fact]
    public void From_LinearUrlBeforeAPullRequestUrl_UsesTheLinearOne()
    {
        // Arrange
        string text = "https://linear.app/acme/issue/ENG-42 fixed by https://github.com/octo/widgets/pull/9";

        // Act
        LaunchHint hint = LaunchHints.From(text, Mapped, Repos);

        // Assert
        Assert.Equal("eng-42", hint.Worktree);
    }

    [Fact]
    public void From_TwoReposShareAnOrigin_UsesTheFirstListed()
    {
        // Arrange
        IReadOnlyList<RepoInfo> repos =
        [
            new("/home/alice/widgets", "https://github.com/octo/widgets.git"),
            new("/home/alice/widgets-copy", "https://github.com/octo/widgets.git"),
        ];

        // Act
        LaunchHint hint = LaunchHints.From("https://github.com/octo/widgets/pull/1", Mapped, repos);

        // Assert
        Assert.Equal("/home/alice/widgets", hint.Folder);
    }

    [Fact]
    public void Description_NamesWhatWasDetected()
    {
        // Act
        LaunchHint linear = LaunchHints.From("https://linear.app/acme/issue/ENG-42", Mapped, Repos);
        LaunchHint pullRequest = LaunchHints.From("https://github.com/octo/widgets/pull/12", Mapped, Repos);
        LaunchHint unmatched = LaunchHints.From("https://github.com/octo/other/pull/5", Mapped, Repos);

        // Assert
        Assert.Equal("Linear issue ENG-42: folder ~/my-app, worktree eng-42", linear.Description);
        Assert.Equal("Pull request octo/widgets#12: folder /home/alice/widgets, worktree pr-12", pullRequest.Description);
        Assert.Equal("Pull request octo/other#5: no matching folder, worktree pr-5", unmatched.Description);
    }
}
