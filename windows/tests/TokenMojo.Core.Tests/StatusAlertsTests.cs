using TokenMojo.Core;
using Baseline = System.Collections.Generic.IReadOnlyDictionary<string, TokenMojo.Core.SeenStatus>;

namespace TokenMojo.Core.Tests;

public class StatusAlertsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan LongTurn = TimeSpan.FromSeconds(30);

    [Fact]
    public void Diff_NoBaselineYet_RaisesNothingBecauseTheFirstLoadIsOnlyABaseline()
    {
        // Arrange
        SessionSnapshot[] current = [Session("a", SessionStatus.NeedsInput)];

        // Act
        IReadOnlyList<StatusAlert> alerts = StatusAlerts.Diff(null, current, LongTurn);

        // Assert
        Assert.Empty(alerts);
    }

    [Fact]
    public void Diff_SessionEntersNeedsInput_RaisesNeedsInputAlert()
    {
        // Arrange
        Baseline baseline = BaselineOf(Session("a", SessionStatus.Working));
        SessionSnapshot[] current = [Session("a", SessionStatus.NeedsInput, sinceSeconds: 5)];

        // Act
        IReadOnlyList<StatusAlert> alerts = StatusAlerts.Diff(baseline, current, LongTurn);

        // Assert
        Assert.Equal([new StatusAlert("a", StatusAlertKind.NeedsInput, "my-session", "Needs input")], alerts);
    }

    [Fact]
    public void Diff_SessionStaysInTheSameNeedsInput_RaisesNothing()
    {
        // Arrange
        SessionSnapshot[] both = [Session("a", SessionStatus.NeedsInput)];

        // Act / Assert
        Assert.Empty(StatusAlerts.Diff(BaselineOf(both), both, LongTurn));
    }

    [Fact]
    public void Diff_NeedsInputAgainWithANewSince_RaisesAnotherAlert()
    {
        // Arrange
        Baseline baseline = BaselineOf(Session("a", SessionStatus.NeedsInput));
        SessionSnapshot[] current = [Session("a", SessionStatus.NeedsInput, sinceSeconds: 40)];

        // Act
        IReadOnlyList<StatusAlert> alerts = StatusAlerts.Diff(baseline, current, LongTurn);

        // Assert
        Assert.Equal(["a"], alerts.Select(a => a.SessionId));
    }

    [Fact]
    public void Diff_SessionFirstSeenAfterBaselineAlreadyNeedingInput_RaisesNeedsInputAlert()
    {
        // Arrange
        Baseline baseline = BaselineOf(Session("a", SessionStatus.Working));
        SessionSnapshot[] current = [Session("a", SessionStatus.Working), Session("b", SessionStatus.NeedsInput)];

        // Act
        IReadOnlyList<StatusAlert> alerts = StatusAlerts.Diff(baseline, current, LongTurn);

        // Assert
        Assert.Equal(["b"], alerts.Select(a => a.SessionId));
    }

    [Theory]
    [InlineData(45, true)]
    [InlineData(30, true)]
    [InlineData(29, false)]
    public void Diff_WorkingToReady_RaisesAlertOnlyWhenTurnWasLong(int turnSeconds, bool expected)
    {
        // Arrange
        Baseline baseline = BaselineOf(Session("a", SessionStatus.Working));
        SessionSnapshot[] current = [Session("a", SessionStatus.Ready, sinceSeconds: 100, turnSeconds: turnSeconds)];

        // Act
        IReadOnlyList<StatusAlert> alerts = StatusAlerts.Diff(baseline, current, LongTurn);

        // Assert
        Assert.Equal(expected, alerts.Count == 1);
    }

    [Fact]
    public void Diff_LongTurnFinishes_AlertNamesTheSessionAndSaysFinished()
    {
        // Arrange
        Baseline baseline = BaselineOf(Session("a", SessionStatus.Working));
        SessionSnapshot[] current = [Session("a", SessionStatus.Ready, sinceSeconds: 100, turnSeconds: 120)];

        // Act
        IReadOnlyList<StatusAlert> alerts = StatusAlerts.Diff(baseline, current, LongTurn);

        // Assert
        Assert.Equal([new StatusAlert("a", StatusAlertKind.TurnFinished, "my-session", "Finished")], alerts);
    }

    [Fact]
    public void Diff_NeedsInputToReadyAfterALongTurn_RaisesFinishedAlert()
    {
        // Arrange
        Baseline baseline = BaselineOf(Session("a", SessionStatus.NeedsInput));
        SessionSnapshot[] current = [Session("a", SessionStatus.Ready, sinceSeconds: 100, turnSeconds: 120)];

        // Act
        IReadOnlyList<StatusAlert> alerts = StatusAlerts.Diff(baseline, current, LongTurn);

        // Assert
        Assert.Equal([StatusAlertKind.TurnFinished], alerts.Select(a => a.Kind));
    }

    [Fact]
    public void Diff_ReadyAgainWithANewSinceAfterALongTurn_RaisesFinishedAlert()
    {
        // Arrange
        Baseline baseline = BaselineOf(Session("a", SessionStatus.Ready, sinceSeconds: 0, turnSeconds: 5));
        SessionSnapshot[] current = [Session("a", SessionStatus.Ready, sinceSeconds: 200, turnSeconds: 120)];

        // Act
        IReadOnlyList<StatusAlert> alerts = StatusAlerts.Diff(baseline, current, LongTurn);

        // Assert
        Assert.Equal([StatusAlertKind.TurnFinished], alerts.Select(a => a.Kind));
    }

    [Fact]
    public void Diff_ReadyWithoutTurnStart_RaisesNothing()
    {
        // Arrange
        Baseline baseline = BaselineOf(Session("a", SessionStatus.Working));
        SessionSnapshot[] current = [Session("a", SessionStatus.Ready, sinceSeconds: 100)];

        // Act / Assert
        Assert.Empty(StatusAlerts.Diff(baseline, current, LongTurn));
    }

    [Fact]
    public void Diff_SessionAlreadyReadyAfterALongTurn_RaisesNothing()
    {
        // Arrange
        SessionSnapshot[] both = [Session("a", SessionStatus.Ready, turnSeconds: 600)];

        // Act / Assert
        Assert.Empty(StatusAlerts.Diff(BaselineOf(both), both, LongTurn));
    }

    [Fact]
    public void Diff_SessionFirstSeenAfterBaselineAlreadyReady_RaisesNothing()
    {
        // Arrange
        Baseline baseline = BaselineOf(Session("a", SessionStatus.Working));
        SessionSnapshot[] current = [Session("b", SessionStatus.Ready, turnSeconds: 600)];

        // Act / Assert
        Assert.Empty(StatusAlerts.Diff(baseline, current, LongTurn));
    }

    [Fact]
    public void Diff_SessionMissingForARefreshThenFinishingALongTurn_StillRaisesFinishedAlert()
    {
        // Arrange
        Baseline afterFirst = BaselineOf(Session("a", SessionStatus.Working));
        Baseline afterGap = StatusAlerts.Advance(afterFirst, [], Now.AddSeconds(5), LongTurn);
        SessionSnapshot[] current = [Session("a", SessionStatus.Ready, sinceSeconds: 100, turnSeconds: 120)];

        // Act
        IReadOnlyList<StatusAlert> alerts = StatusAlerts.Diff(afterGap, current, LongTurn);

        // Assert
        Assert.Equal([StatusAlertKind.TurnFinished], alerts.Select(a => a.Kind));
    }

    [Fact]
    public void Diff_SessionMissingForARefreshStillNeedingInput_DoesNotAlertAgain()
    {
        // Arrange
        SessionSnapshot[] both = [Session("a", SessionStatus.NeedsInput)];
        Baseline afterGap = StatusAlerts.Advance(BaselineOf(both), [], Now.AddSeconds(5), LongTurn);

        // Act / Assert
        Assert.Empty(StatusAlerts.Diff(afterGap, both, LongTurn));
    }

    [Fact]
    public void Diff_UnnamedSession_IsTitledByItsFolder()
    {
        // Arrange
        Baseline baseline = BaselineOf(Session("a", SessionStatus.Working));
        SessionSnapshot unnamed = Session("a", SessionStatus.NeedsInput, sinceSeconds: 5);
        unnamed = unnamed with { State = unnamed.State with { SessionName = null } };

        // Act
        IReadOnlyList<StatusAlert> alerts = StatusAlerts.Diff(baseline, [unnamed], LongTurn);

        // Assert
        Assert.Equal("proj", alerts[0].Title);
    }

    [Fact]
    public void Advance_SessionNotSeenForTwelveHours_IsForgotten()
    {
        // Arrange
        Baseline baseline = BaselineOf(Session("old", SessionStatus.Working), Session("kept", SessionStatus.Working));
        baseline = StatusAlerts.Advance(baseline, [Session("kept", SessionStatus.Working)], Now.AddHours(11), LongTurn);

        // Act
        Baseline later = StatusAlerts.Advance(baseline, [Session("kept", SessionStatus.Working)], Now.AddHours(13), LongTurn);

        // Assert
        Assert.Equal(["kept"], later.Keys);
    }

    [Fact]
    public void Balloon_OneAlert_UsesItsTitleAndMessage()
    {
        // Act
        AlertBalloon balloon = StatusAlerts.Balloon([new StatusAlert("a", StatusAlertKind.NeedsInput, "api", "Needs input")]);

        // Assert
        Assert.Equal(new AlertBalloon("api", "Needs input"), balloon);
    }

    [Fact]
    public void Balloon_SeveralNeedsInputAlerts_SaysTheSessionsNeedYou()
    {
        // Arrange
        StatusAlert[] alerts =
        [
            new("a", StatusAlertKind.NeedsInput, "api", "Needs input"),
            new("b", StatusAlertKind.NeedsInput, "web", "Needs input"),
        ];

        // Act
        AlertBalloon balloon = StatusAlerts.Balloon(alerts);

        // Assert
        Assert.Equal(new AlertBalloon("2 sessions need you", "api: Needs input\nweb: Needs input"), balloon);
    }

    [Fact]
    public void Balloon_MixedAlerts_UsesANeutralTitleListingEachSession()
    {
        // Arrange
        StatusAlert[] alerts =
        [
            new("a", StatusAlertKind.NeedsInput, "api", "Needs input"),
            new("b", StatusAlertKind.TurnFinished, "web", "Finished"),
        ];

        // Act
        AlertBalloon balloon = StatusAlerts.Balloon(alerts);

        // Assert
        Assert.Equal(new AlertBalloon("2 sessions updated", "api: Needs input\nweb: Finished"), balloon);
    }

    [Fact]
    public void Balloon_SeveralFinishedAlerts_UsesANeutralTitle()
    {
        // Arrange
        StatusAlert[] alerts =
        [
            new("a", StatusAlertKind.TurnFinished, "api", "Finished"),
            new("b", StatusAlertKind.TurnFinished, "web", "Finished"),
        ];

        // Act / Assert
        Assert.Equal("2 sessions updated", StatusAlerts.Balloon(alerts).Title);
    }

    [Fact]
    public void Advance_SessionReadingAsNoStatus_KeepsTheStatusItHadBefore()
    {
        // Arrange
        SessionSnapshot[] waiting = [Session("a", SessionStatus.NeedsInput)];
        Baseline baseline = StatusAlerts.Advance(BaselineOf(waiting), [Session("a", SessionStatus.None)], Now.AddSeconds(5), LongTurn);

        // Act
        IReadOnlyList<StatusAlert> alerts = StatusAlerts.Diff(baseline, waiting, LongTurn);

        // Assert
        Assert.Empty(alerts);
    }

    [Fact]
    public void Advance_SessionFirstSeenWithNoStatus_IsRecordedAsNone()
    {
        // Act
        Baseline baseline = StatusAlerts.Advance(null, [Session("a", SessionStatus.None)], Now, LongTurn);

        // Assert
        Assert.Equal(SessionStatus.None, baseline["a"].Status);
    }

    [Fact]
    public void Balloon_MoreThanThreeAlerts_ListsThreeThenCountsTheRest()
    {
        // Arrange
        StatusAlert[] alerts = Enumerable.Range(1, 5)
            .Select(i => new StatusAlert($"s{i}", StatusAlertKind.NeedsInput, $"app{i}", "Needs input"))
            .ToArray();

        // Act
        AlertBalloon balloon = StatusAlerts.Balloon(alerts);

        // Assert
        Assert.Equal("5 sessions need you", balloon.Title);
        Assert.Equal("app1: Needs input\napp2: Needs input\napp3: Needs input\nand 2 more", balloon.Text);
    }

    [Fact]
    public void Balloon_VeryLongSessionTitles_StayWithinTheNotificationLimits()
    {
        // Arrange
        string longTitle = new('w', 300);
        StatusAlert[] alerts =
        [
            new("a", StatusAlertKind.NeedsInput, longTitle, "Needs input"),
            new("b", StatusAlertKind.NeedsInput, longTitle, "Needs input"),
            new("c", StatusAlertKind.NeedsInput, longTitle, "Needs input"),
        ];

        // Act
        AlertBalloon several = StatusAlerts.Balloon(alerts);
        AlertBalloon single = StatusAlerts.Balloon([alerts[0]]);

        // Assert
        Assert.InRange(several.Title.Length, 1, 62);
        Assert.InRange(several.Text.Length, 1, 254);
        Assert.InRange(single.Title.Length, 1, 62);
    }

    [Fact]
    public void Diff_SubagentFinishesAfterTheMainStopOnALongTurn_RaisesFinishedOnceOnTheShownTransition()
    {
        // Arrange: the main agent stopped at 1100, the subagent kept working until 1150
        string[] lines =
        [
            """{"t":1000,"e":"UserPromptSubmit"}""", """{"t":1010,"e":"SubagentStart","a":"sub-1"}""", """{"t":1100,"e":"Stop"}""",
            """{"t":1150,"e":"SubagentStop","a":"sub-1"}""",
        ];
        SessionSnapshot working = Snapshot(SessionEvents.Reduce(lines[..3], At(1120)));
        SessionSnapshot stillWorking = Snapshot(SessionEvents.Reduce(lines[..3], At(1140)));
        SessionSnapshot finished = Snapshot(SessionEvents.Reduce(lines, At(1160)));
        SessionSnapshot laterRefresh = Snapshot(SessionEvents.Reduce(lines, At(1500)));

        // Act
        IReadOnlyList<StatusAlert> atTheMainStop = StatusAlerts.Diff(BaselineOf(Snapshot(SessionEvents.Reduce(lines[..2], At(1050)))), [working], LongTurn);
        IReadOnlyList<StatusAlert> whileSubagentRuns = StatusAlerts.Diff(BaselineOf(working), [stillWorking], LongTurn);
        IReadOnlyList<StatusAlert> atTheEnd = StatusAlerts.Diff(BaselineOf(stillWorking), [finished], LongTurn);
        IReadOnlyList<StatusAlert> afterwards = StatusAlerts.Diff(BaselineOf(finished), [laterRefresh], LongTurn);

        // Assert
        Assert.Empty(atTheMainStop);
        Assert.Empty(whileSubagentRuns);
        Assert.Equal([new StatusAlert("a", StatusAlertKind.TurnFinished, "my-session", "Finished")], atTheEnd);
        Assert.Empty(afterwards);
    }

    [Fact]
    public void Diff_LostSubagentStop_RaisesFinishedWhenTheSubagentExpires()
    {
        // Arrange
        string[] lines = ["""{"t":1000,"e":"UserPromptSubmit"}""", """{"t":1010,"e":"SubagentStart","a":"sub-1"}""", """{"t":1100,"e":"Stop"}"""];
        SessionSnapshot working = Snapshot(SessionEvents.Reduce(lines, At(1500)));
        SessionSnapshot expired = Snapshot(SessionEvents.Reduce(lines, At(1700)));

        // Act
        IReadOnlyList<StatusAlert> alerts = StatusAlerts.Diff(BaselineOf(working), [expired], LongTurn);

        // Assert
        Assert.Equal([new StatusAlert("a", StatusAlertKind.TurnFinished, "my-session", "Finished")], alerts);
    }

    [Fact]
    public void Diff_SubagentExpiresReappearsAndStops_RaisesFinishedOnlyOnceForTheTurn()
    {
        // Arrange: the subagent goes quiet and expires at 1610, sends an event at 2000 and stops at 2100
        string[] lines =
        [
            """{"t":1000,"e":"UserPromptSubmit"}""", """{"t":1010,"e":"SubagentStart","a":"sub-1"}""", """{"t":1100,"e":"Stop"}""",
            """{"t":2000,"e":"PreToolUse","a":"sub-1"}""", """{"t":2100,"e":"SubagentStop","a":"sub-1"}""",
        ];
        SessionSnapshot working = Snapshot(SessionEvents.Reduce(lines[..3], At(1500)));
        SessionSnapshot expired = Snapshot(SessionEvents.Reduce(lines[..3], At(1700)));
        SessionSnapshot reappeared = Snapshot(SessionEvents.Reduce(lines[..4], At(2050)));
        SessionSnapshot stopped = Snapshot(SessionEvents.Reduce(lines, At(2150)));

        // Act
        Baseline seenWorking = BaselineOf(working);
        IReadOnlyList<StatusAlert> atExpiry = StatusAlerts.Diff(seenWorking, [expired], LongTurn);
        Baseline seenExpired = StatusAlerts.Advance(seenWorking, [expired], Now, LongTurn);
        Baseline seenReappeared = StatusAlerts.Advance(seenExpired, [reappeared], Now, LongTurn);
        IReadOnlyList<StatusAlert> atStop = StatusAlerts.Diff(seenReappeared, [stopped], LongTurn);

        // Assert
        Assert.Single(atExpiry);
        Assert.Empty(atStop);
    }

    [Fact]
    public void Diff_ReadyAgainWithANewSinceInsideOneRefresh_DoesNotAnnounceTheSameTurnTwice()
    {
        // Arrange
        Baseline announced = StatusAlerts.Advance(
            BaselineOf(Session("a", SessionStatus.Working)), [Session("a", SessionStatus.Ready, sinceSeconds: 0, turnSeconds: 100)], Now, LongTurn);
        SessionSnapshot[] readyAgain = [Session("a", SessionStatus.Ready, sinceSeconds: 50, turnSeconds: 150)];

        // Act
        IReadOnlyList<StatusAlert> alerts = StatusAlerts.Diff(announced, readyAgain, LongTurn);

        // Assert
        Assert.Empty(alerts);
    }

    [Fact]
    public void Diff_ANewTurnAfterAnAnnouncedOne_CanAnnounceAgain()
    {
        // Arrange
        Baseline announced = StatusAlerts.Advance(
            BaselineOf(Session("a", SessionStatus.Working)), [Session("a", SessionStatus.Ready, sinceSeconds: 0, turnSeconds: 100)], Now, LongTurn);
        SessionSnapshot[] nextTurn = [Session("a", SessionStatus.Ready, sinceSeconds: 500, turnSeconds: 60)];

        // Act
        IReadOnlyList<StatusAlert> alerts = StatusAlerts.Diff(announced, nextTurn, LongTurn);

        // Assert
        Assert.Single(alerts);
    }

    [Fact]
    public void Diff_ShortTurnThatLaterGrowsLong_StillAnnouncesOnce()
    {
        // Arrange: Ready after 10 s is no alert, so it must not use up the turn's one announcement
        Baseline shortTurn = StatusAlerts.Advance(
            BaselineOf(Session("a", SessionStatus.Working)), [Session("a", SessionStatus.Ready, sinceSeconds: 0, turnSeconds: 10)], Now, LongTurn);
        SessionSnapshot[] afterSubagent = [Session("a", SessionStatus.Ready, sinceSeconds: 100, turnSeconds: 110)];

        // Act
        IReadOnlyList<StatusAlert> alerts = StatusAlerts.Diff(shortTurn, afterSubagent, LongTurn);

        // Assert
        Assert.Single(alerts);
    }

    private static SessionSnapshot Snapshot(SessionEventState events)
    {
        BridgeState state = BridgeState.Parse(Payloads.Full) with { SessionId = "a" };
        return new SessionSnapshot("a", state, Now, events.Status, events.Since, events.TurnStarted, Subagents: events.Subagents);
    }

    private static DateTimeOffset At(long seconds) => DateTimeOffset.FromUnixTimeSeconds(seconds);

    private static Baseline BaselineOf(params SessionSnapshot[] sessions) => StatusAlerts.Advance(null, sessions, Now, LongTurn);

    private static SessionSnapshot Session(
        string id, SessionStatus status, int sinceSeconds = 0, int? turnSeconds = null)
    {
        BridgeState state = BridgeState.Parse(Payloads.Full) with { SessionId = id };
        DateTimeOffset since = Now.AddSeconds(sinceSeconds);
        DateTimeOffset? turnStarted = turnSeconds is { } seconds ? since.AddSeconds(-seconds) : null;
        return new SessionSnapshot(id, state, Now, status, since, turnStarted);
    }
}
