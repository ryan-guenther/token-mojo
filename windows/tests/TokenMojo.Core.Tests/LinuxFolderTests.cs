using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public sealed class LinuxFolderTests
{
    [Theory]
    [InlineData("~", true)]
    [InlineData("~/my-app", true)]
    [InlineData("/srv/my app", true)]
    [InlineData("/", true)]
    [InlineData("", false)]
    [InlineData("my-app", false)]
    [InlineData("~other", false)]
    [InlineData("~other/x", false)]
    [InlineData(@"C:\work", false)]
    [InlineData("/home/a\nb", false)]
    [InlineData(null, false)]
    public void IsValid_FolderStartsWithHomeOrRoot_OnlyThenIsAccepted(string? folder, bool expected)
    {
        // Act / Assert
        Assert.Equal(expected, LinuxFolder.IsValid(folder));
    }

    [Theory]
    [InlineData("~", "alice", "/home/alice")]
    [InlineData("~/my-app", "alice", "/home/alice/my-app")]
    [InlineData("~/my-app", "root", "/root/my-app")]
    [InlineData("/srv/code", "alice", "/srv/code")]
    [InlineData("/srv/code", null, "/srv/code")]
    [InlineData("~/my-app", null, null)]
    public void Expand_TildeFolder_BecomesAnAbsolutePathForTheUser(string folder, string? user, string? expected)
    {
        // Act / Assert
        Assert.Equal(expected, LinuxFolder.Expand(folder, user));
    }

    [Theory]
    [InlineData("/home/alice", "alice", "~")]
    [InlineData("/home/alice/my-app", "alice", "~/my-app")]
    [InlineData("/home/alicia/my-app", "alice", "/home/alicia/my-app")]
    [InlineData("/root/my-app", "root", "~/my-app")]
    [InlineData("/srv/code", "alice", "/srv/code")]
    [InlineData("/home/alice/my-app", null, "/home/alice/my-app")]
    public void Abbreviate_FolderInTheUsersHome_UsesATilde(string folder, string? user, string expected)
    {
        // Act / Assert
        Assert.Equal(expected, LinuxFolder.Abbreviate(folder, user));
    }

    [Theory]
    [InlineData("/home/alice/my-app/.claude/worktrees/fix-login", "/home/alice/my-app")]
    [InlineData("/home/alice/my-app/.claude/worktrees/fix-login/src", "/home/alice/my-app")]
    [InlineData("/home/alice/my-app", "/home/alice/my-app")]
    [InlineData("~/my-app/.claude/worktrees/x", "~/my-app")]
    public void RepositoryRoot_FolderInAWorktree_IsTheRepositoryTheWorktreeBelongsTo(string folder, string expected)
    {
        // Act / Assert
        Assert.Equal(expected, LinuxFolder.RepositoryRoot(folder));
    }
}
