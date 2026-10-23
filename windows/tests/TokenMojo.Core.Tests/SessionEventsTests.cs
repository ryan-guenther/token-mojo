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
    public void Reduce_PromptWhileAlreadyWorking_KeepsSinceAndTheTurnStart()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce([Line(100, "UserPromptSubmit"), Line(150, "UserPromptSubmit")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(100), At(100)), state);
    }

    [Fact]
    public void Reduce_PromptWhileNeedingInput_IsWorkingAndKeepsTheTurnStart()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce(
            [Line(100, "UserPromptSubmit"), Line(110, "Notification", notification: Permission), Line(150, "UserPromptSubmit")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(150), At(100)), state);
    }

    [Fact]
    public void Reduce_PromptWhileReady_StartsANewTurn()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce(
            [Line(100, "UserPromptSubmit"), Line(160, "Stop"), Line(300, "UserPromptSubmit")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(300), At(300)), state);
    }

    [Fact]
    public void Reduce_PromptAfterSessionEnd_StaysEndedBecauseAResumeStartsWithSessionStart()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce([Line(100, "SessionEnd"), Line(200, "UserPromptSubmit")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Ended, At(100), null), state);
    }

    [Fact]
    public void Reduce_RealSessionWithASubagent_KeepsTheFirstPromptsTurnStart()
    {
        // Arrange: the order Claude Code sent, where the subagent's result arrives as a second prompt
        string[] lines =
        [
            Line(100, "SessionStart", source: "startup"),
            Line(101, "UserPromptSubmit"),
            Line(102, "PreToolUse"),
            Line(103, "PostToolUse"),
            Line(104, "PreToolUse"),
            Line(105, "PreToolUse", agent: "agent-1"),
            Line(130, "PostToolUse", agent: "agent-1"),
            Line(131, "PostToolUse"),
            Line(132, "UserPromptSubmit"),
            Line(140, "Stop"),
        ];

        // Act
        SessionEventState finished = SessionEvents.Reduce(lines);
        SessionEventState ended = SessionEvents.Reduce([.. lines, Line(150, "SessionEnd")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Ready, At(140), At(101)), finished);
        Assert.Equal(new SessionEventState(SessionStatus.Ended, At(150), null), ended);
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
    public void Reduce_MainAgentPreToolUseAfterPermissionPrompt_ClearsNeedsInputBecauseTheNextToolCallFollowsADenial()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce(
            [Line(100, "UserPromptSubmit"), Line(110, "Notification", notification: Permission), Line(115, "PreToolUse")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(115), At(100)), state);
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
    [InlineData("PostToolUse", null)]
    [InlineData("PostToolUse", "sub-1")]
    [InlineData("PostToolUseFailure", null)]
    [InlineData("PostToolUseFailure", "sub-1")]
    public void Reduce_ToolFinishingAfterPermissionPrompt_ClearsNeedsInput(string toolEvent, string? agent)
    {
        // Act
        SessionEventState state = SessionEvents.Reduce(
            [Line(100, "UserPromptSubmit"), Line(110, "Notification", notification: Permission),
                Line(120, toolEvent, agent: agent)]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(120), At(100)), state);
    }

    [Fact]
    public void Reduce_PostToolUseFailureWhileReady_StartsAnUnpromptedTurn()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce([Line(100, "SessionStart"), Line(200, "PostToolUseFailure")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(200), At(200)), state);
    }

    [Fact]
    public void Reduce_PostToolUseFailureAfterSessionEnd_StaysEnded()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce([Line(100, "SessionEnd"), Line(110, "PostToolUseFailure")]);

        // Assert
        Assert.Equal(SessionStatus.Ended, state.Status);
    }

    [Fact]
    public void Reduce_NeedsInputFromReady_StartsTheTurnSoAnOldTurnStartNeverMakesAFinishedAlert()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce(
            [Line(100, "UserPromptSubmit"), Line(160, "Stop"), Line(900, "Notification", notification: Permission)]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.NeedsInput, At(900), At(900)), state);
    }

    [Fact]
    public void Reduce_NeedsInputWithNoEarlierEvents_StartsTheTurn()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce([Line(900, "Notification", notification: Permission)]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.NeedsInput, At(900), At(900)), state);
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
    public void Reduce_ToolUseWithNoEarlierEvents_IsWorkingWithTheEventAsALowerBoundForTheTurnStart()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce([Line(100, "PreToolUse")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(100), At(100)), state);
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

    [Fact]
    public void Reduce_StopWhileASubagentIsActive_StaysWorkingAndKeepsTheTurnStart()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce(
            [Line(100, "UserPromptSubmit"), Line(110, "SubagentStart", agent: "sub-1"), Line(120, "Stop")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(100), At(100), 1), state);
    }

    [Fact]
    public void Reduce_LastSubagentStopAfterTheMainStop_IsReadyFromThatMomentOfTheSameTurn()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce(
            [Line(100, "UserPromptSubmit"), Line(110, "SubagentStart", agent: "sub-1"), Line(120, "Stop"),
                Line(165, "SubagentStop", agent: "sub-1")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Ready, At(165), At(100)), state);
    }

    [Fact]
    public void Reduce_SubagentStopBeforeTheMainStop_LeavesTheStopToMakeItReady()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce(
            [Line(100, "UserPromptSubmit"), Line(110, "SubagentStart", agent: "sub-1"), Line(150, "SubagentStop", agent: "sub-1"),
                Line(160, "Stop")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Ready, At(160), At(100)), state);
    }

    [Fact]
    public void Reduce_TwoSubagentsAfterTheMainStop_AreReadyOnlyWhenTheLastOneStops()
    {
        // Arrange
        string[] lines =
        [
            Line(100, "UserPromptSubmit"), Line(110, "SubagentStart", agent: "sub-1"), Line(111, "SubagentStart", agent: "sub-2"),
            Line(120, "Stop"), Line(150, "SubagentStop", agent: "sub-1"),
        ];

        // Act
        SessionEventState oneLeft = SessionEvents.Reduce(lines);
        SessionEventState allDone = SessionEvents.Reduce([.. lines, Line(180, "SubagentStop", agent: "sub-2")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(100), At(100), 1), oneLeft);
        Assert.Equal(new SessionEventState(SessionStatus.Ready, At(180), At(100)), allDone);
    }

    [Fact]
    public void Reduce_SubagentToolEventAfterTheMainStop_KeepsTheSameTurn()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce(
            [Line(100, "UserPromptSubmit"), Line(110, "SubagentStart", agent: "sub-1"), Line(120, "Stop"),
                Line(130, "PreToolUse", agent: "sub-1"), Line(135, "PostToolUse", agent: "sub-1")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(100), At(100), 1), state);
    }

    [Fact]
    public void Reduce_SubagentStopForAnUnknownId_ChangesNothing()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce(
            [Line(100, "UserPromptSubmit"), Line(110, "SubagentStop", agent: "cut-off"), Line(120, "Stop")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Ready, At(120), At(100)), state);
    }

    [Fact]
    public void Reduce_SubagentStartTwiceForOneId_CountsOnce()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce(
            [Line(100, "UserPromptSubmit"), Line(110, "SubagentStart", agent: "sub-1"), Line(111, "SubagentStart", agent: "sub-1")]);

        // Assert
        Assert.Equal(1, state.Subagents);
    }

    [Theory]
    [InlineData("SubagentStart")]
    [InlineData("SubagentStop")]
    public void Reduce_SubagentEventWithoutAnId_ChangesNothing(string subagentEvent)
    {
        // Act
        SessionEventState state = SessionEvents.Reduce([Line(100, "UserPromptSubmit"), Line(110, subagentEvent)]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(100), At(100)), state);
    }

    [Fact]
    public void Reduce_SessionEndWithActiveSubagents_IsEndedWithNoneCounted()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce(
            [Line(100, "UserPromptSubmit"), Line(110, "SubagentStart", agent: "sub-1"), Line(120, "Stop"), Line(130, "SessionEnd")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Ended, At(130), null), state);
    }

    [Fact]
    public void Reduce_SessionStartClearsSubagentsLeftByACrash()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce(
            [Line(100, "UserPromptSubmit"), Line(110, "SubagentStart", agent: "sub-1"), Line(120, "Stop"),
                Line(200, "SessionStart", source: "resume"), Line(210, "UserPromptSubmit"), Line(220, "Stop")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Ready, At(220), At(210)), state);
    }

    [Fact]
    public void Reduce_SessionStartAfterCompact_KeepsTheActiveSubagents()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce(
            [Line(100, "UserPromptSubmit"), Line(110, "SubagentStart", agent: "sub-1"), Line(130, "SessionStart", source: "compact")]);

        // Assert
        Assert.Equal(1, state.Subagents);
    }

    [Theory]
    [InlineData("SubagentStart")]
    [InlineData("SubagentStop")]
    public void Reduce_SubagentEventAfterSessionEnd_StaysEnded(string subagentEvent)
    {
        // Act
        SessionEventState state = SessionEvents.Reduce(
            [Line(100, "SessionEnd"), Line(110, subagentEvent, agent: "sub-1")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Ended, At(100), null), state);
    }

    [Fact]
    public void Reduce_MainAgentActsAgainAfterTheStop_StaysWorkingWhenTheLastSubagentStops()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce(
            [Line(100, "UserPromptSubmit"), Line(110, "SubagentStart", agent: "sub-1"), Line(120, "Stop"),
                Line(130, "UserPromptSubmit"), Line(140, "SubagentStop", agent: "sub-1")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(100), At(100)), state);
    }

    [Fact]
    public void Reduce_PermissionPromptWhileOnlySubagentsRun_StillGoesReadyWhenTheLastOneStops()
    {
        // Act
        SessionEventState state = SessionEvents.Reduce(
            [Line(100, "UserPromptSubmit"), Line(110, "SubagentStart", agent: "sub-1"), Line(120, "Stop"),
                Line(130, "Notification", notification: Permission), Line(140, "PostToolUse", agent: "sub-1"),
                Line(150, "SubagentStop", agent: "sub-1")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Ready, At(150), At(100)), state);
    }

    [Fact]
    public void Reduce_RealSessionWithABackgroundSubagent_IsWorkingThroughTheSilenceAfterTheMainStop()
    {
        // Arrange: Stop, then nothing for 44 s while the subagent's model call ran
        string[] lines =
        [
            Line(100, "SessionStart", source: "startup"),
            Line(101, "UserPromptSubmit"),
            Line(102, "PreToolUse"),
            Line(103, "SubagentStart", agent: "a8a4bbd10ee559df4"),
            Line(104, "PostToolUse"),
            Line(105, "PreToolUse", agent: "a8a4bbd10ee559df4"),
            Line(106, "PostToolUse", agent: "a8a4bbd10ee559df4"),
            Line(107, "Stop"),
        ];

        // Act
        SessionEventState silent = SessionEvents.Reduce(lines);
        SessionEventState done = SessionEvents.Reduce([.. lines, Line(151, "SubagentStop", agent: "a8a4bbd10ee559df4")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(101), At(101), 1), silent);
        Assert.Equal(new SessionEventState(SessionStatus.Ready, At(151), At(101)), done);
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
