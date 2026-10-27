using System.Globalization;
using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public sealed class DashboardTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly double Future = Now.AddHours(2).ToUnixTimeSeconds();
    private static readonly ClaudeSource Source = new("Windows", "state.json", "sessions");
    private const string WindowsHome = @"C:\Users\alice";
    private readonly CultureInfo _previousCulture = CultureInfo.CurrentCulture;

    public DashboardTests() => CultureInfo.CurrentCulture = new CultureInfo("en-US");

    public void Dispose() => CultureInfo.CurrentCulture = _previousCulture;

    [Fact]
    public void Build_SubscriberState_ShowsFiveHourAndSevenDayRings()
    {
        // Arrange
        BridgeState state = Subscriber(fiveHour: 23.5, sevenDay: 41.2);

        // Act
        IReadOnlyList<LimitRingModel> limits = Build(state).Limits;

        // Assert
        Assert.Equal(["5-hour limit", "7-day limit"], limits.Select(l => l.Title));
        Assert.Equal(["23%", "41%"], limits.Select(l => l.PercentText));
    }

    [Fact]
    public void Build_ApiKeyState_ShowsNoRings()
    {
        // Arrange
        BridgeState state = BridgeState.Parse(Payloads.Full) with { RateLimits = null };

        // Act
        DashboardModel model = Build(state);

        // Assert
        Assert.Empty(model.Limits);
    }

    [Fact]
    public void Build_LimitOverOneHundred_CapsRingButShowsRealPercent()
    {
        // Act
        LimitRingModel ring = Build(Subscriber(fiveHour: 120, sevenDay: 10)).Limits[0];

        // Assert
        Assert.Equal(100, ring.RingPercent);
        Assert.Equal("120%", ring.PercentText);
        Assert.Equal(Severity.Critical, ring.Severity);
    }

    [Fact]
    public void Build_ExpiredLimit_ShowsEmptyRingAwaitingUpdate()
    {
        // Arrange
        BridgeState state = Subscriber(fiveHour: 90, sevenDay: 10) with
        {
            RateLimits = new BridgeRateLimits(new BridgeLimit(90, Now.AddMinutes(-1).ToUnixTimeSeconds()), null),
        };

        // Act
        LimitRingModel ring = Build(state).Limits[0];

        // Assert
        Assert.Equal(new LimitRingModel("5-hour limit", 0, "–", Severity.Ok, "Reset — awaiting update"), ring);
    }

    [Theory]
    [InlineData(200000L, "8% of 200k")]
    [InlineData(1000000L, "8% of 1M")]
    [InlineData(null, "8% of ?")]
    public void Build_ContextWindow_DescribesUsageAndSize(long? size, string expected)
    {
        // Arrange
        BridgeState state = BridgeState.Parse(Payloads.Full) with { ContextWindow = new BridgeContextWindow(0, 0, size, 8) };

        // Act / Assert
        Assert.Equal(expected, Build(state).Context?.Text);
    }

    [Fact]
    public void Build_NoContextWindow_HidesContextCard()
    {
        // Act
        DashboardModel model = Build(BridgeState.Parse("{}"));

        // Assert
        Assert.Null(model.Context);
    }

    [Fact]
    public void Build_FullPayload_FormatsStatTiles()
    {
        // Act
        DashboardModel model = Build(BridgeState.Parse(Payloads.Full));

        // Assert
        Assert.Equal("$1.2345", model.CostText);
        Assert.Equal("+10 / −2", model.LinesText);
        Assert.Equal("1,000", model.InputTokensText);
        Assert.Equal("200", model.OutputTokensText);
    }

    [Fact]
    public void Build_EmptyPayload_FallsBackToClaudeCodeAndZeros()
    {
        // Act
        DashboardModel model = Build(BridgeState.Parse("{}"));

        // Assert
        Assert.Equal("Claude Code", model.ModelName);
        Assert.Null(model.SessionName);
        Assert.Null(model.Directory);
        Assert.Equal("$0.0000", model.CostText);
        Assert.Equal("+0 / −0", model.LinesText);
    }

    [Theory]
    [InlineData(@"C:\Users\alice\proj", @"~\proj")]
    [InlineData(@"C:\Users\alicesmith\proj", @"C:\Users\alicesmith\proj")]
    [InlineData("/home/bob/proj", "~/proj")]
    [InlineData("/root/proj", "~/proj")]
    [InlineData("/srv/proj", "/srv/proj")]
    public void Build_Directory_AbbreviatesHomeWithTilde(string directory, string expected)
    {
        // Arrange
        BridgeState state = BridgeState.Parse("{}") with { Workspace = new BridgeWorkspace(directory, null) };

        // Act / Assert
        Assert.Equal(expected, Build(state).Directory);
    }

    [Fact]
    public void Build_StateWrittenSecondsAgo_SaysWhenUpdated()
    {
        // Act
        DashboardModel model = Dashboard.Build(new LoadedState(BridgeState.Parse("{}"), Now.AddSeconds(-3), Source), [], Now, WindowsHome);

        // Assert
        Assert.Equal("Updated 3 seconds ago", model.UpdatedText);
    }

    [Fact]
    public void Build_NoRecentSessions_HidesSessionList()
    {
        // Act
        DashboardModel model = Build(BridgeState.Parse(Payloads.Full));

        // Assert
        Assert.Null(model.Sessions);
    }

    [Fact]
    public void Build_RecentSessions_SummarisesCountAndTotalCost()
    {
        // Arrange
        SessionSnapshot[] sessions = [Session("a", 1.25, minutesAgo: 1), Session("b", 2.25, minutesAgo: 30)];

        // Act / Assert
        Assert.Equal("2 · $3.50 total", Build(BridgeState.Parse(Payloads.Full), sessions).Sessions?.Summary);
    }

    [Fact]
    public void Build_MoreThanTenSessions_ListsOnlyTheFirstTen()
    {
        // Arrange
        List<SessionSnapshot> sessions = Enumerable.Range(0, 12).Select(i => Session($"s{i}", 1, minutesAgo: i)).ToList();

        // Act
        SessionListModel list = Build(BridgeState.Parse(Payloads.Full), sessions).Sessions!;

        // Assert
        Assert.Equal(10, list.Rows.Count);
        Assert.Equal("12 · $12.00 total", list.Summary);
    }

    [Fact]
    public void Build_SessionRow_DescribesFolderModelAgeAndContext()
    {
        // Arrange
        BridgeState state = BridgeState.Parse(Payloads.Full) with { SessionName = null };
        SessionSnapshot session = new("abc-123", state, Now.AddMinutes(-5));

        // Act
        SessionRowModel row = Build(BridgeState.Parse(Payloads.Full), [session]).Sessions!.Rows[0];

        // Assert
        Assert.Equal(
            new SessionRowModel("abc-123", "proj", "proj · Opus · 5 min. ago", "$1.23", 8, "8%", Severity.Ok,
                SessionStatus.None, "Active in the last 10 minutes", IsActive: true, IsCurrent: true,
                Distro: null, Cwd: "/home/alice/proj", User: null),
            row);
    }

    [Fact]
    public void Build_SessionOnAWslSource_CarriesDistroUserAndFolder()
    {
        // Arrange
        SessionSnapshot session = new("abc-123", BridgeState.Parse(Payloads.Full), Now, Distro: "Ubuntu", User: "alice");

        // Act
        SessionRowModel row = Build(BridgeState.Parse(Payloads.Full), [session]).Sessions!.Rows[0];

        // Assert
        Assert.Equal(("Ubuntu", "alice", "/home/alice/proj"), (row.Distro, row.User, row.Cwd));
    }

    [Fact]
    public void Build_SessionStartedInAnotherFolder_ResumesAndOpensTheProjectFolder()
    {
        // Arrange
        BridgeState state = BridgeState.Parse(Payloads.Full) with
        {
            Workspace = new BridgeWorkspace("/home/alice/proj/sub", "/home/alice/proj"),
        };
        SessionSnapshot session = new("abc-123", state, Now, Distro: "Ubuntu");

        // Act
        SessionRowModel row = Build(BridgeState.Parse(Payloads.Full), [session]).Sessions!.Rows[0];

        // Assert
        Assert.Equal("/home/alice/proj", row.Cwd);
    }

    [Fact]
    public void Build_SessionWithoutProjectFolder_UsesTheCurrentFolder()
    {
        // Arrange
        BridgeState state = BridgeState.Parse(Payloads.Full) with { Workspace = new BridgeWorkspace("/home/alice/proj/sub", null) };
        SessionSnapshot session = new("abc-123", state, Now, Distro: "Ubuntu");

        // Act
        SessionRowModel row = Build(BridgeState.Parse(Payloads.Full), [session]).Sessions!.Rows[0];

        // Assert
        Assert.Equal("/home/alice/proj/sub", row.Cwd);
    }

    [Fact]
    public void Build_SessionWithoutFolder_HasNoCwd()
    {
        // Arrange
        SessionSnapshot session = Session("abc-123", 1, minutesAgo: 1) with { Distro = "Ubuntu" };

        // Act
        SessionRowModel row = Build(BridgeState.Parse(Payloads.Full), [session]).Sessions!.Rows[0];

        // Assert
        Assert.Null(row.Cwd);
    }

    [Fact]
    public void Build_SessionsWithMixedStatuses_ListsNeedsInputThenReadyThenWorkingThenNoStatusThenEnded()
    {
        // Arrange
        SessionSnapshot[] sessions =
        [
            Session("ended", 1, minutesAgo: 1) with { Status = SessionStatus.Ended },
            Session("none", 1, minutesAgo: 2),
            Session("working", 1, minutesAgo: 3) with { Status = SessionStatus.Working },
            Session("ready", 1, minutesAgo: 4) with { Status = SessionStatus.Ready },
            Session("needs", 1, minutesAgo: 5) with { Status = SessionStatus.NeedsInput },
        ];

        // Act
        SessionListModel list = Build(BridgeState.Parse(Payloads.Full), sessions).Sessions!;

        // Assert
        Assert.Equal(["needs", "ready", "working", "none", "ended"], list.Rows.Select(r => r.Id));
    }

    [Fact]
    public void Build_SessionsWithTheSameStatus_ListsNewestFirst()
    {
        // Arrange
        SessionSnapshot[] sessions =
        [
            Session("older", 1, minutesAgo: 30) with { Status = SessionStatus.Ready },
            Session("newer", 1, minutesAgo: 2) with { Status = SessionStatus.Ready },
        ];

        // Act
        SessionListModel list = Build(BridgeState.Parse(Payloads.Full), sessions).Sessions!;

        // Assert
        Assert.Equal(["newer", "older"], list.Rows.Select(r => r.Id));
    }

    [Fact]
    public void Build_MoreThanTenSessions_KeepsTheOnesNeedingAttention()
    {
        // Arrange
        List<SessionSnapshot> sessions = Enumerable.Range(0, 11).Select(i => Session($"s{i}", 1, minutesAgo: i)).ToList();
        sessions.Add(Session("needs", 1, minutesAgo: 600) with { Status = SessionStatus.NeedsInput });

        // Act
        SessionListModel list = Build(BridgeState.Parse(Payloads.Full), sessions).Sessions!;

        // Assert
        Assert.Equal("needs", list.Rows[0].Id);
        Assert.Equal(10, list.Rows.Count);
    }

    [Theory]
    [InlineData(SessionStatus.NeedsInput, "Needs input")]
    [InlineData(SessionStatus.Ready, "Ready")]
    [InlineData(SessionStatus.Working, "Working")]
    [InlineData(SessionStatus.Ended, "Ended")]
    public void Build_SessionWithHookStatus_DescribesItInTheRow(SessionStatus status, string expected)
    {
        // Arrange
        SessionSnapshot session = Session("a", 1, minutesAgo: 1) with { Status = status };

        // Act
        SessionRowModel row = Build(BridgeState.Parse(Payloads.Full), [session]).Sessions!.Rows[0];

        // Assert
        Assert.Equal(status, row.Status);
        Assert.Equal(expected, row.StatusText);
    }

    [Theory]
    [InlineData(1, "Working: 1 subagent")]
    [InlineData(2, "Working: 2 subagents")]
    public void Build_WorkingSessionWithSubagents_SaysHowManyInTheRow(int subagents, string expected)
    {
        // Arrange
        SessionSnapshot session = Session("a", 1, minutesAgo: 1) with { Status = SessionStatus.Working, Subagents = subagents };

        // Act
        SessionRowModel row = Build(BridgeState.Parse(Payloads.Full), [session]).Sessions!.Rows[0];

        // Assert
        Assert.Equal(expected, row.StatusText);
    }

    [Fact]
    public void Build_NeedsInputSessionWithSubagents_KeepsThePlainWording()
    {
        // Arrange
        SessionSnapshot session = Session("a", 1, minutesAgo: 1) with { Status = SessionStatus.NeedsInput, Subagents = 2 };

        // Act
        SessionRowModel row = Build(BridgeState.Parse(Payloads.Full), [session]).Sessions!.Rows[0];

        // Assert
        Assert.Equal("Needs input", row.StatusText);
    }

    [Theory]
    [InlineData(9, "Active in the last 10 minutes")]
    [InlineData(11, "Idle")]
    public void Build_SessionWithoutHooks_KeepsTheTenMinuteActivityWording(int minutesAgo, string expected)
    {
        // Arrange
        SessionSnapshot session = Session("a", 1, minutesAgo);

        // Act
        SessionRowModel row = Build(BridgeState.Parse(Payloads.Full), [session]).Sessions!.Rows[0];

        // Assert
        Assert.Equal(SessionStatus.None, row.Status);
        Assert.Equal(expected, row.StatusText);
    }

    [Fact]
    public void Build_RecentSessionWithHookStatus_IsNotMarkedActiveByTheTenMinuteRule()
    {
        // Arrange
        SessionSnapshot session = Session("a", 1, minutesAgo: 1) with { Status = SessionStatus.Ended };

        // Act
        SessionRowModel row = Build(BridgeState.Parse(Payloads.Full), [session]).Sessions!.Rows[0];

        // Assert
        Assert.False(row.IsActive);
    }

    [Fact]
    public void Build_Sessions_CountsNeedsInputAndReadyAsWaiting()
    {
        // Arrange
        SessionSnapshot[] sessions =
        [
            Session("needs", 1, minutesAgo: 1) with { Status = SessionStatus.NeedsInput },
            Session("ready", 1, minutesAgo: 2) with { Status = SessionStatus.Ready },
            Session("working", 1, minutesAgo: 3) with { Status = SessionStatus.Working },
            Session("ended", 1, minutesAgo: 4) with { Status = SessionStatus.Ended },
            Session("none", 1, minutesAgo: 5),
        ];

        // Act / Assert
        Assert.Equal(2, Build(BridgeState.Parse(Payloads.Full), sessions).WaitingCount);
    }

    [Fact]
    public void Build_NoSessions_HasNobodyWaiting()
    {
        // Act / Assert
        Assert.Equal(0, Build(BridgeState.Parse(Payloads.Full)).WaitingCount);
    }

    [Fact]
    public void Build_SessionWithoutNameOrFolder_IsTitledSession()
    {
        // Arrange
        SessionSnapshot session = new("x", BridgeState.Parse("{}"), Now.AddMinutes(-20));

        // Act
        SessionRowModel row = Build(BridgeState.Parse(Payloads.Full), [session]).Sessions!.Rows[0];

        // Assert
        Assert.Equal("Session", row.Title);
        Assert.Equal("20 min. ago", row.Detail);
        Assert.False(row.IsActive);
        Assert.False(row.IsCurrent);
    }

    [Theory]
    [InlineData(0, false, "now")]
    [InlineData(1, false, "1 second ago")]
    [InlineData(45, false, "45 seconds ago")]
    [InlineData(60, false, "1 minute ago")]
    [InlineData(7200, false, "2 hours ago")]
    [InlineData(172800, false, "2 days ago")]
    [InlineData(45, true, "45 sec. ago")]
    [InlineData(300, true, "5 min. ago")]
    [InlineData(3600, true, "1 hr. ago")]
    [InlineData(86400, true, "1 day ago")]
    public void RelativeAge_MatchesMacFormatterWording(int seconds, bool abbreviated, string expected)
    {
        // Act
        string age = Dashboard.RelativeAge(TimeSpan.FromSeconds(seconds), abbreviated);

        // Assert
        Assert.Equal(expected, age);
    }

    private static DashboardModel Build(BridgeState state, IReadOnlyList<SessionSnapshot>? sessions = null) =>
        Dashboard.Build(new LoadedState(state, Now, Source), sessions ?? [], Now, WindowsHome);

    private static BridgeState Subscriber(double fiveHour, double sevenDay) =>
        BridgeState.Parse(Payloads.Full) with
        {
            RateLimits = new BridgeRateLimits(new BridgeLimit(fiveHour, Future), new BridgeLimit(sevenDay, Future)),
        };

    private static SessionSnapshot Session(string id, double cost, int minutesAgo) =>
        new(id, BridgeState.Parse(Payloads.ForSession(id, cost)), Now.AddMinutes(-minutesAgo));
}
