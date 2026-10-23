using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public class SessionEventsTests
{
    private const string Permission = "permission_prompt";

    [Fact]
    public void Reduce_NoLines_HasNoStatus()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce([]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.None, null, null), state);
    }

    [Fact]
    public void Reduce_SessionStart_IsReadyWithNoTurn()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce([Line(100, "SessionStart", source: "startup")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Ready, At(100), null), state);
    }

    [Fact]
    public void Reduce_SessionStartAfterCompact_ChangesNothing()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce([Line(100, "SessionStart", source: "compact")]);

        // Assert
        Assert.Equal(SessionStatus.None, state.Status);
    }

    [Fact]
    public void Reduce_CompactMidTurn_KeepsTheTurnStart()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce(
            [Line(100, "UserPromptSubmit"), Line(130, "SessionStart", source: "compact")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(100), At(100)), state);
    }

    [Fact]
    public void Reduce_UserPromptSubmit_IsWorkingAndStartsTheTurn()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce([Line(100, "SessionStart"), Line(110, "UserPromptSubmit")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(110), At(110)), state);
    }

    [Fact]
    public void Reduce_PromptWhileAlreadyWorking_KeepsSinceButRestartsTheTurn()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce([Line(100, "UserPromptSubmit"), Line(150, "UserPromptSubmit")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(100), At(150)), state);
    }

    [Fact]
    public void Reduce_ToolUseWhileWorking_KeepsSince()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce(
            [Line(100, "UserPromptSubmit"), Line(120, "PreToolUse"), Line(125, "PostToolUse")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(100), At(100)), state);
    }

    [Fact]
    public void Reduce_PreToolUseAfterPermissionPrompt_StaysNeedsInput()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce(
            [Line(100, "UserPromptSubmit"), Line(110, "Notification", notification: Permission), Line(115, "PreToolUse")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.NeedsInput, At(110), At(100)), state);
    }

    [Fact]
    public void Reduce_SubagentPreToolUseAfterPermissionPrompt_StaysNeedsInput()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce(
            [Line(100, "UserPromptSubmit"), Line(110, "Notification", notification: Permission),
                Line(112, "PreToolUse", agent: "sub-1")]);

        // Assert
        Assert.Equal(SessionStatus.NeedsInput, state.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("sub-1")]
    public void Reduce_PostToolUseAfterPermissionPrompt_ClearsNeedsInput(string? agent)
    {
        // Act
        SessionEventState state = SessionEvents.Reduce(
            [Line(100, "UserPromptSubmit"), Line(110, "Notification", notification: Permission),
                Line(120, "PostToolUse", agent: agent)]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(120), At(100)), state);
    }

    [Fact]
    public void Reduce_ToolUseWhileReady_StartsAnUnpromptedTurn()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce(
            [Line(100, "SessionStart"), Line(200, "PreToolUse")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(200), At(200)), state);
    }

    [Fact]
    public void Reduce_PostToolUseWhileReady_StartsAnUnpromptedTurn()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce([Line(100, "SessionStart"), Line(200, "PostToolUse")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(200), At(200)), state);
    }

    [Fact]
    public void Reduce_ToolUseWithNoEarlierEvents_IsWorkingWithNoTurnStart()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce([Line(100, "PreToolUse")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(100), null), state);
    }

    [Theory]
    [InlineData("permission_prompt")]
    [InlineData("elicitation_dialog")]
    [InlineData("elicitation_url_dialog")]
    [InlineData("agent_needs_input")]
    public void Reduce_QuestionNotification_IsNeedsInput(string notification)
    {
        // Act
        SessionEventState state = SessionEvents.Reduce(
            [Line(100, "UserPromptSubmit"), Line(110, "Notification", notification: notification)]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.NeedsInput, At(110), At(100)), state);
    }

    [Theory]
    [InlineData("idle_prompt")]
    [InlineData(null)]
    public void Reduce_OtherNotification_ChangesNothing(string? notification)
    {
        // Act
        SessionEventState state = SessionEvents.Reduce(
            [Line(100, "UserPromptSubmit"), Line(110, "Notification", notification: notification)]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(100), At(100)), state);
    }

    [Fact]
    public void Reduce_NotificationAfterSessionEnd_StaysEnded()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce(
            [Line(100, "SessionEnd"), Line(110, "Notification", notification: Permission)]);

        // Assert
        Assert.Equal(SessionStatus.Ended, state.Status);
    }

    [Fact]
    public void Reduce_Stop_IsReadyAndKeepsTheTurnStart()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce([Line(100, "UserPromptSubmit"), Line(160, "Stop")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Ready, At(160), At(100)), state);
    }

    [Fact]
    public void Reduce_StopWhileAlreadyReady_KeepsSince()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce([Line(100, "SessionStart"), Line(160, "Stop")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Ready, At(100), null), state);
    }

    [Fact]
    public void Reduce_SessionEnd_IsEndedWithNoTurn()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce([Line(100, "UserPromptSubmit"), Line(160, "SessionEnd")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Ended, At(160), null), state);
    }

    [Fact]
    public void Reduce_LateEventsAfterSessionEnd_StayEnded()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce(
            [Line(100, "UserPromptSubmit"), Line(160, "SessionEnd"), Line(161, "PreToolUse"), Line(162, "PostToolUse"),
                Line(163, "Stop")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Ended, At(160), null), state);
    }

    [Fact]
    public void Reduce_SessionStartAfterEnd_IsReadyAgain()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce([Line(100, "SessionEnd"), Line(200, "SessionStart", source: "resume")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Ready, At(200), null), state);
    }

    [Fact]
    public void Reduce_BackgroundWake_UsesTheNewTurnStart()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce(
            [Line(100, "UserPromptSubmit"), Line(160, "Stop"), Line(500, "PreToolUse"), Line(560, "Stop")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Ready, At(560), At(500)), state);
    }

    [Fact]
    public void Reduce_UnknownEvent_ChangesNothing()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce([Line(100, "UserPromptSubmit"), Line(110, "Mystery")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(100), At(100)), state);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    [InlineData("null")]
    [InlineData("""{"e":"Stop"}""")]
    [InlineData("""{"t":"soon","e":"Stop"}""")]
    [InlineData("""{"t":99999999999999,"e":"Stop"}""")]
    [InlineData("""{"t":5}""")]
    [InlineData("""{"t":5,"e":7}""")]
    [InlineData("""{"t":5,"e":"Stop""")]
    public void Reduce_MalformedOrPartialLine_IsSkipped(string malformed)
    {
        // Act
        SessionEventState state = SessionEvents.Reduce(
            [Line(100, "UserPromptSubmit"), malformed, Line(120, "PreToolUse")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(100), At(100)), state);
    }

    private static string Line(long t, string e, string? agent = null, string? notification = null, string? source = null)
    {
        string extras = (agent is null ? "" : $",\"a\":\"{agent}\"")
            + (notification is null ? "" : $",\"n\":\"{notification}\"")
            + (source is null ? "" : $",\"s\":\"{source}\"");
        return $"{{\"t\":{t},\"e\":\"{e}\"{extras}}}";
    }

    private static DateTimeOffset At(long t) => DateTimeOffset.FromUnixTimeSeconds(t);
}
