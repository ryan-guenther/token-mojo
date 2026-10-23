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
        Baseline afterGap = StatusAlerts.Advance(afterFirst, [], Now.AddSeconds(5));
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
        Baseline afterGap = StatusAlerts.Advance(BaselineOf(both), [], Now.AddSeconds(5));

        // Act / Assert
        Assert.Empty(StatusAlerts.Diff(afterGap, both, LongTurn));
    }

    [Fact]
    public void Diff_SameSessionIdFromTwoSources_LastOneWinsWithoutThrowing()
    {
        // Arrange
        Baseline baseline = BaselineOf(Session("a", SessionStatus.Working));
        SessionSnapshot[] current = [Session("a", SessionStatus.Working), Session("a", SessionStatus.NeedsInput, sinceSeconds: 5)];

        // Act
        IReadOnlyList<StatusAlert> alerts = StatusAlerts.Diff(baseline, current, LongTurn);

        // Assert
        Assert.Equal(["a"], alerts.Select(a => a.SessionId));
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
    public void Advance_SameSessionIdFromTwoSources_KeepsTheLastOne()
    {
        // Arrange
        SessionSnapshot[] current = [Session("a", SessionStatus.Working), Session("a", SessionStatus.Ready)];

        // Act
        Baseline baseline = StatusAlerts.Advance(null, current, Now);

        // Assert
        Assert.Equal(SessionStatus.Ready, baseline["a"].Status);
    }

    [Fact]
    public void Advance_SessionNotSeenForTwelveHours_IsForgotten()
    {
        // Arrange
        Baseline baseline = BaselineOf(Session("old", SessionStatus.Working), Session("kept", SessionStatus.Working));
        baseline = StatusAlerts.Advance(baseline, [Session("kept", SessionStatus.Working)], Now.AddHours(11));

        // Act
        Baseline later = StatusAlerts.Advance(baseline, [Session("kept", SessionStatus.Working)], Now.AddHours(13));

        // Assert
        Assert.Equal(["kept"], later.Keys);
    }

    private static Baseline BaselineOf(params SessionSnapshot[] sessions) => StatusAlerts.Advance(null, sessions, Now);

    private static SessionSnapshot Session(string id, SessionStatus status, int sinceSeconds = 0, int? turnSeconds = null)
    {
        BridgeState state = BridgeState.Parse(Payloads.Full) with { SessionId = id };
        DateTimeOffset since = Now.AddSeconds(sinceSeconds);
        DateTimeOffset? turnStarted = turnSeconds is { } seconds ? since.AddSeconds(-seconds) : null;
        return new SessionSnapshot(id, state, Now, status, since, turnStarted);
    }
}
