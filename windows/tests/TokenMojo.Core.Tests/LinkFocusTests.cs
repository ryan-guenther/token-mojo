using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public sealed class LinkFocusTests
{
    private const string Id = "0f8e2a4c-1b3d-4e5f-8a9b-0c1d2e3f4a5b";
    private static readonly DateTimeOffset Now = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly BridgeState State = BridgeState.Parse(Payloads.Full);

    [Theory]
    [InlineData(SessionStatus.Working)]
    [InlineData(SessionStatus.Ready)]
    [InlineData(SessionStatus.NeedsInput)]
    public void Find_OpenSession_GivesIt(SessionStatus status)
    {
        // Arrange
        SessionSnapshot session = new(Id, State, Now.AddHours(-2), status);

        // Act
        LinkFocusResult result = LinkFocus.Find(new FocusLink(Id), [session], Now);

        // Assert
        Assert.Null(result.Problem);
        Assert.Same(session, result.Session);
    }

    [Fact]
    public void Find_SessionWithoutStatusActiveRecently_GivesIt()
    {
        // Arrange
        SessionSnapshot session = new(Id, State, Now.AddMinutes(-3));

        // Act
        LinkFocusResult result = LinkFocus.Find(new FocusLink(Id), [session], Now);

        // Assert
        Assert.Same(session, result.Session);
    }

    [Fact]
    public void Find_IdInUppercaseInTheSessionList_GivesIt()
    {
        // Arrange
        SessionSnapshot session = new(Id.ToUpperInvariant(), State, Now, SessionStatus.Working);

        // Act
        LinkFocusResult result = LinkFocus.Find(new FocusLink(Id), [session], Now);

        // Assert
        Assert.Same(session, result.Session);
    }

    [Fact]
    public void Find_UnknownSession_IsAProblem()
    {
        // Arrange
        SessionSnapshot other = new("11111111-2222-4333-8444-555555555555", State, Now, SessionStatus.Working);

        // Act
        LinkFocusResult result = LinkFocus.Find(new FocusLink(Id), [other], Now);

        // Assert
        Assert.Null(result.Session);
        Assert.Equal("Session 0f8e2a4c is not in the session list.", result.Problem);
    }

    [Fact]
    public void Find_EndedSession_IsAProblem()
    {
        // Arrange
        SessionSnapshot session = new(Id, State, Now, SessionStatus.Ended);

        // Act
        LinkFocusResult result = LinkFocus.Find(new FocusLink(Id), [session], Now);

        // Assert
        Assert.Null(result.Session);
        Assert.Equal("Session 0f8e2a4c is closed. Resume it from the session list.", result.Problem);
    }

    [Fact]
    public void Find_SessionWithoutStatusIdleForTenMinutes_IsClosed()
    {
        // Arrange
        SessionSnapshot session = new(Id, State, Now.AddMinutes(-10));

        // Act
        LinkFocusResult result = LinkFocus.Find(new FocusLink(Id), [session], Now);

        // Assert
        Assert.Equal("Session 0f8e2a4c is closed. Resume it from the session list.", result.Problem);
    }
}
