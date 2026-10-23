namespace TokenMojo.Core;

/// <summary>Where the launcher starts a session: which distro, and which Linux user's home it uses.</summary>
public static class LaunchTargets
{
    /// <summary>The configured distro when it is running, else the first running one; null when none is.</summary>
    public static string? ChooseDistro(string? configured, IReadOnlyList<string> running) =>
        running.FirstOrDefault(distro => string.Equals(distro, configured, StringComparison.OrdinalIgnoreCase))
        ?? running.FirstOrDefault();

    /// <summary>The user of the first ~/.claude found in the distro, which is where tm and the hooks were installed.</summary>
    public static string? UserFor(IEnumerable<ClaudeSource> sources, string distro) =>
        sources.FirstOrDefault(source => string.Equals(source.Distro, distro, StringComparison.OrdinalIgnoreCase))?.User;
}
