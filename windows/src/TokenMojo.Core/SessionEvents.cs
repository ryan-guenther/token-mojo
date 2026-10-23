using System.Collections.Immutable;
using System.Text.Json;

namespace TokenMojo.Core;

public sealed record SessionEventState(SessionStatus Status, DateTimeOffset? Since, DateTimeOffset? TurnStarted, int Subagents = 0);

/// <summary>Reduces the lines cctokmon-hook.sh appends to &lt;session_id&gt;.events to the session's current status.</summary>
public static class SessionEvents
{
    private static readonly HashSet<string> NeedsInputNotifications =
        ["permission_prompt", "elicitation_dialog", "elicitation_url_dialog", "agent_needs_input"];

    /// <summary>Applies the events in order. Lines that are not a complete event (a write in progress, foreign text) are skipped.</summary>
    public static SessionEventState Reduce(IEnumerable<string> lines)
    {
        Tracking tracking = new(new SessionEventState(SessionStatus.None, null, null), [], false);
        foreach (string line in lines)
        {
            if (TryParse(line) is { } hookEvent)
                tracking = Apply(tracking, hookEvent);
        }
        return tracking.State;
    }

    // The subagent ids are what the 64 KB tail window can cut: a stop for an id it never saw is ignored.
    private static Tracking Apply(Tracking tracking, HookEvent hookEvent)
    {
        SessionEventState state = tracking.State;
        DateTimeOffset t = hookEvent.Time;
        bool ended = state.Status == SessionStatus.Ended;
        switch (hookEvent.Name)
        {
            case "SessionStart" when hookEvent.Source != "compact":
            case "SessionEnd":
                return Resolve(tracking, ApplyToStatus(state, hookEvent), []);
            case "SubagentStart" when !ended && hookEvent.Agent is { } started:
                return Resolve(tracking, state, tracking.Active.Add(started));
            case "SubagentStop" when !ended && hookEvent.Agent is { } stopped && tracking.Active.Contains(stopped):
                ImmutableHashSet<string> remaining = tracking.Active.Remove(stopped);
                // The turn the main agent already ended is over once its last subagent is.
                return remaining.IsEmpty && tracking.MainIdle
                    ? Resolve(tracking, Enter(state, SessionStatus.Ready, t, state.TurnStarted), remaining)
                    : Resolve(tracking, state, remaining);
            case "Stop" when !ended && !tracking.Active.IsEmpty:
                return Resolve(tracking with { MainIdle = true }, Enter(state, SessionStatus.Working, t, state.TurnStarted), tracking.Active);
            case "UserPromptSubmit" or "PreToolUse" or "PostToolUse" or "PostToolUseFailure" when hookEvent.Agent is null && !ended:
                return Resolve(tracking with { MainIdle = false }, ApplyToStatus(state, hookEvent), tracking.Active);
            default:
                return Resolve(tracking, ApplyToStatus(state, hookEvent), tracking.Active);
        }
    }

    private static Tracking Resolve(Tracking tracking, SessionEventState state, ImmutableHashSet<string> active) =>
        tracking with { State = state with { Subagents = active.Count }, Active = active, MainIdle = tracking.MainIdle && !active.IsEmpty };

    private static SessionEventState ApplyToStatus(SessionEventState state, HookEvent hookEvent)
    {
        DateTimeOffset t = hookEvent.Time;
        bool ended = state.Status == SessionStatus.Ended;
        return hookEvent.Name switch
        {
            // A compact reloads the context mid-turn, so it is not a new session.
            "SessionStart" when hookEvent.Source == "compact" => state,
            "SessionStart" => Enter(state, SessionStatus.Ready, t, null),
            // A resumed session sends SessionStart first, so a prompt does not revive an ended one.
            "UserPromptSubmit" when ended => state,
            // A subagent's result arrives as another prompt in the middle of the turn.
            "UserPromptSubmit" when state.Status is SessionStatus.Working or SessionStatus.NeedsInput =>
                Enter(state, SessionStatus.Working, t, state.TurnStarted),
            "UserPromptSubmit" => Enter(state, SessionStatus.Working, t, t),
            "PreToolUse" or "PostToolUse" or "PostToolUseFailure" => ToolEvent(state, hookEvent),
            "Notification" when !ended && hookEvent.Notification is { } type && NeedsInputNotifications.Contains(type) =>
                Enter(state, SessionStatus.NeedsInput, t, StartsTurn(state) ? t : state.TurnStarted),
            "Stop" when !ended => Enter(state, SessionStatus.Ready, t, state.TurnStarted),
            "SessionEnd" => Enter(state, SessionStatus.Ended, t, null),
            _ => state,
        };
    }

    private static SessionEventState ToolEvent(SessionEventState state, HookEvent hookEvent)
    {
        if (state.Status == SessionStatus.Ended) return state;
        // A subagent starting a tool says nothing about the prompt the main agent is waiting on.
        if (hookEvent.Name == "PreToolUse" && state.Status == SessionStatus.NeedsInput && hookEvent.Agent is not null)
            return state;
        // Ready or no status means this tool starts a turn: a background wake, or a turn whose start was cut off.
        return Enter(state, SessionStatus.Working, hookEvent.Time, StartsTurn(state) ? hookEvent.Time : state.TurnStarted);
    }

    private static bool StartsTurn(SessionEventState state) => state.Status is SessionStatus.Ready or SessionStatus.None;

    // Since is when the status was entered, so an event that leaves the status as it was keeps it.
    private static SessionEventState Enter(SessionEventState state, SessionStatus status, DateTimeOffset t, DateTimeOffset? turnStarted) =>
        new(status, state.Status == status ? state.Since : t, turnStarted);

    private static HookEvent? TryParse(string line)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("t", out JsonElement time) || time.ValueKind != JsonValueKind.Number || !time.TryGetInt64(out long seconds))
                return null;
            if (!root.TryGetProperty("e", out JsonElement name) || name.ValueKind != JsonValueKind.String) return null;
            return new HookEvent(
                DateTimeOffset.FromUnixTimeSeconds(seconds), name.GetString()!, Text(root, "n"), Text(root, "s"), Text(root, "a"));
        }
        catch (Exception e) when (e is JsonException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement root, string property) =>
        root.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text
            : null;

    private sealed record Tracking(SessionEventState State, ImmutableHashSet<string> Active, bool MainIdle);

    private sealed record HookEvent(DateTimeOffset Time, string Name, string? Notification, string? Source, string? Agent);
}
