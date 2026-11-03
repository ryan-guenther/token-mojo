using System.Globalization;

namespace TokenMojo.Core;

/// <summary>The settings window's fields as typed: numbers as text, lists one entry per line, teams as TEAM = folder lines.</summary>
public sealed record SettingsForm(
    bool OpenPopOutAtStart,
    string LongTurnSeconds,
    string SessionHours,
    string SessionRows,
    string? Distro,
    string DefaultFolder,
    string RepoRoots,
    string WindowsDefaultFolder,
    string WindowsRepoRoots,
    string LinearTeams)
{
    public static SettingsForm From(Settings settings) => new(
        settings.OpenPopOutAtStart,
        settings.LongTurnSeconds.ToString(CultureInfo.InvariantCulture),
        settings.SessionHours.ToString(CultureInfo.InvariantCulture),
        settings.SessionRows.ToString(CultureInfo.InvariantCulture),
        settings.Distro,
        settings.DefaultFolder,
        string.Join(Environment.NewLine, settings.RepoRoots),
        settings.WindowsDefaultFolder,
        string.Join(Environment.NewLine, settings.WindowsRepoRoots),
        string.Join(Environment.NewLine, settings.LinearTeams.Select(team => $"{team.Key} = {team.Value}")));

    /// <summary>The settings these fields describe, on top of <paramref name="original"/>, or why they cannot be saved.</summary>
    public SettingsResult Apply(Settings original)
    {
        if (!TryNumber(LongTurnSeconds, out int longTurn)) return Refused("The finished alert's turn length must be a whole number of seconds.");
        if (!TryNumber(SessionHours, out int hours)) return Refused("The session list's hours must be a whole number.");
        if (!TryNumber(SessionRows, out int rows)) return Refused("The session list's rows must be a whole number.");
        Dictionary<string, string> teams = [];
        foreach (string line in Lines(LinearTeams))
        {
            int separator = line.IndexOf('=');
            if (separator < 0) return Refused($"Each Linear team line must be TEAM = folder, not '{line}'.");
            string team = line[..separator].Trim().ToUpperInvariant();
            if (!teams.TryAdd(team, line[(separator + 1)..].Trim())) return Refused($"The Linear team {team} is listed twice.");
        }
        Settings settings = original with
        {
            OpenPopOutAtStart = OpenPopOutAtStart,
            LongTurnSeconds = longTurn,
            SessionHours = hours,
            SessionRows = rows,
            Distro = Distro,
            DefaultFolder = DefaultFolder.Trim(),
            RepoRoots = Lines(RepoRoots),
            WindowsDefaultFolder = WindowsDefaultFolder.Trim(),
            WindowsRepoRoots = Lines(WindowsRepoRoots),
            LinearTeams = teams,
        };
        return settings.Problem() is { } problem ? Refused(problem) : new SettingsResult(settings, null);
    }

    private static SettingsResult Refused(string problem) => new(null, problem);

    private static bool TryNumber(string text, out int value) =>
        int.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

    private static List<string> Lines(string text) =>
        text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToList();
}

/// <param name="Settings">The settings to save; null when <paramref name="Problem"/> is set.</param>
/// <param name="Problem">Why the fields cannot be saved.</param>
public sealed record SettingsResult(Settings? Settings, string? Problem);
