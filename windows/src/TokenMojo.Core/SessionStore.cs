using System.Text.Json;

namespace TokenMojo.Core;

public sealed record SessionSnapshot(
    string Id, BridgeState State, DateTimeOffset Updated,
    SessionStatus Status = SessionStatus.None, DateTimeOffset? StatusSince = null, DateTimeOffset? TurnStarted = null,
    string? Distro = null, string? User = null, string? Home = null, int Subagents = 0)
{
    /// <summary>The folder to resume or open: where Claude was started, else where it is now.</summary>
    public string? ResumeFolder =>
        string.IsNullOrEmpty(State.Workspace?.ProjectDir) ? State.Workspace?.CurrentDir : State.Workspace.ProjectDir;

    public bool IsActive(DateTimeOffset now) => now - Updated < TimeSpan.FromMinutes(10);
}

public sealed record LoadedState(BridgeState State, DateTimeOffset Updated, ClaudeSource Source);

public static class SessionStore
{
    public static readonly TimeSpan RecentWindow = TimeSpan.FromHours(12);
    private const int EventsTailBytes = 64 * 1024;

    /// <summary>The newest state file across sources; rate limits are per account, so the latest write wins.</summary>
    public static LoadedState? LoadCurrent(IEnumerable<ClaudeSource> sources)
    {
        (ClaudeSource source, FileInfo file) newest = sources
            .Select(source => (source, file: new FileInfo(source.StateFile)))
            .Where(candidate => candidate.file.Exists)
            .OrderByDescending(candidate => candidate.file.LastWriteTimeUtc)
            .FirstOrDefault();
        if (newest.file is null) return null;
        return new LoadedState(
            BridgeState.Parse(ReadShared(newest.file.FullName)),
            new DateTimeOffset(newest.file.LastWriteTimeUtc),
            newest.source);
    }

    // Claude Code signals no session end, so "recent" is the best proxy for "open".
    public static IReadOnlyList<SessionSnapshot> LoadSessions(IEnumerable<ClaudeSource> sources, DateTimeOffset now)
    {
        DateTimeOffset cutoff = now - RecentWindow;
        Dictionary<string, Candidate> newestById = new();
        foreach (ClaudeSource source in sources)
        {
            foreach (Candidate candidate in LoadSource(source, cutoff))
            {
                // The same session can be on two sources (say a Windows and a WSL copy): keep the newest, so
                // rows, counts and alerts all see one.
                if (!newestById.TryGetValue(candidate.Session.Id, out Candidate? kept) || candidate.IsNewerThan(kept))
                    newestById[candidate.Session.Id] = candidate;
            }
        }
        return newestById.Values.Select(c => c.Session).OrderByDescending(s => s.Updated).ToList();
    }

    private static List<Candidate> LoadSource(ClaudeSource source, DateTimeOffset cutoff)
    {
        List<Candidate> sessions = new();
        DirectoryInfo dir = new(source.SessionsDir);
        // The directory appears after the first bridge write.
        if (!dir.Exists) return sessions;
        try
        {
            foreach (FileInfo file in dir.EnumerateFiles("*.json"))
            {
                DateTimeOffset updated = new(file.LastWriteTimeUtc);
                if (updated <= cutoff) continue;
                if (TryRead(file) is not { } state) continue;
                // An events file that is there but unreadable is skipped until the next refresh, like the bridge file.
                if (!TryReadEvents(file, out SessionEventState? events, out DateTime eventsWritten)) continue;
                sessions.Add(new Candidate(
                    new SessionSnapshot(
                        Path.GetFileNameWithoutExtension(file.Name), state, updated,
                        events?.Status ?? SessionStatus.None, events?.Since, events?.TurnStarted, source.Distro, source.User, source.Home,
                        events?.Subagents ?? 0),
                    eventsWritten));
            }
        }
        // One unreadable source (a WSL distro that stopped mid-read) must not hide the others.
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
        return sessions;
    }

    private static BridgeState? TryRead(FileInfo file)
    {
        try
        {
            return BridgeState.Parse(ReadShared(file.FullName));
        }
        // Writes are atomic renames, so an undecodable file is foreign; one that vanished or is locked
        // is caught mid-replace and is read again on the next refresh.
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // The hook script appends to <id>.events beside <id>.json. No file means a session without hooks (null events);
    // false means the file is there but could not be read. Lines that are not events never fail the read.
    private static bool TryReadEvents(FileInfo sessionFile, out SessionEventState? events, out DateTime written)
    {
        events = null;
        written = DateTime.MinValue;
        string path = Path.ChangeExtension(sessionFile.FullName, ".events");
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            written = File.GetLastWriteTimeUtc(path);
            long start = Math.Max(0, stream.Length - EventsTailBytes);
            stream.Seek(start, SeekOrigin.Begin);
            using StreamReader reader = new(stream);
            IEnumerable<string> lines = reader.ReadToEnd().Split('\n');
            // Starting mid-file lands inside a line, which is only part of an event.
            events = SessionEvents.Reduce(start > 0 ? lines.Skip(1) : lines);
            return true;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private sealed record Candidate(SessionSnapshot Session, DateTime EventsWritten)
    {
        // Ties on the bridge file are broken by whichever copy's events were written last.
        public bool IsNewerThan(Candidate other) =>
            (Session.Updated, EventsWritten).CompareTo((other.Session.Updated, other.EventsWritten)) > 0;
    }

    // FileShare.Delete lets the bridge rename a new file over this one while it is being read on Windows.
    private static string ReadShared(string path)
    {
        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }
}
