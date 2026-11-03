using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TokenMojo.Core;

/// <summary>
/// A title for a session Claude Code has not named: its first prompt, read from the start of its transcript, on one
/// line, with GitHub and Linear links shortened to the item they point at.
/// </summary>
public static class TranscriptTitle
{
    // The first prompt is a few lines in; a transcript that has none this early has only commands so far.
    private const int MaxLines = 200;
    private const int MaxLength = 100;
    private const string ProjectsDirName = "projects";

    private static readonly Regex Whitespace = new(@"\s+");
    private static readonly Regex GitHubItem = new(@"https?://github\.com/([\w.-]+)/([\w.-]+)/(?:pull|issues)/(\d+)\S*");
    private static readonly Regex LinearIssue = new(@"https?://linear\.app/[\w.-]+/issue/([A-Za-z][A-Za-z0-9]*-\d+)\S*");
    private static readonly Regex CommandName = new(@"<command-name>\s*(.*?)\s*</command-name>", RegexOptions.Singleline);
    private static readonly Regex CommandArgs = new(@"<command-args>\s*(.*?)\s*</command-args>", RegexOptions.Singleline);

    // A session's first prompt never changes, so a title once found is kept for the life of the process.
    private static readonly ConcurrentDictionary<string, string> Found = new();

    /// <summary>
    /// The title for a session whose transcript Claude Code reported at <paramref name="transcriptPath"/>; null while it
    /// has no prompt yet or cannot be read. The transcript is looked for beside the source's sessions folder, under
    /// projects\&lt;folder&gt;\&lt;id&gt;.jsonl, since the reported path is in the session's own file system (Linux for WSL).
    /// </summary>
    public static string? For(ClaudeSource source, string transcriptPath)
    {
        string[] parts = transcriptPath.Split('/', '\\');
        if (parts.Length < 2 || Directory.GetParent(source.SessionsDir) is not { } claudeDir) return null;
        string path = Path.Combine(claudeDir.FullName, ProjectsDirName, parts[^2], parts[^1]);
        if (Found.TryGetValue(path, out string? title)) return title;
        title = Read(path);
        if (title is not null) Found[path] = title;
        return title;
    }

    /// <summary>The first prompt the user gave in these transcript lines, shortened; null when there is none.</summary>
    public static string? FirstPrompt(IEnumerable<string> lines)
    {
        foreach (string line in lines)
        {
            if (PromptText(line) is { } text && Shorten(text) is { Length: > 0 } title) return title;
        }
        return null;
    }

    /// <summary>The text on one line, links shortened, cut to <see cref="MaxLength"/> characters.</summary>
    public static string Shorten(string text)
    {
        string shortened = LinearIssue.Replace(GitHubItem.Replace(text, "$1/$2#$3"), match => match.Groups[1].Value.ToUpperInvariant());
        string line = Whitespace.Replace(shortened, " ").Trim();
        return line.Length <= MaxLength ? line : line[..(MaxLength - 1)].TrimEnd() + "…";
    }

    private static string? Read(string path)
    {
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using StreamReader reader = new(stream);
            return FirstPrompt(ReadLines(reader).Take(MaxLines));
        }
        // No transcript yet, or a share that went away mid-read: tried again on the next refresh.
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IEnumerable<string> ReadLines(StreamReader reader)
    {
        while (reader.ReadLine() is { } line) yield return line;
    }

    // A user line with typed text. Tool results, meta lines (skill bodies, caveats) and the output of local
    // commands are not prompts; a slash command is shown as typed, except /clear, which only empties the screen.
    private static string? PromptText(string line)
    {
        JsonElement root;
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("type", out JsonElement type) || type.ValueKind != JsonValueKind.String || type.GetString() != "user")
            return null;
        if (root.TryGetProperty("isMeta", out JsonElement meta) && meta.ValueKind == JsonValueKind.True) return null;
        if (!root.TryGetProperty("message", out JsonElement message) || message.ValueKind != JsonValueKind.Object
            || !message.TryGetProperty("content", out JsonElement content))
            return null;
        string? text = content.ValueKind switch
        {
            JsonValueKind.String => content.GetString(),
            JsonValueKind.Array => content.EnumerateArray()
                .Where(part => part.ValueKind == JsonValueKind.Object
                    && part.TryGetProperty("type", out JsonElement kind) && kind.ValueKind == JsonValueKind.String && kind.GetString() == "text"
                    && part.TryGetProperty("text", out JsonElement value) && value.ValueKind == JsonValueKind.String)
                .Select(part => part.GetProperty("text").GetString())
                .FirstOrDefault(),
            _ => null,
        };
        if (text is null || text.StartsWith("<local-command-", StringComparison.Ordinal)) return null;
        if (CommandName.Match(text) is not { Success: true } command) return text;
        string name = command.Groups[1].Value;
        if (name == "/clear") return null;
        return CommandArgs.Match(text) is { Success: true } args ? $"{name} {args.Groups[1].Value}" : name;
    }
}
