using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public sealed class ClaudeDirLocatorTests : IDisposable
{
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Locate_NoWslRunning_ReturnsOnlyWindowsHome()
    {
        // Arrange
        string home = _temp.MakeDir("winhome");
        ClaudeDirLocator locator = new(home, new FakeWsl(), new FakeAccounts(), NoEnv);

        // Act / Assert
        Assert.Equal(
            [new ClaudeSource("Windows",
                Path.Combine(home, ".claude", "cctokmon-state.json"),
                Path.Combine(home, ".claude", "cctokmon-sessions"))],
            locator.Locate());
    }

    [Fact]
    public void Locate_EnvOverrides_ReplaceWindowsDefaults()
    {
        // Arrange
        Dictionary<string, string> env = new()
        {
            ["CCTOKMON_STATE_FILE"] = "/custom/state.json",
            ["CCTOKMON_SESSIONS_DIR"] = "/custom/sessions",
        };
        ClaudeDirLocator locator = new(_temp.MakeDir("winhome"), new FakeWsl(), new FakeAccounts(), env.GetValueOrDefault);

        // Act / Assert
        Assert.Equal([new ClaudeSource("Windows", "/custom/state.json", "/custom/sessions")], locator.Locate());
    }

    [Fact]
    public void Locate_RunningDistro_AddsEveryHomeThatHasAClaudeDir()
    {
        // Arrange
        string share = _temp.MakeDir("share");
        string alice = _temp.MakeDir("share/Ubuntu/home/alice/.claude");
        _temp.MakeDir("share/Ubuntu/home/bob");
        string root = _temp.MakeDir("share/Ubuntu/root/.claude");
        ClaudeDirLocator locator = new(_temp.MakeDir("winhome"), new FakeWsl("Ubuntu"), new FakeAccounts(), NoEnv, share);

        // Act
        List<ClaudeSource> wslSources = locator.Locate().Skip(1).ToList();

        // Assert
        Assert.Equal(
            [
                new ClaudeSource("Ubuntu:alice", Path.Combine(alice, "cctokmon-state.json"), Path.Combine(alice, "cctokmon-sessions"), "Ubuntu", "alice", "/home/alice"),
                new ClaudeSource("Ubuntu:root", Path.Combine(root, "cctokmon-state.json"), Path.Combine(root, "cctokmon-sessions"), "Ubuntu", "root", "/root"),
            ],
            wslSources);
    }

    [Fact]
    public void Locate_HomeFolderNamedDifferentlyFromTheAccount_UsesTheAccountAsTheUser()
    {
        // Arrange
        string share = _temp.MakeDir("share");
        string claude = _temp.MakeDir("share/Ubuntu/home/alice.smith/.claude");
        FakeAccounts accounts = new()
        {
            ["Ubuntu"] = new LinuxAccounts("alice", new Dictionary<string, string> { ["/home/alice.smith"] = "alice" }),
        };
        ClaudeDirLocator locator = new(_temp.MakeDir("winhome"), new FakeWsl("Ubuntu"), accounts, NoEnv, share);

        // Act
        ClaudeSource source = locator.Locate().Last();

        // Assert
        Assert.Equal(
            new ClaudeSource("Ubuntu:alice", Path.Combine(claude, "cctokmon-state.json"), Path.Combine(claude, "cctokmon-sessions"),
                "Ubuntu", "alice", "/home/alice.smith", IsDefaultUser: true),
            source);
    }

    [Fact]
    public void Locate_AccountOtherThanTheDefaultUser_IsNotMarkedDefault()
    {
        // Arrange
        string share = _temp.MakeDir("share");
        _temp.MakeDir("share/Ubuntu/home/bob/.claude");
        FakeAccounts accounts = new()
        {
            ["Ubuntu"] = new LinuxAccounts("alice", new Dictionary<string, string> { ["/home/bob"] = "bob" }),
        };
        ClaudeDirLocator locator = new(_temp.MakeDir("winhome"), new FakeWsl("Ubuntu"), accounts, NoEnv, share);

        // Act
        ClaudeSource source = locator.Locate().Last();

        // Assert
        Assert.False(source.IsDefaultUser);
    }

    [Fact]
    public void Locate_AccountsAreAskedOncePerDistro()
    {
        // Arrange
        string share = _temp.MakeDir("share");
        _temp.MakeDir("share/Ubuntu/home/alice/.claude");
        _temp.MakeDir("share/Ubuntu/home/bob/.claude");
        FakeAccounts accounts = new();
        ClaudeDirLocator locator = new(_temp.MakeDir("winhome"), new FakeWsl("Ubuntu"), accounts, NoEnv, share);

        // Act
        locator.Locate();

        // Assert
        Assert.Equal(["Ubuntu"], accounts.Asked);
    }

    [Fact]
    public void Locate_QueryFailsAfterOneSucceeded_ReusesTheLastAnswer()
    {
        // Arrange
        string share = _temp.MakeDir("share");
        _temp.MakeDir("share/Ubuntu/home/alice.smith/.claude");
        LinuxAccounts answer = new("alice", new Dictionary<string, string> { ["/home/alice.smith"] = "alice" });
        FlakyAccounts accounts = new(answer);
        ClaudeDirLocator locator = new(_temp.MakeDir("winhome"), new FakeWsl("Ubuntu"), accounts, NoEnv, share);
        locator.Locate();

        // Act
        ClaudeSource source = locator.Locate().Last();

        // Assert
        Assert.Equal(2, accounts.Calls);
        Assert.Equal(("alice", true), (source.User, source.IsDefaultUser));
    }

    [Fact]
    public void Locate_FirstQueryFailsThenSucceeds_UsesTheAnswerOnceItArrives()
    {
        // Arrange
        string share = _temp.MakeDir("share");
        _temp.MakeDir("share/Ubuntu/home/alice.smith/.claude");
        LinuxAccounts answer = new("alice", new Dictionary<string, string> { ["/home/alice.smith"] = "alice" });
        FlakyAccounts accounts = new(answer, failFirst: true);
        ClaudeDirLocator locator = new(_temp.MakeDir("winhome"), new FakeWsl("Ubuntu"), accounts, NoEnv, share);

        // Act
        string beforeAnswer = locator.Locate().Last().User!;
        string afterAnswer = locator.Locate().Last().User!;

        // Assert
        Assert.Equal(("alice.smith", "alice"), (beforeAnswer, afterAnswer));
    }

    [Fact]
    public void Locate_HomeFolderNamedRootUnderHome_IsNotRootsHome()
    {
        // Arrange
        string share = _temp.MakeDir("share");
        _temp.MakeDir("share/Ubuntu/home/root/.claude");
        _temp.MakeDir("share/Ubuntu/root/.claude");
        ClaudeDirLocator locator = new(_temp.MakeDir("winhome"), new FakeWsl("Ubuntu"), new FakeAccounts(), NoEnv, share);

        // Act
        List<ClaudeSource> sources = locator.Locate().Skip(1).ToList();

        // Assert
        Assert.Equal(["/home/root", "/root"], sources.Select(source => source.Home));
    }

    [Fact]
    public void Locate_DistroCannotBeAsked_FallsBackToTheFolderNameWithoutADefaultUser()
    {
        // Arrange
        string share = _temp.MakeDir("share");
        _temp.MakeDir("share/Ubuntu/home/alice/.claude");
        ClaudeDirLocator locator = new(_temp.MakeDir("winhome"), new FakeWsl("Ubuntu"), new FakeAccounts(), NoEnv, share);

        // Act
        ClaudeSource source = locator.Locate().Last();

        // Assert
        Assert.Equal(("alice", "/home/alice", false), (source.User, source.Home, source.IsDefaultUser));
    }

    [Fact]
    public void Locate_HomeFolderMissingFromTheAccountList_FallsBackToTheFolderName()
    {
        // Arrange
        string share = _temp.MakeDir("share");
        _temp.MakeDir("share/Ubuntu/home/stray/.claude");
        FakeAccounts accounts = new() { ["Ubuntu"] = new LinuxAccounts("alice", new Dictionary<string, string>()) };
        ClaudeDirLocator locator = new(_temp.MakeDir("winhome"), new FakeWsl("Ubuntu"), accounts, NoEnv, share);

        // Act
        ClaudeSource source = locator.Locate().Last();

        // Assert
        Assert.Equal("stray", source.User);
    }

    [Fact]
    public void Locate_DistroShareUnreachable_IsSkipped()
    {
        // Arrange
        ClaudeDirLocator locator = new(_temp.MakeDir("winhome"), new FakeWsl("Gone"), new FakeAccounts(), NoEnv, _temp.MakeDir("share"));

        // Act / Assert
        Assert.Single(locator.Locate());
    }

    [Fact]
    public void Locate_UnreadableDistroHome_SkipsThatDistroOnly()
    {
        // Arrange
        string share = _temp.MakeDir("share");
        string blocked = _temp.MakeDir("share/Broken/home");
        _temp.MakeDir("share/Ubuntu/home/alice/.claude");
        ClaudeDirLocator locator = new(_temp.MakeDir("winhome"), new FakeWsl("Broken", "Ubuntu"), new FakeAccounts(), NoEnv, share);
        using IDisposable denied = TempDir.DenyRead(blocked);

        // Act
        IReadOnlyList<ClaudeSource> sources = locator.Locate();

        // Assert
        Assert.Equal(["Windows", "Ubuntu:alice"], sources.Select(s => s.Name));
    }

    private static string? NoEnv(string name) => null;

    private sealed class FakeWsl(params string[] running) : IWslDistros
    {
        public IReadOnlyList<string> Running() => running;
    }

    // Answers once and then fails, or fails once and then answers.
    private sealed class FlakyAccounts(LinuxAccounts answer, bool failFirst = false) : ILinuxAccounts
    {
        public int Calls { get; private set; }

        public LinuxAccounts? Query(string distro)
        {
            Calls++;
            return (Calls == 1) == failFirst ? null : answer;
        }
    }

    private sealed class FakeAccounts : Dictionary<string, LinuxAccounts?>, ILinuxAccounts
    {
        public List<string> Asked { get; } = new();

        public LinuxAccounts? Query(string distro)
        {
            Asked.Add(distro);
            return TryGetValue(distro, out LinuxAccounts? found) ? found : null;
        }
    }
}
