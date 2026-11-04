namespace TokenMojo.Core;

/// <param name="Session">The session to focus or resume; null when <paramref name="Problem"/> is set.</param>
/// <param name="Problem">Why the link does nothing.</param>
public sealed record LinkFocusResult(SessionSnapshot? Session, string? Problem);

/// <summary>Finds the session a focus link names among the open ones in the session list.</summary>
public static class LinkFocus
{
    public static LinkFocusResult Find(FocusLink link, IReadOnlyList<SessionSnapshot> sessions, DateTimeOffset now)
    {
        string id8 = link.SessionId[..8];
        SessionSnapshot? session = sessions.FirstOrDefault(candidate => string.Equals(candidate.Id, link.SessionId, StringComparison.OrdinalIgnoreCase));
        if (session is null) return new LinkFocusResult(null, $"Session {id8} is not in the session list.");
        return Dashboard.IsClosed(session, now)
            ? new LinkFocusResult(null, $"Session {id8} is closed. Resume it from the session list.")
            : new LinkFocusResult(session, null);
    }
}
