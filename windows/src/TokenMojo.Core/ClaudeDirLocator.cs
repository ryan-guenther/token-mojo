using System.Collections.Concurrent;
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

    // Locate runs on the discovery timer and when the launcher opens, so this is shared between threads.
    private readonly ConcurrentDictionary<string, LinuxAccounts> _lastAnswers = new(StringComparer.OrdinalIgnoreCase);

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
        List<(string Folder, string Home, string OnShare)> homes = new();
        string homeDir = Path.Combine(distroRoot, "home");
        try
        {
            if (Directory.Exists(homeDir))
            {
                homes.AddRange(Directory.EnumerateDirectories(homeDir).Order()
                    .Select(onShare => (Path.GetFileName(onShare), $"/home/{Path.GetFileName(onShare)}", onShare)));
            }
        }
        // The distro may stop between listing and reading, or deny access to /home.
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
        // Only this one is root's home: a folder named root under /home is an ordinary home.
        homes.Add(("root", "/root", Path.Combine(distroRoot, "root")));
        List<(string Home, string Folder, string Claude)> withClaude = homes
            .Select(entry => (entry.Home, entry.Folder, Claude: Path.Combine(entry.OnShare, ".claude")))
            .Where(entry => Directory.Exists(entry.Claude))
            .ToList();
        if (withClaude.Count == 0) return [];
        LinuxAccounts? distroAccounts = AccountsOf(distro);
        return withClaude
            .Select(entry => (entry.Home, entry.Claude, User: UserOf(distro, distroAccounts, entry.Folder, entry.Home)))
            .Select(entry => new ClaudeSource($"{distro}:{entry.User}",
                Path.Combine(entry.Claude, StateFileName), Path.Combine(entry.Claude, SessionsDirName),
                distro, entry.User, entry.Home, IsDefaultUser: entry.User == distroAccounts?.DefaultUser))
            .ToList();
    }

    // One question per distro, and only when there is something to name. A failed question does not undo what an earlier
    // one found: the last answer is reused, and folder names are the fallback only when there never was one.
    private LinuxAccounts? AccountsOf(string distro)
    {
        LinuxAccounts? answer = accounts.Query(distro);
        if (answer is not null)
        {
            _lastAnswers[distro] = answer;
            return answer;
        }
        if (_lastAnswers.TryGetValue(distro, out LinuxAccounts? earlier))
        {
            Trace.TraceWarning("TokenMojo: cannot read the users of {0}; using the answer from earlier", distro);
            return earlier;
        }
        Trace.TraceWarning("TokenMojo: cannot read the users of {0}; using home folder names as user names", distro);
        return null;
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
