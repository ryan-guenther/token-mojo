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
        DashboardModel model = Dashboard.Build(new LoadedState(BridgeState.Parse("{}"), Now.AddSeconds(-3), Source), [], Now, WindowsHome, new Settings());

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
        Assert.Equal(10, list.OpenRows.Count + list.ClosedRows.Count);
        Assert.Equal("12 · $12.00 total", list.Summary);
    }

    [Fact]
    public void Build_SessionRowsSetting_ListsThatManyRows()
    {
        // Arrange
        List<SessionSnapshot> sessions = Enumerable.Range(0, 25).Select(i => Session($"s{i}", 1, minutesAgo: i)).ToList();

        // Act
        SessionListModel list = Build(BridgeState.Parse(Payloads.Full), sessions, new Settings { SessionRows = 20 }).Sessions!;

        // Assert
        Assert.Equal(20, list.OpenRows.Count + list.ClosedRows.Count);
        Assert.Equal("25 · $25.00 total", list.Summary);
    }

    [Theory]
    [InlineData(12, "Sessions · last 12h")]
    [InlineData(24, "Sessions · last 24h")]
    public void Build_SessionHoursSetting_NamesTheWindowInTheHeading(int hours, string expected)
    {
        // Arrange
        SessionSnapshot[] sessions = [Session("a", 1, minutesAgo: 1)];

        // Act
        SessionListModel list = Build(BridgeState.Parse(Payloads.Full), sessions, new Settings { SessionHours = hours }).Sessions!;

        // Assert
        Assert.Equal(expected, list.Heading);
    }

    [Fact]
    public void Build_SessionRow_DescribesFolderModelAgeAndContext()
    {
        // Arrange
        BridgeState state = BridgeState.Parse(Payloads.Full) with { SessionName = null };
        SessionSnapshot session = new("abc-123", state, Now.AddMinutes(-5));

        // Act
        SessionRowModel row = OnlyRow(Build(BridgeState.Parse(Payloads.Full), [session]));

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
        SessionRowModel row = OnlyRow(Build(BridgeState.Parse(Payloads.Full), [session]));

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
        SessionRowModel row = OnlyRow(Build(BridgeState.Parse(Payloads.Full), [session]));

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
        SessionRowModel row = OnlyRow(Build(BridgeState.Parse(Payloads.Full), [session]));

        // Assert
        Assert.Equal("/home/alice/proj/sub", row.Cwd);
    }

    [Fact]
    public void Build_SessionWithoutFolder_HasNoCwd()
    {
        // Arrange
        SessionSnapshot session = Session("abc-123", 1, minutesAgo: 1) with { Distro = "Ubuntu" };

        // Act
        SessionRowModel row = OnlyRow(Build(BridgeState.Parse(Payloads.Full), [session]));

        // Assert
        Assert.Null(row.Cwd);
    }

    [Fact]
    public void Build_OpenSessionsWithMixedStatuses_ListsNeedsInputThenReadyThenWorkingThenNoStatus()
    {
        // Arrange
        SessionSnapshot[] sessions =
        [
            Session("none", 1, minutesAgo: 2),
            Session("working", 1, minutesAgo: 3) with { Status = SessionStatus.Working },
            Session("ready", 1, minutesAgo: 4) with { Status = SessionStatus.Ready },
            Session("needs", 1, minutesAgo: 5) with { Status = SessionStatus.NeedsInput },
        ];

        // Act
        SessionListModel list = Build(BridgeState.Parse(Payloads.Full), sessions).Sessions!;

        // Assert
        Assert.Equal(["needs", "ready", "working", "none"], list.OpenRows.Select(r => r.Id));
    }

    [Fact]
    public void Build_EndedAndIdleSessions_ListsThemApartFromOpenOnes()
    {
        // Arrange
        SessionSnapshot[] sessions =
        [
            Session("ended", 1, minutesAgo: 1) with { Status = SessionStatus.Ended },
            Session("idle", 1, minutesAgo: 30),
            Session("active", 1, minutesAgo: 2),
            Session("ready", 1, minutesAgo: 40) with { Status = SessionStatus.Ready },
        ];

        // Act
        SessionListModel list = Build(BridgeState.Parse(Payloads.Full), sessions).Sessions!;

        // Assert
        Assert.Equal(["ready", "active"], list.OpenRows.Select(r => r.Id));
        Assert.Equal(["ended", "idle"], list.ClosedRows.Select(r => r.Id));
    }

    [Fact]
    public void Build_ClosedSessions_ListsTheMostRecentlyClosedFirst()
    {
        // Arrange
        SessionSnapshot[] sessions =
        [
            Session("closed-earlier", 1, minutesAgo: 5) with { Status = SessionStatus.Ended, StatusSince = Now.AddMinutes(-5) },
            Session("idle", 1, minutesAgo: 20),
            Session("left-idle-then-closed", 1, minutesAgo: 60) with { Status = SessionStatus.Ended, StatusSince = Now.AddMinutes(-1) },
        ];

        // Act
        SessionListModel list = Build(BridgeState.Parse(Payloads.Full), sessions).Sessions!;

        // Assert
        Assert.Equal(["left-idle-then-closed", "closed-earlier", "idle"], list.ClosedRows.Select(r => r.Id));
    }

    [Fact]
    public void Build_EndedSession_DatesTheRowByWhenItEnded()
    {
        // Arrange
        SessionSnapshot session = Session("a", 1, minutesAgo: 60) with { Status = SessionStatus.Ended, StatusSince = Now.AddMinutes(-2) };

        // Act
        SessionRowModel row = OnlyRow(Build(BridgeState.Parse(Payloads.Full), [session]));

        // Assert
        Assert.EndsWith("2 min. ago", row.Detail);
    }

    [Fact]
    public void Build_OpenSessionsFillingTheRows_ListsNoClosedOnes()
    {
        // Arrange
        List<SessionSnapshot> sessions = Enumerable.Range(0, 3)
            .Select(i => Session($"open{i}", 1, minutesAgo: i) with { Status = SessionStatus.Ready }).ToList();
        sessions.Add(Session("ended", 1, minutesAgo: 0) with { Status = SessionStatus.Ended, StatusSince = Now });

        // Act
        SessionListModel list = Build(BridgeState.Parse(Payloads.Full), sessions, new Settings { SessionRows = 3 }).Sessions!;

        // Assert
        Assert.Equal(3, list.OpenRows.Count);
        Assert.Empty(list.ClosedRows);
    }

    [Fact]
    public void Build_FewerOpenSessionsThanRows_FillsTheRestWithClosedOnes()
    {
        // Arrange
        List<SessionSnapshot> sessions = Enumerable.Range(0, 3)
            .Select(i => Session($"ended{i}", 1, minutesAgo: i) with { Status = SessionStatus.Ended }).ToList();
        sessions.Add(Session("open", 1, minutesAgo: 0) with { Status = SessionStatus.Working });

        // Act
        SessionListModel list = Build(BridgeState.Parse(Payloads.Full), sessions, new Settings { SessionRows = 3 }).Sessions!;

        // Assert
        Assert.Equal(["open"], list.OpenRows.Select(r => r.Id));
        Assert.Equal(["ended0", "ended1"], list.ClosedRows.Select(r => r.Id));
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
        Assert.Equal(["newer", "older"], list.OpenRows.Select(r => r.Id));
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
        Assert.Equal("needs", list.OpenRows[0].Id);
        Assert.Equal(10, list.OpenRows.Count + list.ClosedRows.Count);
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
        SessionRowModel row = OnlyRow(Build(BridgeState.Parse(Payloads.Full), [session]));

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
        SessionRowModel row = OnlyRow(Build(BridgeState.Parse(Payloads.Full), [session]));

        // Assert
        Assert.Equal(expected, row.StatusText);
    }

    [Fact]
    public void Build_NeedsInputSessionWithSubagents_KeepsThePlainWording()
    {
        // Arrange
        SessionSnapshot session = Session("a", 1, minutesAgo: 1) with { Status = SessionStatus.NeedsInput, Subagents = 2 };

        // Act
        SessionRowModel row = OnlyRow(Build(BridgeState.Parse(Payloads.Full), [session]));

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
        SessionRowModel row = OnlyRow(Build(BridgeState.Parse(Payloads.Full), [session]));

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
        SessionRowModel row = OnlyRow(Build(BridgeState.Parse(Payloads.Full), [session]));

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
        SessionRowModel row = OnlyRow(Build(BridgeState.Parse(Payloads.Full), [session]));

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

    private static DashboardModel Build(BridgeState state, IReadOnlyList<SessionSnapshot>? sessions = null, Settings? settings = null) =>
        Dashboard.Build(new LoadedState(state, Now, Source), sessions ?? [], Now, WindowsHome, settings ?? new Settings());

    private static SessionRowModel OnlyRow(DashboardModel model) =>
        Assert.Single(model.Sessions!.OpenRows.Concat(model.Sessions.ClosedRows));

    private static BridgeState Subscriber(double fiveHour, double sevenDay) =>
        BridgeState.Parse(Payloads.Full) with
        {
            RateLimits = new BridgeRateLimits(new BridgeLimit(fiveHour, Future), new BridgeLimit(sevenDay, Future)),
        };

    private static SessionSnapshot Session(string id, double cost, int minutesAgo) =>
        new(id, BridgeState.Parse(Payloads.ForSession(id, cost)), Now.AddMinutes(-minutesAgo));
}
