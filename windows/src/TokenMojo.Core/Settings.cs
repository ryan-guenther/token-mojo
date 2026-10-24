using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TokenMojo.Core;

public sealed class SettingsException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>User settings in %APPDATA%\TokenMojo\settings.json. A file that cannot be read is an error, never silently replaced by defaults.</summary>
public sealed record Settings
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };
    private static readonly Regex TeamKey = new(@"\A[A-Z][A-Z0-9]*\z");

    public int LongTurnSeconds { get; init; } = 30;

    /// <summary>The WSL distro the launcher uses, or "Windows" for Claude Code on Windows itself; null is the first available.</summary>
    public string? Distro { get; init; }

    public string DefaultFolder { get; init; } = LinuxFolder.Home;

    /// <summary>Folders whose subfolders the launcher offers when they are git repositories.</summary>
    public IReadOnlyList<string> RepoRoots { get; init; } = [LinuxFolder.Home];

    /// <summary>The launcher's folder for sessions on Windows itself: a full path, or relative to the user profile with ~.</summary>
    public string WindowsDefaultFolder { get; init; } = WindowsFolder.Home;

    /// <summary>The same as <see cref="RepoRoots"/> for sessions on Windows itself.</summary>
    public IReadOnlyList<string> WindowsRepoRoots { get; init; } = [WindowsFolder.Home];

    /// <summary>Linear team key to the folder its issues are worked in.</summary>
    public IReadOnlyDictionary<string, string> LinearTeams { get; init; } = new Dictionary<string, string>();

    public TimeSpan LongTurn => TimeSpan.FromSeconds(LongTurnSeconds);

    public static Settings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return CreateDefault(path);
            Settings settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Options)
                ?? throw new JsonException("The file is JSON null");
            settings.Validate();
            return settings;
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            throw new SettingsException($"Cannot read settings file {path}: {e.Message}", e);
        }
    }

    // JSON null can reach the non-nullable fields, so they are checked rather than trusted.
    private void Validate()
    {
        if (LongTurnSeconds < 0) throw new JsonException("longTurnSeconds must not be negative");
        if (Distro is "") throw new JsonException("distro must not be empty; leave it out to use the first running distro");
        RequireFolder("defaultFolder", DefaultFolder);
        if (RepoRoots is null) throw new JsonException("repoRoots must be a list");
        foreach (string root in RepoRoots) RequireFolder("repoRoots", root);
        RequireWindowsFolder("windowsDefaultFolder", WindowsDefaultFolder);
        if (WindowsRepoRoots is null) throw new JsonException("windowsRepoRoots must be a list");
        foreach (string root in WindowsRepoRoots) RequireWindowsFolder("windowsRepoRoots", root);
        if (LinearTeams is null) throw new JsonException("linearTeams must be an object");
        foreach ((string team, string folder) in LinearTeams)
        {
            if (!TeamKey.IsMatch(team)) throw new JsonException($"linearTeams key '{team}' must be a team key such as ENG");
            // A team is worked on in WSL or on Windows, so either kind of folder will do.
            if (!LinuxFolder.IsValid(folder) && !WindowsFolder.IsWindowsOnly(folder))
                throw new JsonException(
                    $"linearTeams.{team} must be a folder starting with ~/ or /, just ~, or a Windows path such as C:\\code or ~\\code, not '{folder}'");
        }
    }

    private static void RequireFolder(string field, string? folder)
    {
        if (!LinuxFolder.IsValid(folder))
            throw new JsonException($"{field} must be a folder starting with ~/ or /, or just ~, not '{folder}'");
    }

    private static void RequireWindowsFolder(string field, string? folder)
    {
        if (!WindowsFolder.IsValid(folder))
            throw new JsonException($"{field} must be a full Windows path such as C:\\code, or ~ or a path starting with ~\\, not '{folder}'");
    }

    // A folder that cannot be written is no reason to stop reading Claude's files: run on the defaults.
    private static Settings CreateDefault(string path)
    {
        Settings defaults = new();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(defaults, Options));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("TokenMojo: cannot create settings file {0}, using defaults: {1}", path, e.Message);
        }
        return defaults;
    }
}
