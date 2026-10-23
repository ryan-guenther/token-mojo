using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public sealed class SessionStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private readonly TempDir _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void LoadSessions_SeveralRecentSessions_ReturnsNewestFirst()
    {
        // Arrange
        ClaudeSource source = _temp.Source("win");
        _temp.Write("win/cctokmon-sessions/older.json", Payloads.ForSession("older"), Now.AddHours(-2));
        _temp.Write("win/cctokmon-sessions/newer.json", Payloads.ForSession("newer"), Now.AddMinutes(-1));

        // Act
        IReadOnlyList<SessionSnapshot> sessions = SessionStore.LoadSessions([source], Now);

        // Assert
        Assert.Equal(["newer", "older"], sessions.Select(s => s.Id));
    }

    [Fact]
    public void LoadSessions_SessionOlderThanTwelveHours_IsLeftOut()
    {
        // Arrange
        ClaudeSource source = _temp.Source("win");
        _temp.Write("win/cctokmon-sessions/stale.json", Payloads.ForSession("stale"), Now.AddHours(-13));

        // Act / Assert
        Assert.Empty(SessionStore.LoadSessions([source], Now));
    }

    [Fact]
    public void LoadSessions_UndecodableAndNonJsonFiles_AreSkipped()
    {
        // Arrange
        ClaudeSource source = _temp.Source("win");
        _temp.Write("win/cctokmon-sessions/garbage.json", "not json", Now);
        _temp.Write("win/cctokmon-sessions/inflight.json.tmp", Payloads.ForSession("inflight"), Now);
        _temp.Write("win/cctokmon-sessions/good.json", Payloads.ForSession("good"), Now);

        // Act / Assert
        Assert.Equal(["good"], SessionStore.LoadSessions([source], Now).Select(s => s.Id));
    }

    [Fact]
    public void LoadSessions_SessionsDirNotCreatedYet_ReturnsEmpty()
    {
        // Act
        IReadOnlyList<SessionSnapshot> sessions = SessionStore.LoadSessions([_temp.Source("missing")], Now);

        // Assert
        Assert.Empty(sessions);
    }

    [Fact]
    public void LoadSessions_SessionsFromTwoSources_AreMerged()
    {
        // Arrange
        _temp.Write("win/cctokmon-sessions/a.json", Payloads.ForSession("a"), Now.AddMinutes(-5));
        _temp.Write("wsl/cctokmon-sessions/b.json", Payloads.ForSession("b"), Now.AddMinutes(-1));

        // Act
        IReadOnlyList<SessionSnapshot> sessions = SessionStore.LoadSessions([_temp.Source("win"), _temp.Source("wsl")], Now);

        // Assert
        Assert.Equal(["b", "a"], sessions.Select(s => s.Id));
    }

    [Fact]
    public void LoadSessions_SessionFromADistroSource_RecordsTheDistro()
    {
        // Arrange
        _temp.Write("win/cctokmon-sessions/a.json", Payloads.ForSession("a"), Now.AddMinutes(-5));
        _temp.Write("wsl/cctokmon-sessions/b.json", Payloads.ForSession("b"), Now.AddMinutes(-1));
        ClaudeSource distro = _temp.Source("wsl") with { Distro = "Ubuntu", User = "alice" };

        // Act
        IReadOnlyList<SessionSnapshot> sessions = SessionStore.LoadSessions([_temp.Source("win"), distro], Now);

        // Assert
        Assert.Equal([("b", "Ubuntu", "alice"), ("a", null, null)], sessions.Select(s => (s.Id, s.Distro, s.User)));
    }

    [Fact]
    public void LoadSessions_UnreadableSourceDir_KeepsOtherSources()
    {
        // Arrange
        _temp.Write("win/cctokmon-sessions/a.json", Payloads.ForSession("a"), Now);
        _temp.Write("wsl/cctokmon-sessions/b.json", Payloads.ForSession("b"), Now);
        using IDisposable denied = TempDir.DenyRead(Path.Combine(_temp.Path, "wsl", "cctokmon-sessions"));

        // Act
        IReadOnlyList<SessionSnapshot> sessions = SessionStore.LoadSessions([_temp.Source("win"), _temp.Source("wsl")], Now);

        // Assert
        Assert.Equal(["a"], sessions.Select(s => s.Id));
    }

    [Fact]
    public void LoadSessions_UnreadableSessionFile_IsSkipped()
    {
        // Arrange
        string locked = _temp.Write("win/cctokmon-sessions/locked.json", Payloads.ForSession("locked"), Now);
        _temp.Write("win/cctokmon-sessions/good.json", Payloads.ForSession("good"), Now);
        using IDisposable denied = TempDir.DenyRead(locked);

        // Act
        IReadOnlyList<SessionSnapshot> sessions = SessionStore.LoadSessions([_temp.Source("win")], Now);

        // Assert
        Assert.Equal(["good"], sessions.Select(s => s.Id));
    }

    [Fact]
    public void LoadSessions_EventsFileBesideSession_AttachesStatusAndTimes()
    {
        // Arrange
        ClaudeSource source = _temp.Source("win");
        _temp.Write("win/cctokmon-sessions/a.json", Payloads.ForSession("a"), Now);
        _temp.Write("win/cctokmon-sessions/a.events", string.Join("\n",
            """{"t":1000,"e":"SessionStart"}""",
            """{"t":1010,"e":"UserPromptSubmit"}""",
            """{"t":1020,"e":"Notification","n":"permission_prompt"}""") + "\n", Now);

        // Act
        SessionSnapshot session = SessionStore.LoadSessions([source], Now).Single();

        // Assert
        Assert.Equal(SessionStatus.NeedsInput, session.Status);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1020), session.StatusSince);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1010), session.TurnStarted);
    }

    [Fact]
    public void LoadSessions_SubagentStillRunningAfterTheMainStop_IsWorkingWithItsCountUntilItExpires()
    {
        // Arrange
        ClaudeSource source = _temp.Source("win");
        long start = Now.AddMinutes(-20).ToUnixTimeSeconds();
        _temp.Write("win/cctokmon-sessions/a.json", Payloads.ForSession("a"), Now);
        _temp.Write("win/cctokmon-sessions/a.events", string.Join("\n",
            $$"""{"t":{{start}},"e":"UserPromptSubmit"}""",
            $$"""{"t":{{start + 600}},"e":"SubagentStart","a":"sub-1"}""",
            $$"""{"t":{{start + 660}},"e":"Stop"}""") + "\n", Now);

        // Act
        SessionSnapshot running = SessionStore.LoadSessions([source], Now.AddMinutes(-5)).Single();
        SessionSnapshot expired = SessionStore.LoadSessions([source], Now).Single();

        // Assert
        Assert.Equal(SessionStatus.Working, running.Status);
        Assert.Equal(1, running.Subagents);
        Assert.Equal(SessionStatus.Ready, expired.Status);
        Assert.Equal(0, expired.Subagents);
    }

    [Fact]
    public void LoadSessions_NoEventsFile_HasNoStatus()
    {
        // Arrange
        _temp.Write("win/cctokmon-sessions/a.json", Payloads.ForSession("a"), Now);

        // Act
        SessionSnapshot session = SessionStore.LoadSessions([_temp.Source("win")], Now).Single();

        // Assert
        Assert.Equal(SessionStatus.None, session.Status);
        Assert.Null(session.StatusSince);
        Assert.Null(session.TurnStarted);
    }

    [Fact]
    public void LoadSessions_EventsFileOfGarbage_StillListsTheSessionWithNoStatus()
    {
        // Arrange
        _temp.Write("win/cctokmon-sessions/a.json", Payloads.ForSession("a"), Now);
        _temp.Write("win/cctokmon-sessions/a.events", "not json\n{\"t\":\n", Now);

        // Act
        SessionSnapshot session = SessionStore.LoadSessions([_temp.Source("win")], Now).Single();

        // Assert
        Assert.Equal(SessionStatus.None, session.Status);
    }

    [Fact]
    public void LoadSessions_UnreadableEventsFile_SkipsTheSessionForThisRefresh()
    {
        // Arrange
        _temp.Write("win/cctokmon-sessions/a.json", Payloads.ForSession("a"), Now);
        string locked = _temp.Write("win/cctokmon-sessions/a.events", """{"t":1,"e":"SessionStart"}""" + "\n", Now);
        _temp.Write("win/cctokmon-sessions/b.json", Payloads.ForSession("b"), Now);
        using IDisposable denied = TempDir.DenyRead(locked);

        // Act
        IReadOnlyList<SessionSnapshot> sessions = SessionStore.LoadSessions([_temp.Source("win")], Now);

        // Assert
        Assert.Equal(["b"], sessions.Select(s => s.Id));
    }

    [Fact]
    public void LoadSessions_EventsFileWithoutSessionFile_IsNotListed()
    {
        // Arrange
        _temp.Write("win/cctokmon-sessions/orphan.events", """{"t":1,"e":"SessionStart"}""" + "\n", Now);

        // Act / Assert
        Assert.Empty(SessionStore.LoadSessions([_temp.Source("win")], Now));
    }

    [Fact]
    public void LoadSessions_SameSessionInTwoSources_KeepsOnlyTheNewestCopy()
    {
        // Arrange
        _temp.Write("win/cctokmon-sessions/a.json", Payloads.ForSession("a"), Now.AddMinutes(-5));
        _temp.Write("win/cctokmon-sessions/a.events", """{"t":1000,"e":"SessionStart"}""" + "\n", Now);
        _temp.Write("wsl/cctokmon-sessions/a.json", Payloads.ForSession("a"), Now.AddMinutes(-1));
        _temp.Write("wsl/cctokmon-sessions/a.events", """{"t":1000,"e":"UserPromptSubmit"}""" + "\n", Now.AddMinutes(-9));

        // Act
        SessionSnapshot session = SessionStore.LoadSessions([_temp.Source("win"), _temp.Source("wsl")], Now).Single();

        // Assert
        Assert.Equal(SessionStatus.Working, session.Status);
        Assert.Equal(Now.AddMinutes(-1), session.Updated);
    }

    [Fact]
    public void LoadSessions_SameSessionUpdatedAtTheSameTime_KeepsTheCopyWithTheNewerEventsFile()
    {
        // Arrange
        _temp.Write("win/cctokmon-sessions/a.json", Payloads.ForSession("a"), Now);
        _temp.Write("win/cctokmon-sessions/a.events", """{"t":1000,"e":"SessionStart"}""" + "\n", Now.AddMinutes(-10));
        _temp.Write("wsl/cctokmon-sessions/a.json", Payloads.ForSession("a"), Now);
        _temp.Write("wsl/cctokmon-sessions/a.events", """{"t":1000,"e":"UserPromptSubmit"}""" + "\n", Now);

        // Act
        SessionSnapshot session = SessionStore.LoadSessions([_temp.Source("win"), _temp.Source("wsl")], Now).Single();

        // Assert
        Assert.Equal(SessionStatus.Working, session.Status);
    }

    [Fact]
    public void LoadSessions_LongEventsFile_ReadsOnlyTheLastSixtyFourKilobytes()
    {
        // Arrange
        _temp.Write("win/cctokmon-sessions/a.json", Payloads.ForSession("a"), Now);
        string early = """{"t":1,"e":"SessionEnd"}""" + "\n";
        string filler = new string('x', 70_000) + "\n";
        string last = """{"t":9,"e":"Stop"}""" + "\n";
        _temp.Write("win/cctokmon-sessions/a.events", early + filler + last, Now);

        // Act
        SessionSnapshot session = SessionStore.LoadSessions([_temp.Source("win")], Now).Single();

        // Assert
        Assert.Equal(SessionStatus.Ready, session.Status);
    }

    [Fact]
    public void LoadSessions_ReadStartingMidLine_DropsThePartialFirstLine()
    {
        // Arrange: the 64 KB window starts exactly where a valid-looking SessionEnd line begins mid-line
        _temp.Write("win/cctokmon-sessions/a.json", Payloads.ForSession("a"), Now);
        string ended = """{"t":5,"e":"SessionEnd"}""" + "\n";
        string last = """{"t":9,"e":"Stop"}""" + "\n";
        string filler = new string('y', 65_536 - ended.Length - last.Length - 1) + "\n";
        _temp.Write("win/cctokmon-sessions/a.events", "GARBAGE" + ended + filler + last, Now);

        // Act
        SessionSnapshot session = SessionStore.LoadSessions([_temp.Source("win")], Now).Single();

        // Assert
        Assert.Equal(SessionStatus.Ready, session.Status);
    }

    [Fact]
    public void LoadCurrent_StateFilesInTwoSources_PicksMostRecentlyWritten()
    {
        // Arrange
        _temp.Write("win/cctokmon-state.json", Payloads.ForSession("from-windows"), Now.AddMinutes(-30));
        _temp.Write("wsl/cctokmon-state.json", Payloads.ForSession("from-wsl"), Now.AddMinutes(-1));

        // Act
        LoadedState? current = SessionStore.LoadCurrent([_temp.Source("win"), _temp.Source("wsl")]);

        // Assert
        Assert.Equal("from-wsl", current?.State.SessionId);
        Assert.Equal("wsl", current?.Source.Name);
        Assert.Equal(Now.AddMinutes(-1), current?.Updated);
    }

    [Fact]
    public void LoadCurrent_NoStateFileAnywhere_ReturnsNull()
    {
        // Act
        LoadedState? current = SessionStore.LoadCurrent([_temp.Source("win"), _temp.Source("wsl")]);

        // Assert
        Assert.Null(current);
    }

    [Theory]
    [InlineData(-9, true)]
    [InlineData(-11, false)]
    public void IsActive_UpdatedWithinTenMinutes_IsActive(int minutesAgo, bool expected)
    {
        // Arrange
        SessionSnapshot session = new("s", BridgeState.Parse("{}"), Now.AddMinutes(minutesAgo));

        // Act / Assert
        Assert.Equal(expected, session.IsActive(Now));
    }
}
