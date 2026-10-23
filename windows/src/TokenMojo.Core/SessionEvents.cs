using System.Collections.Immutable;
using System.Text.Json;

namespace TokenMojo.Core;

public sealed record SessionEventState(SessionStatus Status, DateTimeOffset? Since, DateTimeOffset? TurnStarted, int Subagents = 0);

/// <summary>Reduces the lines cctokmon-hook.sh appends to &lt;session_id&gt;.events to the session's current status.</summary>
public static class SessionEvents
{
    private static readonly TimeSpan SubagentLifetime = TimeSpan.FromMinutes(10);

    private static readonly HashSet<string> NeedsInputNotifications =
        ["permission_prompt", "elicitation_dialog", "elicitation_url_dialog", "agent_needs_input"];

    /// <summary>
    /// Applies the events in order. Lines that are not a complete event (a write in progress, foreign text) are skipped.
    /// The main agent's own events drive its state; subagents only keep the session shown as working while they are live.
    /// </summary>
    public static SessionEventState Reduce(IEnumerable<string> lines, DateTimeOffset now)
    {
        SessionEventState none = new(SessionStatus.None, null, null);
        Tracking tracking = new(none, ImmutableDictionary<string, DateTimeOffset>.Empty, none);
        foreach (string line in lines)
        {
            if (TryParse(line) is { } hookEvent)
                tracking = Apply(Settle(tracking, hookEvent.Time), hookEvent);
        }
        tracking = Settle(tracking, now);
        return tracking.Shown with { Subagents = LiveCount(tracking, now) };
    }

    private static Tracking Apply(Tracking tracking, HookEvent hookEvent)
    {
        DateTimeOffset t = hookEvent.Time;
        if (hookEvent.Name == "SessionEnd" || (hookEvent.Name == "SessionStart" && hookEvent.Source != "compact"))
            tracking = tracking with { Subagents = tracking.Subagents.Clear() };
        else if (tracking.Main.Status == SessionStatus.Ended)
            return Show(tracking, t, t);
        else if (hookEvent.Name == "SubagentStop" && hookEvent.Agent is { } stopped)
            tracking = tracking with { Subagents = tracking.Subagents.Remove(stopped) };
        else if (hookEvent.Agent is { } seen)
            tracking = tracking with { Subagents = tracking.Subagents.SetItem(seen, t) };

        SessionEventState main = tracking.Main;
        SessionEventState next = NextMain(tracking, hookEvent);
        string? promptAgent = tracking.PromptAgent;
        SessionEventState? interrupted = tracking.Interrupted;
        if (next.Status != SessionStatus.NeedsInput)
            (promptAgent, interrupted) = (null, null);
        else if (hookEvent.Name == "Notification")
        {
            // A second prompt replaces the first as the one shown and owned; the first is not restored when it clears.
            promptAgent = hookEvent.Agent;
            if (main.Status != SessionStatus.NeedsInput) interrupted = main;
        }

        return Show(tracking with { Main = next, PromptAgent = promptAgent, Interrupted = interrupted }, t, t);
    }

    // Subagents never change the main state. They only release a prompt of their own, and a prompt the main agent
    // raised is cleared only by the main agent's own tool events.
    private static SessionEventState NextMain(Tracking tracking, HookEvent hookEvent)
    {
        SessionEventState main = tracking.Main;
        bool subagentPrompt = main.Status == SessionStatus.NeedsInput && tracking.PromptAgent is not null;
        bool toolEvent = hookEvent.Name is "PreToolUse" or "PostToolUse" or "PostToolUseFailure";
        bool toolFinished = hookEvent.Name is "PostToolUse" or "PostToolUseFailure";
        return hookEvent.Name switch
        {
            "Notification" => ApplyToStatus(main, hookEvent),
            "SubagentStop" when subagentPrompt && hookEvent.Agent == tracking.PromptAgent => Release(tracking, hookEvent.Time),
            _ when hookEvent.Name.StartsWith("Subagent", StringComparison.Ordinal) => main,
            _ when toolFinished && subagentPrompt && hookEvent.Agent == tracking.PromptAgent => Release(tracking, hookEvent.Time),
            _ when hookEvent.Agent is not null => main,
            _ when toolEvent && subagentPrompt => main,
            _ => ApplyToStatus(main, hookEvent),
        };
    }

    // Back to what the prompt interrupted.
    private static SessionEventState Release(Tracking tracking, DateTimeOffset t) =>
        tracking.Interrupted ?? Enter(tracking.Main, SessionStatus.Working, t, tracking.Main.TurnStarted);

    // An id not heard from for ten minutes counts as finished, so a lost SubagentStop cannot pin the session.
    private static Tracking Settle(Tracking tracking, DateTimeOffset at)
    {
        if (tracking.Main.Status == SessionStatus.NeedsInput && tracking.PromptAgent is { } asker
            && !(tracking.Subagents.TryGetValue(asker, out DateTimeOffset askerSeen) && at - askerSeen < SubagentLifetime))
            tracking = tracking with { Main = Release(tracking, at), PromptAgent = null, Interrupted = null };
        // Only working to ready by expiry is dated by the expiry itself; any other change found here happened by now.
        bool expired = tracking.Shown.Status == SessionStatus.Working && ShownStatus(tracking, at) == SessionStatus.Ready
            && !tracking.Subagents.IsEmpty;
        return Show(tracking, at, expired ? tracking.Subagents.Values.Max() + SubagentLifetime : at);
    }

    // Since is never after the moment the state was computed for.
    private static Tracking Show(Tracking tracking, DateTimeOffset at, DateTimeOffset changedAt)
    {
        SessionStatus status = ShownStatus(tracking, at);
        // Since is when the shown status was entered, so an event that leaves it as it was keeps it.
        DateTimeOffset since = (status == tracking.Shown.Status ? tracking.Shown.Since : changedAt) ?? changedAt;
        DateTimeOffset clamped = since < at ? since : at;
        return tracking with { Shown = new SessionEventState(status, status == SessionStatus.None ? null : clamped, tracking.Main.TurnStarted) };
    }

    private static SessionStatus ShownStatus(Tracking tracking, DateTimeOffset at) =>
        tracking.Main.Status == SessionStatus.Ready && LiveCount(tracking, at) > 0 ? SessionStatus.Working : tracking.Main.Status;

    private static int LiveCount(Tracking tracking, DateTimeOffset at) =>
        tracking.Subagents.Values.Count(lastSeen => at - lastSeen < SubagentLifetime);

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

    // Main is the main agent's state, Shown what the row displays. PromptAgent is the subagent that raised the open
    // prompt (null for the main agent) and Interrupted the state that prompt interrupted.
    private sealed record Tracking(
        SessionEventState Main, ImmutableDictionary<string, DateTimeOffset> Subagents, SessionEventState Shown,
        string? PromptAgent = null, SessionEventState? Interrupted = null);

    private sealed record HookEvent(DateTimeOffset Time, string Name, string? Notification, string? Source, string? Agent);
}
