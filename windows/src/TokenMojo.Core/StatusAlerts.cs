namespace TokenMojo.Core;

public enum StatusAlertKind { NeedsInput, TurnFinished }

public sealed record StatusAlert(string SessionId, StatusAlertKind Kind, string Title, string Message);

public sealed record AlertBalloon(string Title, string Text);

/// <summary>What the last refresh saw of a session; Since tells two visits to the same status apart, FinishedTurn is the turn already announced as finished.</summary>
public sealed record SeenStatus(
    SessionStatus Status, DateTimeOffset? Since, DateTimeOffset? TurnStarted, DateTimeOffset LastSeen, DateTimeOffset? FinishedTurn = null);

/// <summary>Decides which status changes between refreshes deserve a notification. Session ids are unique in the lists it is given.</summary>
public static class StatusAlerts
{
    // Windows notification limits: a title under 64 characters and a body under 256.
    private const int MaxTitleLength = 62;
    private const int MaxTextLength = 254;
    private const int MaxListedSessions = 3;

    /// <param name="baseline">What earlier refreshes saw, from <see cref="Advance"/>; null before the first load, which is only a baseline.</param>
    public static IReadOnlyList<StatusAlert> Diff(
        IReadOnlyDictionary<string, SeenStatus>? baseline, IReadOnlyList<SessionSnapshot> current, TimeSpan longTurn)
    {
        List<StatusAlert> alerts = new();
        if (baseline is null) return alerts;
        foreach (SessionSnapshot session in current)
        {
            baseline.TryGetValue(session.Id, out SeenStatus? previous);
            bool changed = previous is null || previous.Status != session.Status || previous.Since != session.StatusSince;
            if (!changed) continue;
            if (session.Status == SessionStatus.NeedsInput)
                alerts.Add(new StatusAlert(session.Id, StatusAlertKind.NeedsInput, Dashboard.SessionTitle(session.State), "Needs input"));
            else if (previous is not null && session.Status == SessionStatus.Ready && FinishedLongTurn(session, longTurn)
                && session.TurnStarted != previous.FinishedTurn)
                alerts.Add(new StatusAlert(session.Id, StatusAlertKind.TurnFinished, Dashboard.SessionTitle(session.State), "Finished"));
        }
        return alerts;
    }

    /// <summary>One balloon replaces the last, so the alerts of one refresh share it.</summary>
    public static AlertBalloon Balloon(IReadOnlyList<StatusAlert> alerts)
    {
        if (alerts.Count == 1) return new AlertBalloon(Clip(alerts[0].Title, MaxTitleLength), alerts[0].Message);
        string title = alerts.All(a => a.Kind == StatusAlertKind.NeedsInput)
            ? $"{alerts.Count} sessions need you"
            : $"{alerts.Count} sessions updated";
        List<string> lines = alerts.Take(MaxListedSessions).Select(a => $"{Clip(a.Title, 40)}: {a.Message}").ToList();
        if (alerts.Count > MaxListedSessions) lines.Add($"and {alerts.Count - MaxListedSessions} more");
        return new AlertBalloon(title, Clip(string.Join("\n", lines), MaxTextLength));
    }

    /// <summary>The baseline after a refresh: sessions missing this time are kept, so a gap does not lose what they were doing, until unseen for the session list's window.</summary>
    public static IReadOnlyDictionary<string, SeenStatus> Advance(
        IReadOnlyDictionary<string, SeenStatus>? baseline, IReadOnlyList<SessionSnapshot> current, DateTimeOffset now, TimeSpan longTurn,
        TimeSpan window)
    {
        Dictionary<string, SeenStatus> next = (baseline ?? new Dictionary<string, SeenStatus>())
            .Where(entry => now - entry.Value.LastSeen < window)
            .ToDictionary(entry => entry.Key, entry => entry.Value);
        foreach (SessionSnapshot session in current)
        {
            // A session reading as no status (its events file was briefly absent) must not forget what it was doing.
            if (session.Status == SessionStatus.None && next.TryGetValue(session.Id, out SeenStatus? known) && known.Status != SessionStatus.None)
                next[session.Id] = known with { LastSeen = now };
            else
            {
                // A turn can show ready, working and ready again, so the turn announced is remembered, not just the status.
                next.TryGetValue(session.Id, out SeenStatus? earlier);
                DateTimeOffset? finishedTurn = session.Status == SessionStatus.Ready && FinishedLongTurn(session, longTurn)
                    ? session.TurnStarted
                    : earlier?.FinishedTurn;
                next[session.Id] = new SeenStatus(session.Status, session.StatusSince, session.TurnStarted, now, finishedTurn);
            }
        }
        return next;
    }

    private static string Clip(string text, int maxLength) => text.Length <= maxLength ? text : text[..(maxLength - 1)] + "…";

    private static bool FinishedLongTurn(SessionSnapshot session, TimeSpan longTurn) =>
        session.StatusSince is { } since && session.TurnStarted is { } started && since - started >= longTurn;
}
