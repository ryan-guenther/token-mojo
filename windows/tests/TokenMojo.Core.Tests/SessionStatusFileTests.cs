using System.Text.Json;
using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public class SessionStatusFileTests
{
    [Theory]
    [InlineData("working", SessionStatus.Working)]
    [InlineData("needs_input", SessionStatus.NeedsInput)]
    [InlineData("ready", SessionStatus.Ready)]
    [InlineData("ended", SessionStatus.Ended)]
    public void Parse_EachHookStatus_MapsToItsEnumValue(string name, SessionStatus expected)
    {
        // Act
        SessionStatusFile file = SessionStatusFile.Parse($$"""{"status":"{{name}}","since":1000,"turn_started":null}""");

        // Assert
        Assert.Equal(expected, file.Status);
    }

    [Fact]
    public void Parse_TurnInProgress_ReadsSinceAndTurnStartAsTimes()
    {
        // Act
        SessionStatusFile file = SessionStatusFile.Parse("""{"status":"working","since":1792721250,"turn_started":1792721248}""");

        // Assert
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1792721250), file.Since);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1792721248), file.TurnStarted);
    }

    [Fact]
    public void Parse_NullTurnStarted_LeavesItNull()
    {
        // Act
        SessionStatusFile file = SessionStatusFile.Parse("""{"status":"ready","since":1000,"turn_started":null}""");

        // Assert
        Assert.Null(file.TurnStarted);
    }

    [Theory]
    [InlineData("""{"status":"napping","since":1000,"turn_started":null}""")]
    [InlineData("""{"since":1000,"turn_started":null}""")]
    [InlineData("""{"status":"ready","turn_started":null}""")]
    [InlineData("""{"status":"ready","since":99999999999999,"turn_started":null}""")]
    [InlineData("""{"status":"working","since":1000,"turn_started":-99999999999999}""")]
    [InlineData("not json")]
    [InlineData("null")]
    public void Parse_UnknownOrIncompleteContent_ThrowsJsonException(string json)
    {
        // Act / Assert
        Assert.Throws<JsonException>(() => SessionStatusFile.Parse(json));
    }
}
