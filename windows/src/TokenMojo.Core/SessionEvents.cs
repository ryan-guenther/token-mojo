using System.Text.Json;

namespace TokenMojo.Core;

public sealed record SessionEventState(SessionStatus Status, DateTimeOffset? Since, DateTimeOffset? TurnStarted);

/// <summary>Reduces the lines cctokmon-hook.sh appends to &lt;session_id&gt;.events to the session's current status.</summary>
public static class SessionEvents
{
    private static readonly HashSet<string> NeedsInputNotifications =
        ["permission_prompt", "elicitation_dialog", "elicitation_url_dialog", "agent_needs_input"];

    /// <summary>Applies the events in order. Lines that are not a complete event (a write in progress, foreign text) are skipped.</summary>
    public static SessionEventState Reduce(IEnumerable<string> lines)
    {
        SessionEventState state = new(SessionStatus.None, null, null);
        foreach (string line in lines)
        {
            if (TryParse(line) is { } hookEvent)
                state = Apply(state, hookEvent);
        }
        return state;
    }

    private static SessionEventState Apply(SessionEventState state, HookEvent hookEvent)
    {
        DateTimeOffset t = hookEvent.Time;
        bool ended = state.Status == SessionStatus.Ended;
        return hookEvent.Name switch
        {
            // A compact reloads the context mid-turn, so it is not a new session.
            "SessionStart" when hookEvent.Source == "compact" => state,
            "SessionStart" => Enter(state, SessionStatus.Ready, t, null),
            "UserPromptSubmit" => Enter(state, SessionStatus.Working, t, t),
            // The prompt that raised needs-input is still open until a tool finishes.
            "PreToolUse" when state.Status is SessionStatus.NeedsInput or SessionStatus.Ended => state,
            "PreToolUse" or "PostToolUse" when state.Status == SessionStatus.Ready => Enter(state, SessionStatus.Working, t, t),
            "PostToolUse" when ended => state,
            "PreToolUse" or "PostToolUse" => Enter(state, SessionStatus.Working, t, state.TurnStarted),
            "Notification" when !ended && hookEvent.Notification is { } type && NeedsInputNotifications.Contains(type) =>
                Enter(state, SessionStatus.NeedsInput, t, state.TurnStarted),
            "Stop" when !ended => Enter(state, SessionStatus.Ready, t, state.TurnStarted),
            "SessionEnd" => Enter(state, SessionStatus.Ended, t, null),
            _ => state,
        };
    }

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
                DateTimeOffset.FromUnixTimeSeconds(seconds), name.GetString()!, Text(root, "n"), Text(root, "s"));
        }
        catch (Exception e) when (e is JsonException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string? Text(JsonElement root, string property) =>
        root.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private sealed record HookEvent(DateTimeOffset Time, string Name, string? Notification, string? Source);
}
