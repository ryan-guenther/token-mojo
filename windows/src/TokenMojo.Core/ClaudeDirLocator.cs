using System.Diagnostics;

namespace TokenMojo.Core;

/// <param name="Distro">The WSL distro this source lives in; null for the Windows profile.</param>
/// <param name="User">The Linux account that owns the ~/.claude, which may be named differently from its home folder; null for the Windows profile.</param>
/// <param name="Home">The account's home folder as Linux sees it; null for the Windows profile.</param>
/// <param name="IsDefaultUser">Whether the user is the distro's default one, whose identity the distro share writes as.</param>
public sealed record ClaudeSource(
    string Name, string StateFile, string SessionsDir, string? Distro = null, string? User = null, string? Home = null, bool IsDefaultUser = false);

public interface IWslDistros
{
    IReadOnlyList<string> Running();
}

/// <summary>Finds every ~/.claude the bridge may write to: the Windows profile and each running WSL distro.</summary>
public sealed class ClaudeDirLocator(
    string windowsHome, IWslDistros wsl, ILinuxAccounts accounts, Func<string, string?> getEnv, string wslShareRoot = @"\\wsl.localhost")
{
    private const string StateFileName = "cctokmon-state.json";
    private const string SessionsDirName = "cctokmon-sessions";

    public IReadOnlyList<ClaudeSource> Locate()
    {
        string windowsClaude = Path.Combine(windowsHome, ".claude");
        List<ClaudeSource> sources = new()
        {
            new("Windows",
                getEnv("CCTOKMON_STATE_FILE") ?? Path.Combine(windowsClaude, StateFileName),
                getEnv("CCTOKMON_SESSIONS_DIR") ?? Path.Combine(windowsClaude, SessionsDirName)),
        };
        // Only running distros: reading a stopped distro's share would boot it.
        foreach (string distro in wsl.Running())
            sources.AddRange(DistroSources(distro));
        return sources;
    }

    private List<ClaudeSource> DistroSources(string distro)
    {
        string distroRoot = Path.Combine(wslShareRoot, distro);
        List<(string Folder, string OnShare)> homes = new();
        string homeDir = Path.Combine(distroRoot, "home");
        try
        {
            if (Directory.Exists(homeDir))
                homes.AddRange(Directory.EnumerateDirectories(homeDir).Order().Select(onShare => (Path.GetFileName(onShare), onShare)));
        }
        // The distro may stop between listing and reading, or deny access to /home.
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
        homes.Add(("root", Path.Combine(distroRoot, "root")));
        List<(string Home, string Folder, string Claude)> withClaude = homes
            .Select(entry => (Home: entry.Folder == "root" ? "/root" : $"/home/{entry.Folder}", entry.Folder, Claude: Path.Combine(entry.OnShare, ".claude")))
            .Where(entry => Directory.Exists(entry.Claude))
            .ToList();
        if (withClaude.Count == 0) return [];
        // One question per distro, and only when there is something to name.
        LinuxAccounts? distroAccounts = accounts.Query(distro);
        if (distroAccounts is null)
            Trace.TraceWarning("TokenMojo: cannot read the users of {0}; using home folder names as user names", distro);
        return withClaude
            .Select(entry => (entry.Home, entry.Claude, User: UserOf(distro, distroAccounts, entry.Folder, entry.Home)))
            .Select(entry => new ClaudeSource($"{distro}:{entry.User}",
                Path.Combine(entry.Claude, StateFileName), Path.Combine(entry.Claude, SessionsDirName),
                distro, entry.User, entry.Home, IsDefaultUser: entry.User == distroAccounts?.DefaultUser))
            .ToList();
    }

    // The account that owns the home folder; the folder's name when the distro could not say.
    private static string UserOf(string distro, LinuxAccounts? distroAccounts, string folder, string home)
    {
        if (distroAccounts is null) return folder;
        if (distroAccounts.UserByHome.TryGetValue(home, out string? user)) return user;
        Trace.TraceWarning("TokenMojo: no account in {0} has {1} as its home; using {2} as the user name", distro, home, folder);
        return folder;
    }
}
