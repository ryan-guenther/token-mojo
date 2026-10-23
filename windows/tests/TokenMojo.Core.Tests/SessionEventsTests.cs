using TokenMojo.Core;

namespace TokenMojo.Core.Tests;

public class SessionEventsTests
{
    private const string Permission = "permission_prompt";

    [Fact]
    public void Reduce_NoLines_HasNoStatus()
    {
        // Act
        SessionEventState state = Reduce([]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.None, null, null), state);
    }

    [Fact]
    public void Reduce_SessionStart_IsReadyWithNoTurn()
    {
        // Act
        SessionEventState state = Reduce([Line(100, "SessionStart", source: "startup")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Ready, At(100), null), state);
    }

    [Fact]
    public void Reduce_SessionStartAfterCompact_ChangesNothing()
    {
        // Act
        SessionEventState state = Reduce([Line(100, "SessionStart", source: "compact")]);

        // Assert
        Assert.Equal(SessionStatus.None, state.Status);
    }

    [Fact]
    public void Reduce_CompactMidTurn_KeepsTheTurnStart()
    {
        // Act
        SessionEventState state = Reduce(
            [Line(100, "UserPromptSubmit"), Line(130, "SessionStart", source: "compact")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(100), At(100)), state);
    }

    [Fact]
    public void Reduce_UserPromptSubmit_IsWorkingAndStartsTheTurn()
    {
        // Act
        SessionEventState state = Reduce([Line(100, "SessionStart"), Line(110, "UserPromptSubmit")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(110), At(110)), state);
    }

    [Fact]
    public void Reduce_PromptWhileAlreadyWorking_KeepsSinceAndTheTurnStart()
    {
        // Act
        SessionEventState state = Reduce([Line(100, "UserPromptSubmit"), Line(150, "UserPromptSubmit")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(100), At(100)), state);
    }

    [Fact]
    public void Reduce_PromptWhileNeedingInput_IsWorkingAndKeepsTheTurnStart()
    {
        // Act
        SessionEventState state = Reduce(
            [Line(100, "UserPromptSubmit"), Line(110, "Notification", notification: Permission), Line(150, "UserPromptSubmit")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(150), At(100)), state);
    }

    [Fact]
    public void Reduce_PromptWhileReady_StartsANewTurn()
    {
        // Act
        SessionEventState state = Reduce(
            [Line(100, "UserPromptSubmit"), Line(160, "Stop"), Line(300, "UserPromptSubmit")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(300), At(300)), state);
    }

    [Fact]
    public void Reduce_PromptAfterSessionEnd_StaysEndedBecauseAResumeStartsWithSessionStart()
    {
        // Act
        SessionEventState state = Reduce([Line(100, "SessionEnd"), Line(200, "UserPromptSubmit")]);

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
            Line(105, "SubagentStart", agent: "agent-1"),
            Line(106, "PreToolUse", agent: "agent-1"),
            Line(130, "PostToolUse", agent: "agent-1"),
            Line(130, "SubagentStop", agent: "agent-1"),
            Line(131, "PostToolUse"),
            Line(132, "UserPromptSubmit"),
            Line(140, "Stop"),
        ];

        // Act
        SessionEventState finished = Reduce(lines);
        SessionEventState ended = Reduce([.. lines, Line(150, "SessionEnd")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Ready, At(140), At(101)), finished);
        Assert.Equal(new SessionEventState(SessionStatus.Ended, At(150), null), ended);
    }

    [Fact]
    public void Reduce_ToolUseWhileWorking_KeepsSince()
    {
        // Act
        SessionEventState state = Reduce(
            [Line(100, "UserPromptSubmit"), Line(120, "PreToolUse"), Line(125, "PostToolUse")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(100), At(100)), state);
    }

    [Fact]
    public void Reduce_MainAgentPreToolUseAfterPermissionPrompt_ClearsNeedsInputBecauseTheNextToolCallFollowsADenial()
    {
        // Act
        SessionEventState state = Reduce(
            [Line(100, "UserPromptSubmit"), Line(110, "Notification", notification: Permission), Line(115, "PreToolUse")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(115), At(100)), state);
    }

    [Fact]
    public void Reduce_SubagentPreToolUseAfterPermissionPrompt_StaysNeedsInput()
    {
        // Act
        SessionEventState state = Reduce(
            [Line(100, "UserPromptSubmit"), Line(110, "Notification", notification: Permission),
                Line(112, "PreToolUse", agent: "sub-1")]);

        // Assert
        Assert.Equal(SessionStatus.NeedsInput, state.Status);
    }

    [Theory]
    [InlineData("PostToolUse")]
    [InlineData("PostToolUseFailure")]
    public void Reduce_MainAgentToolFinishingAfterItsPermissionPrompt_ClearsNeedsInput(string toolEvent)
    {
        // Act
        SessionEventState state = Reduce(
            [Line(100, "UserPromptSubmit"), Line(110, "Notification", notification: Permission), Line(120, toolEvent)]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(120), At(100)), state);
    }

    [Fact]
    public void Reduce_PostToolUseFailureWhileReady_StartsAnUnpromptedTurn()
    {
        // Act
        SessionEventState state = Reduce([Line(100, "SessionStart"), Line(200, "PostToolUseFailure")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(200), At(200)), state);
    }

    [Fact]
    public void Reduce_PostToolUseFailureAfterSessionEnd_StaysEnded()
    {
        // Act
        SessionEventState state = Reduce([Line(100, "SessionEnd"), Line(110, "PostToolUseFailure")]);

        // Assert
        Assert.Equal(SessionStatus.Ended, state.Status);
    }

    [Fact]
    public void Reduce_NeedsInputFromReady_StartsTheTurnSoAnOldTurnStartNeverMakesAFinishedAlert()
    {
        // Act
        SessionEventState state = Reduce(
            [Line(100, "UserPromptSubmit"), Line(160, "Stop"), Line(900, "Notification", notification: Permission)]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.NeedsInput, At(900), At(900)), state);
    }

    [Fact]
    public void Reduce_NeedsInputWithNoEarlierEvents_StartsTheTurn()
    {
        // Act
        SessionEventState state = Reduce([Line(900, "Notification", notification: Permission)]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.NeedsInput, At(900), At(900)), state);
    }

    [Fact]
    public void Reduce_ToolUseWhileReady_StartsAnUnpromptedTurn()
    {
        // Act
        SessionEventState state = Reduce(
            [Line(100, "SessionStart"), Line(200, "PreToolUse")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(200), At(200)), state);
    }

    [Fact]
    public void Reduce_PostToolUseWhileReady_StartsAnUnpromptedTurn()
    {
        // Act
        SessionEventState state = Reduce([Line(100, "SessionStart"), Line(200, "PostToolUse")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(200), At(200)), state);
    }

    [Fact]
    public void Reduce_ToolUseWithNoEarlierEvents_IsWorkingWithTheEventAsALowerBoundForTheTurnStart()
    {
        // Act
        SessionEventState state = Reduce([Line(100, "PreToolUse")]);

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
        SessionEventState state = Reduce(
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
        SessionEventState state = Reduce(
            [Line(100, "UserPromptSubmit"), Line(110, "Notification", notification: notification)]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(100), At(100)), state);
    }

    [Fact]
    public void Reduce_NotificationAfterSessionEnd_StaysEnded()
    {
        // Act
        SessionEventState state = Reduce(
            [Line(100, "SessionEnd"), Line(110, "Notification", notification: Permission)]);

        // Assert
        Assert.Equal(SessionStatus.Ended, state.Status);
    }

    [Fact]
    public void Reduce_Stop_IsReadyAndKeepsTheTurnStart()
    {
        // Act
        SessionEventState state = Reduce([Line(100, "UserPromptSubmit"), Line(160, "Stop")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Ready, At(160), At(100)), state);
    }

    [Fact]
    public void Reduce_StopWhileAlreadyReady_KeepsSince()
    {
        // Act
        SessionEventState state = Reduce([Line(100, "SessionStart"), Line(160, "Stop")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Ready, At(100), null), state);
    }

    [Fact]
    public void Reduce_SessionEnd_IsEndedWithNoTurn()
    {
        // Act
        SessionEventState state = Reduce([Line(100, "UserPromptSubmit"), Line(160, "SessionEnd")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Ended, At(160), null), state);
    }

    [Fact]
    public void Reduce_LateEventsAfterSessionEnd_StayEnded()
    {
        // Act
        SessionEventState state = Reduce(
            [Line(100, "UserPromptSubmit"), Line(160, "SessionEnd"), Line(161, "PreToolUse"), Line(162, "PostToolUse"),
                Line(163, "Stop")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Ended, At(160), null), state);
    }

    [Fact]
    public void Reduce_SessionStartAfterEnd_IsReadyAgain()
    {
        // Act
        SessionEventState state = Reduce([Line(100, "SessionEnd"), Line(200, "SessionStart", source: "resume")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Ready, At(200), null), state);
    }

    [Fact]
    public void Reduce_BackgroundWake_UsesTheNewTurnStart()
    {
        // Act
        SessionEventState state = Reduce(
            [Line(100, "UserPromptSubmit"), Line(160, "Stop"), Line(500, "PreToolUse"), Line(560, "Stop")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Ready, At(560), At(500)), state);
    }

    [Fact]
    public void Reduce_UnknownEvent_ChangesNothing()
    {
        // Act
        SessionEventState state = Reduce([Line(100, "UserPromptSubmit"), Line(110, "Mystery")]);

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
        SessionEventState state = Reduce(
            [Line(100, "UserPromptSubmit"), malformed, Line(120, "PreToolUse")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(100), At(100)), state);
    }

    [Fact]
    public void Reduce_RealCapturedOrderWithABackgroundSubagent_IsWorkingThroughTheSilenceAfterTheMainStop()
    {
        // Arrange: Stop, then nothing while the subagent's model call ran
        string[] lines =
        [
            Line(100, "SessionStart", source: "startup"), Line(101, "UserPromptSubmit"), Line(102, "PreToolUse"),
            Line(103, "SubagentStart", agent: "a8a4bbd10ee559df4"), Line(104, "PostToolUse"),
            Line(105, "PreToolUse", agent: "a8a4bbd10ee559df4"), Line(106, "PostToolUse", agent: "a8a4bbd10ee559df4"),
            Line(107, "Stop"),
        ];

        // Act
        SessionEventState silent = ReduceAt(150, lines);
        SessionEventState done = ReduceAt(160, [.. lines, Line(151, "SubagentStop", agent: "a8a4bbd10ee559df4")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(101), At(101), 1), silent);
        Assert.Equal(new SessionEventState(SessionStatus.Ready, At(151), At(101)), done);
    }

    [Fact]
    public void Reduce_SubagentStopBeforeTheMainStop_IsReadyAtTheStop()
    {
        // Act
        SessionEventState state = ReduceAt(200,
            [Line(100, "UserPromptSubmit"), Line(110, "SubagentStart", agent: "sub-1"), Line(150, "SubagentStop", agent: "sub-1"),
                Line(160, "Stop")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Ready, At(160), At(100)), state);
    }

    [Fact]
    public void Reduce_SubagentRunsAfterTheMainAgentWasReady_IsWorkingUntilItStopsWithoutStartingATurn()
    {
        // Arrange: the main agent stopped with no subagent active
        string[] lines =
        [
            Line(100, "UserPromptSubmit"), Line(120, "Stop"), Line(130, "SubagentStart", agent: "sub-1"),
            Line(140, "PreToolUse", agent: "sub-1"), Line(150, "PostToolUse", agent: "sub-1"),
        ];

        // Act
        SessionEventState running = ReduceAt(155, lines);
        SessionEventState done = ReduceAt(200, [.. lines, Line(160, "SubagentStop", agent: "sub-1")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(130), At(100), 1), running);
        Assert.Equal(new SessionEventState(SessionStatus.Ready, At(160), At(100)), done);
    }

    [Fact]
    public void Reduce_SubagentToolEventsWithoutAStart_AreWorkingUntilItsStop()
    {
        // Arrange: the start was cut off by the tail window, or never logged
        string[] lines =
        [
            Line(100, "UserPromptSubmit"), Line(120, "Stop"), Line(140, "PreToolUse", agent: "sub-1"),
            Line(150, "PostToolUse", agent: "sub-1"),
        ];

        // Act
        SessionEventState running = ReduceAt(155, lines);
        SessionEventState done = ReduceAt(200, [.. lines, Line(160, "SubagentStop", agent: "sub-1")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(140), At(100), 1), running);
        Assert.Equal(new SessionEventState(SessionStatus.Ready, At(160), At(100)), done);
    }

    [Fact]
    public void Reduce_SubagentThatNeverStops_ExpiresTenMinutesAfterItsLastEvent()
    {
        // Arrange
        string[] lines = [Line(100, "UserPromptSubmit"), Line(110, "SubagentStart", agent: "sub-1"), Line(120, "Stop")];

        // Act
        SessionEventState silent = ReduceAt(300, lines);
        SessionEventState justBefore = ReduceAt(709, lines);
        SessionEventState expired = ReduceAt(710, lines);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(100), At(100), 1), silent);
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(100), At(100), 1), justBefore);
        Assert.Equal(new SessionEventState(SessionStatus.Ready, At(710), At(100)), expired);
    }

    [Fact]
    public void Reduce_ExpiredSubagent_DoesNotPinALaterTurn()
    {
        // Act
        SessionEventState state = ReduceAt(1000,
            [Line(100, "UserPromptSubmit"), Line(110, "SubagentStart", agent: "sub-1"), Line(120, "Stop"),
                Line(900, "UserPromptSubmit"), Line(950, "Stop")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Ready, At(950), At(900)), state);
    }

    [Fact]
    public void Reduce_SubagentEventsKeepItAlive_UntilTenMinutesAfterTheLastOne()
    {
        // Arrange
        string[] lines =
        [
            Line(100, "UserPromptSubmit"), Line(110, "SubagentStart", agent: "sub-1"), Line(120, "Stop"),
            Line(600, "PreToolUse", agent: "sub-1"),
        ];

        // Act
        SessionEventState alive = ReduceAt(1100, lines);
        SessionEventState expired = ReduceAt(1300, lines);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(100), At(100), 1), alive);
        Assert.Equal(new SessionEventState(SessionStatus.Ready, At(1200), At(100)), expired);
    }

    [Fact]
    public void Reduce_TwoSubagents_AreReadyOnlyWhenTheLastOneStops()
    {
        // Arrange
        string[] lines =
        [
            Line(100, "UserPromptSubmit"), Line(110, "SubagentStart", agent: "sub-1"), Line(111, "SubagentStart", agent: "sub-2"),
            Line(120, "Stop"), Line(150, "SubagentStop", agent: "sub-1"),
        ];

        // Act
        SessionEventState oneLeft = ReduceAt(160, lines);
        SessionEventState allDone = ReduceAt(190, [.. lines, Line(180, "SubagentStop", agent: "sub-2")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(100), At(100), 1), oneLeft);
        Assert.Equal(new SessionEventState(SessionStatus.Ready, At(180), At(100)), allDone);
    }

    [Fact]
    public void Reduce_SubagentPermissionPromptWhileTheMainAgentIsIdle_ReturnsToWorkingOnItsToolEvent()
    {
        // Arrange
        string[] lines =
        [
            Line(100, "UserPromptSubmit"), Line(110, "SubagentStart", agent: "sub-1"), Line(120, "Stop"),
            Line(130, "Notification", notification: Permission, agent: "sub-1"),
        ];
        string[] answered = [.. lines, Line(140, "PostToolUse", agent: "sub-1")];

        // Act
        SessionEventState waiting = ReduceAt(135, lines);
        SessionEventState working = ReduceAt(145, answered);
        SessionEventState done = ReduceAt(160, [.. answered, Line(150, "SubagentStop", agent: "sub-1")]);

        // Assert
        Assert.Equal(SessionStatus.NeedsInput, waiting.Status);
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(140), At(100), 1), working);
        Assert.Equal(new SessionEventState(SessionStatus.Ready, At(150), At(100)), done);
    }

    [Fact]
    public void Reduce_SubagentStopWhoseStartWasCut_ChangesNothing()
    {
        // Act
        SessionEventState state = ReduceAt(200,
            [Line(100, "UserPromptSubmit"), Line(120, "Stop"), Line(130, "SubagentStop", agent: "cut-off")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Ready, At(120), At(100)), state);
    }

    [Fact]
    public void Reduce_SubagentStopAfterItExpired_KeepsTheExpiryAsTheReadyTime()
    {
        // Act
        SessionEventState state = ReduceAt(2000,
            [Line(100, "UserPromptSubmit"), Line(110, "SubagentStart", agent: "sub-1"), Line(120, "Stop"),
                Line(1500, "SubagentStop", agent: "sub-1")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Ready, At(710), At(100)), state);
    }

    [Fact]
    public void Reduce_SubagentEventWithoutAnId_ChangesNothing()
    {
        // Act
        SessionEventState state = ReduceAt(120, [Line(100, "UserPromptSubmit"), Line(110, "SubagentStart")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(100), At(100)), state);
    }

    [Fact]
    public void Reduce_SessionEndWithActiveSubagents_IsEndedWithNoneCounted()
    {
        // Act
        SessionEventState state = ReduceAt(140,
            [Line(100, "UserPromptSubmit"), Line(110, "SubagentStart", agent: "sub-1"), Line(120, "Stop"), Line(130, "SessionEnd")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Ended, At(130), null), state);
    }

    [Fact]
    public void Reduce_SessionStartClearsSubagentsLeftByACrash()
    {
        // Act
        SessionEventState state = ReduceAt(210,
            [Line(100, "UserPromptSubmit"), Line(110, "SubagentStart", agent: "sub-1"), Line(120, "Stop"),
                Line(200, "SessionStart", source: "resume")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Ready, At(200), null), state);
    }

    [Fact]
    public void Reduce_SessionStartAfterCompact_KeepsTheActiveSubagents()
    {
        // Act
        SessionEventState state = ReduceAt(140,
            [Line(100, "UserPromptSubmit"), Line(110, "SubagentStart", agent: "sub-1"), Line(130, "SessionStart", source: "compact")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(100), At(100), 1), state);
    }

    [Fact]
    public void Reduce_SubagentEventsAfterSessionEnd_StayEnded()
    {
        // Act
        SessionEventState state = ReduceAt(130,
            [Line(100, "SessionEnd"), Line(110, "SubagentStart", agent: "sub-1"), Line(120, "PreToolUse", agent: "sub-1")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Ended, At(100), null), state);
    }

    [Fact]
    public void Reduce_SubagentToolEventWhileReady_ShowsWorkingWithoutStartingATurn()
    {
        // Act
        SessionEventState state = ReduceAt(150, [Line(100, "SessionStart"), Line(110, "PreToolUse", agent: "sub-1")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(110), null, 1), state);
    }

    private static SessionEventState ReduceAt(long now, IEnumerable<string> lines) => SessionEvents.Reduce(lines, At(now));

    [Theory]
    [InlineData("PostToolUse", "sub-1")]
    [InlineData("PostToolUseFailure", "sub-1")]
    [InlineData("PreToolUse", "sub-1")]
    public void Reduce_MainAgentPromptWithASubagentToolEvent_StaysNeedsInput(string toolEvent, string agent)
    {
        // Act
        SessionEventState state = ReduceAt(130,
            [Line(100, "UserPromptSubmit"), Line(110, "Notification", notification: Permission), Line(120, toolEvent, agent: agent)]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.NeedsInput, At(110), At(100), 1), state);
    }

    [Theory]
    [InlineData("PreToolUse", null)]
    [InlineData("PostToolUse", null)]
    [InlineData("PostToolUseFailure", null)]
    [InlineData("PostToolUse", "sub-2")]
    [InlineData("PostToolUseFailure", "sub-2")]
    public void Reduce_SubagentPromptWithAnotherAgentsToolEvent_StaysNeedsInput(string toolEvent, string? agent)
    {
        // Act
        SessionEventState state = ReduceAt(130,
            [Line(100, "UserPromptSubmit"), Line(110, "Notification", notification: Permission, agent: "sub-1"),
                Line(120, toolEvent, agent: agent)]);

        // Assert
        Assert.Equal(SessionStatus.NeedsInput, state.Status);
        Assert.Equal(At(110), state.Since);
    }

    [Theory]
    [InlineData("PostToolUse")]
    [InlineData("PostToolUseFailure")]
    public void Reduce_SubagentPromptThenThatSubagentsToolFinishes_ReturnsToTheInterruptedState(string toolEvent)
    {
        // Act
        SessionEventState state = ReduceAt(130,
            [Line(100, "UserPromptSubmit"), Line(110, "Notification", notification: Permission, agent: "sub-1"),
                Line(120, toolEvent, agent: "sub-1")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(120), At(100), 1), state);
    }

    [Fact]
    public void Reduce_SubagentPromptThenThatSubagentStops_ReturnsToTheInterruptedState()
    {
        // Act
        SessionEventState state = ReduceAt(140,
            [Line(100, "UserPromptSubmit"), Line(110, "Notification", notification: Permission, agent: "sub-1"),
                Line(130, "SubagentStop", agent: "sub-1")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(130), At(100)), state);
    }

    [Fact]
    public void Reduce_SubagentPromptThenAnotherSubagentStops_StaysNeedsInput()
    {
        // Act
        SessionEventState state = ReduceAt(140,
            [Line(100, "UserPromptSubmit"), Line(110, "Notification", notification: Permission, agent: "sub-1"),
                Line(115, "SubagentStart", agent: "sub-2"), Line(130, "SubagentStop", agent: "sub-2")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.NeedsInput, At(110), At(100), 1), state);
    }

    [Fact]
    public void Reduce_SubagentPromptThatItsSubagentNeverAnswers_ReleasesWhenTheSubagentExpires()
    {
        // Arrange
        string[] lines = [Line(100, "UserPromptSubmit"), Line(110, "Notification", notification: Permission, agent: "sub-1")];

        // Act
        SessionEventState waiting = ReduceAt(700, lines);
        SessionEventState released = ReduceAt(800, lines);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.NeedsInput, At(110), At(100), 1), waiting);
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(800), At(100)), released);
    }

    [Fact]
    public void Reduce_SubagentPromptWhileTheMainAgentWasIdleThenExpiry_ReturnsToReady()
    {
        // Act
        SessionEventState state = ReduceAt(900,
            [Line(100, "UserPromptSubmit"), Line(120, "Stop"), Line(130, "Notification", notification: Permission, agent: "sub-1")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Ready, At(900), At(100)), state);
    }

    [Fact]
    public void Reduce_SecondPromptWhileOneIsOpen_ShowsTheNewestAndDoesNotRestoreTheEarlierOne()
    {
        // Arrange: the main agent's prompt, then a subagent's
        string[] lines =
        [
            Line(100, "UserPromptSubmit"), Line(110, "Notification", notification: Permission),
            Line(120, "Notification", notification: Permission, agent: "sub-1"),
        ];

        // Act
        SessionEventState both = ReduceAt(125, lines);
        SessionEventState newestAnswered = ReduceAt(135, [.. lines, Line(130, "PostToolUse", agent: "sub-1")]);

        // Assert
        Assert.Equal(SessionStatus.NeedsInput, both.Status);
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(130), At(100), 1), newestAnswered);
    }

    [Fact]
    public void Reduce_MainPromptAfterASubagentPrompt_IsOwnedByTheMainAgent()
    {
        // Act
        SessionEventState state = ReduceAt(135,
            [Line(100, "UserPromptSubmit"), Line(110, "Notification", notification: Permission, agent: "sub-1"),
                Line(120, "Notification", notification: Permission), Line(130, "PostToolUse", agent: "sub-1")]);

        // Assert
        Assert.Equal(SessionStatus.NeedsInput, state.Status);
    }

    [Fact]
    public void Reduce_FutureDatedEvent_KeepsItsOwnTimeAsSince()
    {
        // Act
        SessionEventState state = ReduceAt(150, [Line(200, "UserPromptSubmit")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(200), At(200)), state);
    }

    [Fact]
    public void Reduce_SameLinesAtDifferentNows_KeepTheSameSinceWhileNothingChanges()
    {
        // Arrange
        string[] lines = [Line(200, "UserPromptSubmit"), Line(210, "Stop")];

        // Act
        SessionEventState early = ReduceAt(100, lines);
        SessionEventState later = ReduceAt(150, lines);
        SessionEventState muchLater = ReduceAt(5000, lines);

        // Assert
        Assert.Equal(At(210), early.Since);
        Assert.Equal(early, later);
        Assert.Equal(early, muchLater);
    }

    [Fact]
    public void Reduce_NowEarlierThanTheLastEvent_DoesNotBringBackAnExpiredSubagent()
    {
        // Arrange: the subagent expired by the last line, so an earlier now must not make it live again
        string[] lines =
        [
            Line(100, "UserPromptSubmit"), Line(110, "SubagentStart", agent: "sub-1"), Line(120, "Stop"), Line(2000, "Mystery"),
        ];

        // Act
        SessionEventState behind = ReduceAt(150, lines);
        SessionEventState ahead = ReduceAt(3000, lines);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Ready, At(710), At(100)), ahead);
        Assert.Equal(ahead, behind);
    }

    [Fact]
    public void Reduce_EventsOutOfOrder_KeepTheSinceOfTheStatusThatWasEntered()
    {
        // Act
        SessionEventState state = ReduceAt(300, [Line(200, "SessionStart"), Line(150, "Stop")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Ready, At(200), null), state);
    }

    [Fact]
    public void Reduce_ExpirySince_IsNeverAfterTheTimeComputedFor()
    {
        // Act
        SessionEventState state = ReduceAt(700,
            [Line(100, "UserPromptSubmit"), Line(700, "SubagentStart", agent: "sub-1"), Line(710, "Stop")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(100), At(100), 1), state);
    }

    [Fact]
    public void Reduce_OtherNotificationFromASubagent_DoesNotTakeOverAMainAgentPrompt()
    {
        // Arrange
        string[] lines =
        [
            Line(100, "PreToolUse"), Line(110, "Notification", notification: Permission),
            Line(120, "Notification", notification: "idle_prompt", agent: "sub-1"),
        ];

        // Act
        SessionEventState waiting = ReduceAt(125, lines);
        SessionEventState cleared = ReduceAt(135, [.. lines, Line(130, "PostToolUse")]);

        // Assert
        Assert.Equal(SessionStatus.NeedsInput, waiting.Status);
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(130), At(100), 1), cleared);
    }

    [Fact]
    public void Reduce_OtherNotificationFromTheMainAgent_DoesNotTakeOverASubagentPrompt()
    {
        // Arrange
        string[] lines =
        [
            Line(100, "UserPromptSubmit"), Line(110, "Notification", notification: Permission, agent: "sub-1"),
            Line(120, "Notification", notification: "idle_prompt"),
        ];

        // Act
        SessionEventState mainTool = ReduceAt(135, [.. lines, Line(130, "PostToolUse")]);
        SessionEventState subagentTool = ReduceAt(145, [.. lines, Line(130, "PostToolUse"), Line(140, "PostToolUse", agent: "sub-1")]);

        // Assert
        Assert.Equal(SessionStatus.NeedsInput, mainTool.Status);
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(140), At(100), 1), subagentTool);
    }

    [Fact]
    public void Reduce_ReleasingAPromptThatOpenedTheLog_GoesToWorkingNotToNoStatus()
    {
        // Act
        SessionEventState state = ReduceAt(130,
            [Line(110, "Notification", notification: Permission, agent: "sub-1"), Line(120, "PostToolUse", agent: "sub-1")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(120), At(110), 1), state);
    }

    [Fact]
    public void Reduce_ExpiryOfAPromptThatOpenedTheLog_GoesToWorking()
    {
        // Act
        SessionEventState state = ReduceAt(900, [Line(110, "Notification", notification: Permission, agent: "sub-1")]);

        // Assert
        Assert.Equal(new SessionEventState(SessionStatus.Working, At(900), At(110)), state);
    }

    // Far after every event, so a subagent that never stopped has expired.
    private static SessionEventState Reduce(IEnumerable<string> lines) => SessionEvents.Reduce(lines, At(1_000_000));

    private static string Line(long t, string e, string? agent = null, string? notification = null, string? source = null)
    {
        string extras = (agent is null ? "" : $",\"a\":\"{agent}\"")
            + (notification is null ? "" : $",\"n\":\"{notification}\"")
            + (source is null ? "" : $",\"s\":\"{source}\"");
        return $"{{\"t\":{t},\"e\":\"{e}\"{extras}}}";
    }

    private static DateTimeOffset At(long t) => DateTimeOffset.FromUnixTimeSeconds(t);
}
