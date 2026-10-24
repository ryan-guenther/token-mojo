using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public sealed class FolderListTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_NoSessionsOrRepos_IsJustTheDefault()
    {
        // Act
        IReadOnlyList<string> folders = FolderList.Build("~/my-app", "Ubuntu", "/home/alice", [], []);

        // Assert
        Assert.Equal(["~/my-app"], folders);
    }

    [Fact]
    public void Build_DefaultThenRecentSessionFoldersNewestFirstThenRepos()
    {
        // Arrange
        SessionSnapshot[] sessions =
        [
            Session("a", "/home/alice/older", minutesAgo: 30),
            Session("b", "/home/alice/newer", minutesAgo: 5),
        ];
        RepoInfo[] repos = [new("/home/alice/widgets", []), new("/home/alice/gadgets", [])];

        // Act
        IReadOnlyList<string> folders = FolderList.Build("~", "Ubuntu", "/home/alice", sessions, repos);

        // Assert
        Assert.Equal(["~", "~/newer", "~/older", "~/gadgets", "~/widgets"], folders);
    }

    [Fact]
    public void Build_SameFolderInSeveralPlaces_AppearsOnceAtItsFirstPosition()
    {
        // Arrange
        SessionSnapshot[] sessions =
        [
            Session("a", "/home/alice/my-app", minutesAgo: 5),
            Session("b", "/home/alice/my-app", minutesAgo: 10),
        ];
        RepoInfo[] repos = [new("/home/alice/my-app", []), new("/home/alice/widgets", [])];

        // Act
        IReadOnlyList<string> folders = FolderList.Build("~/my-app", "Ubuntu", "/home/alice", sessions, repos);

        // Assert
        Assert.Equal(["~/my-app", "~/widgets"], folders);
    }

    [Fact]
    public void Build_SessionInAWorktree_OffersTheRepositoryRootInstead()
    {
        // Arrange
        SessionSnapshot[] sessions = [Session("a", "/home/alice/my-app/.claude/worktrees/eng-42", minutesAgo: 5)];

        // Act
        IReadOnlyList<string> folders = FolderList.Build("~", "Ubuntu", "/home/alice", sessions, []);

        // Assert
        Assert.Equal(["~", "~/my-app"], folders);
    }

    [Fact]
    public void Build_SessionsFromOtherDistrosOrTheWindowsProfile_AreLeftOut()
    {
        // Arrange
        SessionSnapshot[] sessions =
        [
            Session("a", "/home/alice/here", minutesAgo: 5, distro: "Ubuntu"),
            Session("b", "/home/alice/elsewhere", minutesAgo: 4, distro: "Debian"),
            Session("c", @"C:\Users\alice\code", minutesAgo: 3, distro: null),
            Session("d", null, minutesAgo: 2, distro: "Ubuntu"),
        ];

        // Act
        IReadOnlyList<string> folders = FolderList.Build("~", "Ubuntu", "/home/alice", sessions, []);

        // Assert
        Assert.Equal(["~", "~/here"], folders);
    }

    [Fact]
    public void Build_FoldersOutsideTheHome_KeepTheirAbsolutePath()
    {
        // Arrange
        SessionSnapshot[] sessions = [Session("a", "/srv/code/api", minutesAgo: 5)];
        RepoInfo[] repos = [new("/srv/code/web", [])];

        // Act
        IReadOnlyList<string> folders = FolderList.Build("~", "Ubuntu", "/home/alice", sessions, repos);

        // Assert
        Assert.Equal(["~", "/srv/code/api", "/srv/code/web"], folders);
    }

    [Fact]
    public void Build_NoKnownUser_KeepsAbsolutePathsAsTheyAre()
    {
        // Arrange
        RepoInfo[] repos = [new("/home/alice/widgets", [])];

        // Act
        IReadOnlyList<string> folders = FolderList.Build("~", "Ubuntu", null, [], repos);

        // Assert
        Assert.Equal(["~", "/home/alice/widgets"], folders);
    }

    [Fact]
    public void BuildWindows_DefaultThenWindowsSessionRepositoriesThenRepos_WithTheProfileAsTilde()
    {
        // Arrange
        SessionSnapshot[] sessions =
        [
            Session("a", @"C:\Users\ann\older", minutesAgo: 30, distro: null),
            Session("b", @"D:\code\app\.claude\worktrees\pr-12", minutesAgo: 5, distro: null),
            Session("c", "/home/alice/linux", minutesAgo: 1),
        ];
        RepoInfo[] repos = [new(@"D:\code\widgets", []), new(@"D:\code\APP", [])];

        // Act
        IReadOnlyList<string> folders = FolderList.BuildWindows("~", @"C:\Users\ann", sessions, repos);

        // Assert
        Assert.Equal(["~", @"D:\code\app", @"~\older", @"D:\code\widgets"], folders);
    }

    private static SessionSnapshot Session(string id, string? folder, int minutesAgo, string? distro = "Ubuntu")
    {
        BridgeState state = new(id, null, null, new BridgeWorkspace(folder, folder), null, null, null);
        return new SessionSnapshot(id, state, Now.AddMinutes(-minutesAgo), Distro: distro, User: "alice");
    }
}
