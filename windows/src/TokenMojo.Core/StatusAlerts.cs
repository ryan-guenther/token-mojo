namespace TokenMojo.Core;

public enum StatusAlertKind { NeedsInput, TurnFinished }

public sealed record StatusAlert(string SessionId, StatusAlertKind Kind, string Title, string Message);

/// <summary>Decides which status changes between two refreshes deserve a notification.</summary>
public static class StatusAlerts
{
    /// <param name="previous">The sessions at the last refresh; null on the first load, which is only a baseline.</param>
    public static IReadOnlyList<StatusAlert> Diff(
        IReadOnlyList<SessionSnapshot>? previous, IReadOnlyList<SessionSnapshot> current, TimeSpan longTurn)
    {
        List<StatusAlert> alerts = new();
        if (previous is null) return alerts;
        Dictionary<string, SessionStatus> before = previous.ToDictionary(s => s.Id, s => s.Status);
        foreach (SessionSnapshot session in current)
        {
            SessionStatus was = before.GetValueOrDefault(session.Id, SessionStatus.None);
            if (session.Status == SessionStatus.NeedsInput && was != SessionStatus.NeedsInput)
                alerts.Add(new StatusAlert(session.Id, StatusAlertKind.NeedsInput, Dashboard.SessionTitle(session.State), "Needs input"));
            else if (was == SessionStatus.Working && session.Status == SessionStatus.Ready && FinishedLongTurn(session, longTurn))
                alerts.Add(new StatusAlert(session.Id, StatusAlertKind.TurnFinished, Dashboard.SessionTitle(session.State), "Finished"));
        }
        return alerts;
    }

    private static bool FinishedLongTurn(SessionSnapshot session, TimeSpan longTurn) =>
        session.StatusSince is { } since && session.TurnStarted is { } started && since - started >= longTurn;
}
