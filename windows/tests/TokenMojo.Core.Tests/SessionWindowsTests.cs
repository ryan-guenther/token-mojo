using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public sealed class SessionWindowsTests
{
    private const string SessionId = "3636af4c-8a5c-4e7f-a26c-739e5f93af06";

    [Fact]
    public void Find_TitleHasTheTag_ReturnsItsIndex()
    {
        // Arrange
        string[] titles = ["Ubuntu", "TM my-app 3636af4c", "Notes"];

        // Act / Assert
        Assert.Equal(1, SessionWindows.Find(titles, SessionId));
    }

    [Fact]
    public void Find_NoTitleHasTheTag_ReturnsNull()
    {
        // Arrange
        string[] titles = ["Ubuntu", "TM my-app 11111111"];

        // Act / Assert
        Assert.Null(SessionWindows.Find(titles, SessionId));
    }

    [Fact]
    public void Find_SeveralTitlesMatch_ReturnsTheFirst()
    {
        // Arrange
        string[] titles = ["TM a 3636af4c", "TM b 3636af4c"];

        // Act / Assert
        Assert.Equal(0, SessionWindows.Find(titles, SessionId));
    }

    [Theory]
    [InlineData("TM my-app 3636af4cd")]
    [InlineData("TM my-app a3636af4c")]
    [InlineData("TM my-app 3636af4c0123456789")]
    [InlineData("TM my-app 0123456783636af4c")]
    public void Matches_IdsEightCharactersAreInsideALongerRun_IsFalse(string title)
    {
        // Act / Assert
        Assert.False(SessionWindows.Matches(title, SessionId));
    }

    [Theory]
    [InlineData("TM my-app 3636af4c")]
    [InlineData("3636af4c")]
    [InlineData("⠂ TM my-app 3636af4c")]
    [InlineData("TM my-app 3636af4c - Windows Terminal")]
    [InlineData("TM my-app (3636af4c)")]
    [InlineData("TM my-app 3636AF4C")]
    public void Matches_IdIsAWholeToken_IsTrue(string title)
    {
        // Act / Assert
        Assert.True(SessionWindows.Matches(title, SessionId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc-123")]
    public void Matches_SessionIdShorterThanEightCharacters_IsFalse(string sessionId)
    {
        // Act / Assert
        Assert.False(SessionWindows.Matches("TM my-app abc-123 ", sessionId));
    }
}
