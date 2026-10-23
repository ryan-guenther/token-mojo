namespace TokenMojo.Core;

public enum SessionAction { Focus, ConfirmResume, Resume, Unavailable }

/// <summary>What clicking a session row does.</summary>
public static class SessionActions
{
    /// <param name="distro">The session's WSL distro; null for the Windows source, whose sessions cannot be resumed.</param>
    /// <param name="windowFound">Whether a terminal window carrying the session's tag is open.</param>
    public static SessionAction Decide(SessionStatus status, string? distro, bool windowFound)
    {
        if (windowFound) return SessionAction.Focus;
        if (distro is null) return SessionAction.Unavailable;
        // A session with no status may be running without hooks, so it is asked about like a running one.
        return status == SessionStatus.Ended ? SessionAction.Resume : SessionAction.ConfirmResume;
    }
}
