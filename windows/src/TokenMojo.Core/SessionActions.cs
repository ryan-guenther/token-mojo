namespace TokenMojo.Core;

public enum SessionAction { Focus, ConfirmResume, Resume }

/// <summary>What clicking a session row does.</summary>
public static class SessionActions
{
    /// <param name="windowFound">Whether a terminal window carrying the session's tag is open.</param>
    public static SessionAction Decide(SessionStatus status, bool windowFound)
    {
        if (windowFound) return SessionAction.Focus;
        // A session with no status may be running without hooks, so it is asked about like a running one.
        return status == SessionStatus.Ended ? SessionAction.Resume : SessionAction.ConfirmResume;
    }
}
