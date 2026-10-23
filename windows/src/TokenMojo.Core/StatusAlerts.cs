namespace TokenMojo.Core;

public enum StatusAlertKind { NeedsInput, TurnFinished }

public sealed record StatusAlert(string SessionId, StatusAlertKind Kind, string Title, string Message);

public sealed record AlertBalloon(string Title, string Text);

/// <summary>What the last refresh saw of a session; Since tells two visits to the same status apart.</summary>
public sealed record SeenStatus(SessionStatus Status, DateTimeOffset? Since, DateTimeOffset? TurnStarted, DateTimeOffset LastSeen);

/// <summary>Decides which status changes between refreshes deserve a notification.</summary>
public static class StatusAlerts
{
    /// <param name="baseline">What earlier refreshes saw, from <see cref="Advance"/>; null before the first load, which is only a baseline.</param>
    public static IReadOnlyList<StatusAlert> Diff(
        IReadOnlyDictionary<string, SeenStatus>? baseline, IReadOnlyList<SessionSnapshot> current, TimeSpan longTurn)
    {
        List<StatusAlert> alerts = new();
        if (baseline is null) return alerts;
        foreach (SessionSnapshot session in NewestPerId(current).Values)
        {
            baseline.TryGetValue(session.Id, out SeenStatus? previous);
            bool changed = previous is null || previous.Status != session.Status || previous.Since != session.StatusSince;
            if (!changed) continue;
            if (session.Status == SessionStatus.NeedsInput)
                alerts.Add(new StatusAlert(session.Id, StatusAlertKind.NeedsInput, Dashboard.SessionTitle(session.State), "Needs input"));
            else if (previous is not null && session.Status == SessionStatus.Ready && FinishedLongTurn(session, longTurn))
                alerts.Add(new StatusAlert(session.Id, StatusAlertKind.TurnFinished, Dashboard.SessionTitle(session.State), "Finished"));
        }
        return alerts;
    }

    /// <summary>One balloon replaces the last, so the alerts of one refresh share it.</summary>
    public static AlertBalloon Balloon(IReadOnlyList<StatusAlert> alerts)
    {
        if (alerts.Count == 1) return new AlertBalloon(alerts[0].Title, alerts[0].Message);
        string title = alerts.All(a => a.Kind == StatusAlertKind.NeedsInput)
            ? $"{alerts.Count} sessions need you"
            : $"{alerts.Count} sessions updated";
        return new AlertBalloon(title, string.Join("\n", alerts.Select(a => $"{a.Title}: {a.Message}")));
    }

    /// <summary>The baseline after a refresh: sessions missing this time are kept, so a gap does not lose what they were doing, until unseen for the recent window.</summary>
    public static IReadOnlyDictionary<string, SeenStatus> Advance(
        IReadOnlyDictionary<string, SeenStatus>? baseline, IReadOnlyList<SessionSnapshot> current, DateTimeOffset now)
    {
        Dictionary<string, SeenStatus> next = (baseline ?? new Dictionary<string, SeenStatus>())
            .Where(entry => now - entry.Value.LastSeen < SessionStore.RecentWindow)
            .ToDictionary(entry => entry.Key, entry => entry.Value);
        foreach (SessionSnapshot session in NewestPerId(current).Values)
            next[session.Id] = new SeenStatus(session.Status, session.StatusSince, session.TurnStarted, now);
        return next;
    }

    // The same session id can come from two sources (say a Windows and a WSL copy); the most recently updated wins.
    private static Dictionary<string, SessionSnapshot> NewestPerId(IReadOnlyList<SessionSnapshot> sessions)
    {
        Dictionary<string, SessionSnapshot> byId = new();
        foreach (SessionSnapshot session in sessions)
        {
            if (!byId.TryGetValue(session.Id, out SessionSnapshot? kept) || session.Updated >= kept.Updated)
                byId[session.Id] = session;
        }
        return byId;
    }

    private static bool FinishedLongTurn(SessionSnapshot session, TimeSpan longTurn) =>
        session.StatusSince is { } since && session.TurnStarted is { } started && since - started >= longTurn;
}
