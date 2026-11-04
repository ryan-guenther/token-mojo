using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TokenMojo.Core;

/// <summary>One session as sessions.json lists it.</summary>
/// <param name="Repository">owner/name, only for a GitHub repository.</param>
/// <param name="PullRequest">Set only for a GitHub repository's worktree named exactly pr-&lt;number&gt;.</param>
public sealed record SnapshotSession(
    string Id, string Title, SessionStatus Status, DateTimeOffset? StatusSince, bool Open, string? Distro, string? Folder,
    string? Repository, string? Worktree, PullRequestRef? PullRequest);

/// <summary>
/// sessions.json: every listed session and whether it is open, for other local tools to read rather than work out
/// session status themselves.
/// </summary>
public static partial class SessionSnapshotFile
{
    public const int Version = 1;

    /// <summary>How often the file is rewritten while nothing changes, so a reader can tell from written that TokenMojo runs.</summary>
    public static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(60);

    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        NewLine = "\n",
        // A local file, never embedded in a page, so titles stay readable.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The sessions in the dashboard's order: open ones first, then closed ones.</summary>
    public static IReadOnlyList<SnapshotSession> Entries(IReadOnlyList<SessionSnapshot> sessions, DateTimeOffset now)
    {
        (IReadOnlyList<SessionSnapshot> open, IReadOnlyList<SessionSnapshot> closed) = Dashboard.Ordered(sessions, now);
        return open.Select(session => Entry(session, open: true))
            .Concat(closed.Select(session => Entry(session, open: false)))
            .ToList();
    }

    /// <summary>The file's text: camelCase properties in a fixed order, times in UTC.</summary>
    public static string Build(IReadOnlyList<SnapshotSession> entries, DateTimeOffset written)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, WriterOptions))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", Version);
            writer.WriteString("written", UtcText(written));
            writer.WriteStartArray("sessions");
            foreach (SnapshotSession entry in entries) WriteEntry(writer, entry);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Write when the sessions changed, or when the last write is a heartbeat old.</summary>
    public static bool ShouldWrite(
        IReadOnlyList<SnapshotSession>? previous, IReadOnlyList<SnapshotSession> current, DateTimeOffset? lastWritten, DateTimeOffset now) =>
        previous is null || lastWritten is null || !previous.SequenceEqual(current) || now - lastWritten.Value >= Heartbeat;

    /// <summary>Writes through a temp file beside the target, then moves it over, so a reader never sees half a file.</summary>
    public static void Write(string path, string content)
    {
        string temp = path + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllText(temp, content);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            File.Delete(temp);
            throw;
        }
    }

    private static SnapshotSession Entry(SessionSnapshot session, bool open)
    {
        BridgeRepo? repo = session.State.Workspace?.Repo;
        bool onGitHub = repo is { Owner: { Length: > 0 }, Name: { Length: > 0 } }
            && string.Equals(repo.Host, "github.com", StringComparison.OrdinalIgnoreCase);
        string? worktree = session.State.Worktree?.Name;
        Match pullRequest = worktree is null ? Match.Empty : PullRequestWorktree().Match(worktree);
        return new SnapshotSession(
            session.Id,
            Dashboard.SessionTitle(session),
            session.Status,
            session.StatusSince,
            open,
            session.Distro,
            session.ResumeFolder,
            onGitHub ? $"{repo!.Owner}/{repo.Name}" : null,
            worktree,
            onGitHub && pullRequest.Success
                ? new PullRequestRef(repo!.Owner!, repo.Name!, int.Parse(pullRequest.Groups[1].Value, CultureInfo.InvariantCulture))
                : null);
    }

    private static void WriteEntry(Utf8JsonWriter writer, SnapshotSession entry)
    {
        writer.WriteStartObject();
        writer.WriteString("id", entry.Id);
        writer.WriteString("title", entry.Title);
        writer.WriteString("status", StatusName(entry.Status));
        WriteStringOrNull(writer, "statusSince", entry.StatusSince is { } since ? UtcText(since) : null);
        writer.WriteBoolean("open", entry.Open);
        WriteStringOrNull(writer, "distro", entry.Distro);
        WriteStringOrNull(writer, "folder", entry.Folder);
        WriteStringOrNull(writer, "repository", entry.Repository);
        WriteStringOrNull(writer, "worktree", entry.Worktree);
        if (entry.PullRequest is { } pullRequest)
        {
            writer.WriteStartObject("pullRequest");
            writer.WriteString("owner", pullRequest.Owner);
            writer.WriteString("repo", pullRequest.Repo);
            writer.WriteNumber("number", pullRequest.Number);
            writer.WriteEndObject();
        }
        else
        {
            writer.WriteNull("pullRequest");
        }
        writer.WriteEndObject();
    }

    private static void WriteStringOrNull(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null) writer.WriteNull(name);
        else writer.WriteString(name, value);
    }

    private static string StatusName(SessionStatus status) => status switch
    {
        SessionStatus.NeedsInput => "needs_input",
        SessionStatus.Ready => "ready",
        SessionStatus.Working => "working",
        SessionStatus.Ended => "ended",
        _ => "none",
    };

    private static string UtcText(DateTimeOffset time) =>
        time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    // The name tm, links and the launcher give a pull request's worktree.
    [GeneratedRegex(@"\Apr-([1-9][0-9]{0,8})\z")]
    private static partial Regex PullRequestWorktree();
}
