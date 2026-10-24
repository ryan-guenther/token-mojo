using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public sealed class SessionActionsTests
{
    [Theory]
    [InlineData(SessionStatus.Working)]
    [InlineData(SessionStatus.Ready)]
    [InlineData(SessionStatus.NeedsInput)]
    [InlineData(SessionStatus.Ended)]
    [InlineData(SessionStatus.None)]
    public void Decide_WindowFound_Focuses(SessionStatus status)
    {
        // Act / Assert
        Assert.Equal(SessionAction.Focus, SessionActions.Decide(status, windowFound: true));
    }

    [Theory]
    [InlineData(SessionStatus.Working)]
    [InlineData(SessionStatus.Ready)]
    [InlineData(SessionStatus.NeedsInput)]
    [InlineData(SessionStatus.None)]
    public void Decide_NoWindowButSessionMayStillBeRunning_AsksBeforeResuming(SessionStatus status)
    {
        // Act / Assert
        Assert.Equal(SessionAction.ConfirmResume, SessionActions.Decide(status, windowFound: false));
    }

    [Fact]
    public void Decide_NoWindowAndSessionEnded_ResumesWithoutAsking()
    {
        // Act / Assert
        Assert.Equal(SessionAction.Resume, SessionActions.Decide(SessionStatus.Ended, windowFound: false));
    }
}
