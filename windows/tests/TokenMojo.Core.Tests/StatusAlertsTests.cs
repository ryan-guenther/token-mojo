using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public class StatusAlertsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan LongTurn = TimeSpan.FromSeconds(30);

    [Fact]
    public void Diff_FirstLoad_RaisesNothingBecauseItIsOnlyABaseline()
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
        SessionSnapshot[] previous = [Session("a", SessionStatus.Working)];
        SessionSnapshot[] current = [Session("a", SessionStatus.NeedsInput)];

        // Act
        IReadOnlyList<StatusAlert> alerts = StatusAlerts.Diff(previous, current, LongTurn);

        // Assert
        Assert.Equal([new StatusAlert("a", StatusAlertKind.NeedsInput, "my-session", "Needs input")], alerts);
    }

    [Fact]
    public void Diff_SessionStaysInNeedsInput_RaisesNothing()
    {
        // Arrange
        SessionSnapshot[] both = [Session("a", SessionStatus.NeedsInput)];

        // Act / Assert
        Assert.Empty(StatusAlerts.Diff(both, both, LongTurn));
    }

    [Fact]
    public void Diff_SessionFirstSeenWaitingForInput_RaisesNeedsInputAlert()
    {
        // Arrange
        SessionSnapshot[] previous = [Session("a", SessionStatus.Working)];
        SessionSnapshot[] current = [Session("a", SessionStatus.Working), Session("b", SessionStatus.NeedsInput)];

        // Act
        IReadOnlyList<StatusAlert> alerts = StatusAlerts.Diff(previous, current, LongTurn);

        // Assert
        Assert.Equal(["b"], alerts.Select(a => a.SessionId));
    }

    [Theory]
    [InlineData(45, true)]
    [InlineData(30, true)]
    [InlineData(29, false)]
    public void Diff_TurnFinishes_RaisesAlertOnlyWhenTurnWasLong(int turnSeconds, bool expected)
    {
        // Arrange
        SessionSnapshot[] previous = [Session("a", SessionStatus.Working)];
        SessionSnapshot[] current = [Session("a", SessionStatus.Ready, turnSeconds: turnSeconds)];

        // Act
        IReadOnlyList<StatusAlert> alerts = StatusAlerts.Diff(previous, current, LongTurn);

        // Assert
        Assert.Equal(expected, alerts.Count == 1);
    }

    [Fact]
    public void Diff_LongTurnFinishes_AlertNamesTheSessionAndSaysFinished()
    {
        // Arrange
        SessionSnapshot[] previous = [Session("a", SessionStatus.Working)];
        SessionSnapshot[] current = [Session("a", SessionStatus.Ready, turnSeconds: 120)];

        // Act
        IReadOnlyList<StatusAlert> alerts = StatusAlerts.Diff(previous, current, LongTurn);

        // Assert
        Assert.Equal([new StatusAlert("a", StatusAlertKind.TurnFinished, "my-session", "Finished")], alerts);
    }

    [Fact]
    public void Diff_ReadyWithoutTurnStart_RaisesNothing()
    {
        // Arrange
        SessionSnapshot[] previous = [Session("a", SessionStatus.Working)];
        SessionSnapshot[] current = [Session("a", SessionStatus.Ready)];

        // Act / Assert
        Assert.Empty(StatusAlerts.Diff(previous, current, LongTurn));
    }

    [Fact]
    public void Diff_SessionAlreadyReady_RaisesNothingEvenAfterALongTurn()
    {
        // Arrange
        SessionSnapshot[] both = [Session("a", SessionStatus.Ready, turnSeconds: 600)];

        // Act / Assert
        Assert.Empty(StatusAlerts.Diff(both, both, LongTurn));
    }

    [Fact]
    public void Diff_UnnamedSession_IsTitledByItsFolder()
    {
        // Arrange
        SessionSnapshot[] previous = [Session("a", SessionStatus.Working)];
        SessionSnapshot unnamed = Session("a", SessionStatus.NeedsInput);
        unnamed = unnamed with { State = unnamed.State with { SessionName = null } };

        // Act
        IReadOnlyList<StatusAlert> alerts = StatusAlerts.Diff(previous, [unnamed], LongTurn);

        // Assert
        Assert.Equal("proj", alerts[0].Title);
    }

    private static SessionSnapshot Session(string id, SessionStatus status, int? turnSeconds = null)
    {
        BridgeState state = BridgeState.Parse(Payloads.Full) with { SessionId = id };
        DateTimeOffset? turnStarted = turnSeconds is { } seconds ? Now.AddSeconds(-seconds) : null;
        return new SessionSnapshot(id, state, Now, status, Now, turnStarted);
    }
}
