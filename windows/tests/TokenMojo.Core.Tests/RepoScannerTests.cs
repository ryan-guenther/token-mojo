using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public sealed class RepoScannerTests : IDisposable
{
    private const string OriginConfig = """
        [core]
        	repositoryformatversion = 0
        [remote "upstream"]
        	url = https://github.com/someone/else.git
        [remote "origin"]
        	url = git@github.com:octo/widgets.git
        	fetch = +refs/heads/*:refs/remotes/origin/*
        [branch "main"]
        	remote = origin
        """;

    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Scan_RepoUnderTheHomeFolder_IsFoundWithItsOrigin()
    {
        // Arrange
        MakeRepo("home/alice/widgets", OriginConfig);

        // Act
        IReadOnlyList<RepoInfo> repos = Scan(["~"]);

        // Assert
        Assert.Equal([new RepoInfo("/home/alice/widgets", "git@github.com:octo/widgets.git")], repos);
    }

    [Fact]
    public void Scan_RepoWithoutAnOriginRemote_IsFoundWithoutOne()
    {
        // Arrange
        MakeRepo("home/alice/scratch", "[core]\n\tbare = false\n[remote \"upstream\"]\n\turl = https://github.com/a/b.git\n");

        // Act
        IReadOnlyList<RepoInfo> repos = Scan(["~"]);

        // Assert
        Assert.Equal([new RepoInfo("/home/alice/scratch", null)], repos);
    }

    [Fact]
    public void Scan_RepoWithoutAConfigFile_IsStillFound()
    {
        // Arrange
        _temp.MakeDir("Ubuntu/home/alice/fresh/.git");

        // Act
        IReadOnlyList<RepoInfo> repos = Scan(["~"]);

        // Assert
        Assert.Equal([new RepoInfo("/home/alice/fresh", null)], repos);
    }

    [Fact]
    public void Scan_FolderThatIsNotARepo_IsIgnored()
    {
        // Arrange
        _temp.MakeDir("Ubuntu/home/alice/notes");
        MakeRepo("home/alice/widgets", OriginConfig);

        // Act
        IReadOnlyList<RepoInfo> repos = Scan(["~"]);

        // Assert
        Assert.Equal(["/home/alice/widgets"], repos.Select(repo => repo.Path));
    }

    [Fact]
    public void Scan_WorktreeOrSubmoduleWhoseGitIsAFile_IsNotARepoRoot()
    {
        // Arrange
        _temp.Write("Ubuntu/home/alice/linked/.git", "gitdir: /home/alice/widgets/.git/worktrees/linked", DateTimeOffset.UtcNow);
        MakeRepo("home/alice/widgets", OriginConfig);

        // Act
        IReadOnlyList<RepoInfo> repos = Scan(["~"]);

        // Assert
        Assert.Equal(["/home/alice/widgets"], repos.Select(repo => repo.Path));
    }

    [Fact]
    public void Scan_RepoNestedDeeperThanOneLevel_IsNotFound()
    {
        // Arrange
        MakeRepo("home/alice/code/widgets", OriginConfig);

        // Act
        IReadOnlyList<RepoInfo> repos = Scan(["~"]);

        // Assert
        Assert.Empty(repos);
    }

    [Fact]
    public void Scan_SeveralRoots_ListsEachRootsReposInOrderWithoutRepeats()
    {
        // Arrange
        MakeRepo("home/alice/zeta", OriginConfig);
        MakeRepo("home/alice/alpha", OriginConfig);
        MakeRepo("srv/code/beta", OriginConfig);

        // Act
        IReadOnlyList<RepoInfo> repos = Scan(["~", "/srv/code", "~/", "/home/alice"]);

        // Assert
        Assert.Equal(["/home/alice/alpha", "/home/alice/zeta", "/srv/code/beta"], repos.Select(repo => repo.Path));
    }

    [Fact]
    public void Scan_RootThatDoesNotExist_IsSkipped()
    {
        // Arrange
        MakeRepo("srv/code/beta", OriginConfig);

        // Act
        IReadOnlyList<RepoInfo> repos = Scan(["~/missing", "/srv/code"]);

        // Assert
        Assert.Equal(["/srv/code/beta"], repos.Select(repo => repo.Path));
    }

    [Fact]
    public void Scan_RepoWhoseConfigCannotBeRead_IsSkippedAndTheOthersAreKept()
    {
        // Arrange
        string unreadable = MakeRepo("home/alice/locked", OriginConfig) + "/config";
        MakeRepo("home/alice/widgets", OriginConfig);
        using IDisposable denied = TempDir.DenyRead(unreadable);

        // Act
        IReadOnlyList<RepoInfo> repos = Scan(["~"]);

        // Assert
        Assert.Equal(["/home/alice/widgets"], repos.Select(repo => repo.Path));
    }

    [Fact]
    public void Scan_RootThatCannotBeListed_IsSkippedAndTheOthersAreKept()
    {
        // Arrange
        MakeRepo("srv/code/beta", OriginConfig);
        string locked = _temp.MakeDir("Ubuntu/home/alice");
        using IDisposable denied = TempDir.DenyRead(locked);

        // Act
        IReadOnlyList<RepoInfo> repos = Scan(["~", "/srv/code"]);

        // Assert
        Assert.Contains(repos, repo => repo.Path == "/srv/code/beta");
    }

    [Fact]
    public void Scan_HomeRootWithoutAKnownUser_IsSkipped()
    {
        // Arrange
        MakeRepo("home/alice/widgets", OriginConfig);
        MakeRepo("srv/code/beta", OriginConfig);

        // Act
        IReadOnlyList<RepoInfo> repos = RepoScanner.Scan("Ubuntu", null, ["~", "/srv/code"], _temp.Path);

        // Assert
        Assert.Equal(["/srv/code/beta"], repos.Select(repo => repo.Path));
    }

    [Fact]
    public void Scan_DistroIsNotThere_ReturnsNothingInsteadOfThrowing()
    {
        // Act
        IReadOnlyList<RepoInfo> repos = RepoScanner.Scan("Gone", "/home/alice", ["~"], _temp.Path);

        // Assert
        Assert.Empty(repos);
    }

    private IReadOnlyList<RepoInfo> Scan(IReadOnlyList<string> roots) => RepoScanner.Scan("Ubuntu", "/home/alice", roots, _temp.Path);

    private string MakeRepo(string relativePath, string config)
    {
        string git = _temp.MakeDir($"Ubuntu/{relativePath}/.git");
        File.WriteAllText(Path.Combine(git, "config"), config.Replace("\r\n", "\n"));
        return git;
    }
}
