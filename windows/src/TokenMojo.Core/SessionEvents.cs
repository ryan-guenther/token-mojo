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
        Tracking tracking = new(none, ImmutableDictionary<string, DateTimeOffset>.Empty, null, none);
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
        SessionEventState main = tracking.Main;
        bool ended = main.Status == SessionStatus.Ended;
        ImmutableDictionary<string, DateTimeOffset> subagents = tracking.Subagents;
        bool isSubagentEvent = hookEvent.Agent is not null && hookEvent.Name != "Notification";

        if (hookEvent.Name == "SessionEnd" || (hookEvent.Name == "SessionStart" && hookEvent.Source != "compact"))
            subagents = subagents.Clear();
        else if (ended)
            return Show(tracking, t, t);
        else if (hookEvent.Name == "SubagentStop" && hookEvent.Agent is { } stopped)
            subagents = subagents.Remove(stopped);
        else if (hookEvent.Agent is { } seen)
            subagents = subagents.SetItem(seen, t);

        SessionEventState next = main;
        SessionEventState? idle = tracking.IdleBeforePrompt;
        if (!isSubagentEvent)
            next = ApplyToStatus(main, hookEvent);
        // The prompt a subagent's tool was waiting on is answered; the main agent had stopped before it came up.
        else if (hookEvent.Name is "PostToolUse" or "PostToolUseFailure" && main.Status == SessionStatus.NeedsInput)
            next = idle ?? Enter(main, SessionStatus.Working, t, main.TurnStarted);

        if (next.Status != SessionStatus.NeedsInput)
            idle = null;
        else if (main.Status != SessionStatus.NeedsInput)
            idle = main.Status == SessionStatus.Ready ? main : null;

        return Show(tracking with { Main = next, Subagents = subagents, IdleBeforePrompt = idle }, t, t);
    }

    // An id not heard from for ten minutes counts as finished, so a lost SubagentStop cannot pin the session.
    private static Tracking Settle(Tracking tracking, DateTimeOffset at)
    {
        if (ShownStatus(tracking, at) == tracking.Shown.Status) return tracking;
        // Only an expiry can change the display with no event, and it happened when the last live id timed out.
        DateTimeOffset expiredAt = tracking.Subagents.Values.Max() + SubagentLifetime;
        return Show(tracking, at, expiredAt);
    }

    private static Tracking Show(Tracking tracking, DateTimeOffset at, DateTimeOffset changedAt)
    {
        SessionStatus status = ShownStatus(tracking, at);
        // Since is when the shown status was entered, so an event that leaves it as it was keeps it.
        DateTimeOffset? since = status == tracking.Shown.Status ? tracking.Shown.Since : changedAt;
        return tracking with { Shown = new SessionEventState(status, since, tracking.Main.TurnStarted) };
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

    // Main is the main agent's state, Shown what the row displays; IdleBeforePrompt is the Ready state a question interrupted.
    private sealed record Tracking(
        SessionEventState Main, ImmutableDictionary<string, DateTimeOffset> Subagents, SessionEventState? IdleBeforePrompt, SessionEventState Shown);

    private sealed record HookEvent(DateTimeOffset Time, string Name, string? Notification, string? Source, string? Agent);
}
