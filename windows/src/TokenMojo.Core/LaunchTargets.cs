namespace TokenMojo.Core;

/// <param name="Source">The ~/.claude to launch from, whose user and home the session runs with; null when there is none to use.</param>
/// <param name="Problem">Why nothing can be launched; null when <paramref name="Source"/> is set.</param>
/// <param name="Note">Why the launch user is not the distro's default user, when that is not obvious.</param>
public sealed record LaunchUser(ClaudeSource? Source, string? Problem, string? Note);

/// <summary>Where the launcher starts a session: which distro, and which Linux user's home it uses.</summary>
public static class LaunchTargets
{
    /// <summary>The target that starts sessions on Windows itself rather than in a WSL distro.</summary>
    public const string Windows = "Windows";

    private const string NoClaudeFolderText =
        "Could not find a Claude Code folder in {0}. Run install.sh in that distro, then reopen this window.";
    private const string NoGitBashText =
        "Could not find Git Bash, which runs tm for sessions on Windows. Install Git for Windows, or set CLAUDE_CODE_GIT_BASH_PATH to its bash.exe.";

    /// <summary>
    /// Where sessions can be started: each running distro with a Claude Code folder, in the order given, then Windows when
    /// Claude Code is set up there. A distro without one, such as Docker Desktop's, has nothing to start.
    /// </summary>
    public static IReadOnlyList<string> Targets(IReadOnlyList<string> running, IEnumerable<ClaudeSource> sources, bool windowsHasClaude)
    {
        HashSet<string> withClaude = sources.Select(source => source.Distro).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        List<string> targets = running.Where(withClaude.Contains).ToList();
        if (windowsHasClaude) targets.Add(Windows);
        return targets;
    }

    public static bool IsWindows(string target) => target == Windows;

    /// <summary>The configured target when it is available, else the first one; null when there is none.</summary>
    public static string? ChooseDistro(string? configured, IReadOnlyList<string> targets) =>
        targets.FirstOrDefault(target => string.Equals(target, configured, StringComparison.OrdinalIgnoreCase))
        ?? targets.FirstOrDefault();

    /// <summary>Why a session cannot be started or resumed on Windows itself; null when it can.</summary>
    /// <param name="bash">Git's bash.exe, or null when it was not found.</param>
    /// <param name="tm">The tm in the user profile.</param>
    public static string? NativeProblem(string? bash, TmState tm, string home)
    {
        if (bash is null) return NoGitBashText;
        string path = $@"{home.TrimEnd('\\')}\.local\bin\tm";
        return tm switch
        {
            TmState.Ours => null,
            TmState.Missing => $"Could not find tm ({path}). Run install.sh in Git Bash, then try again.",
            TmState.Unreadable => $"The tm at {path} could not be read, so TokenMojo did not run it. Check its permissions.",
            _ => $"The tm at {path} is not cctokmon's, so install.sh left it alone and TokenMojo did not run it. "
                + "Move it aside and run install.sh again.",
        };
    }

    /// <summary>
    /// The distro's default user when it has a ~/.claude with a tm of cctokmon's, else the first user that has one.
    /// The default user comes first because it is who the distro share writes as and who a plain `wsl.exe` runs as.
    /// </summary>
    /// <param name="tmState">Inspects the tm of a user; asked at most once for each user, in order, until one is ours.</param>
    public static LaunchUser ChooseUser(IEnumerable<ClaudeSource> sources, string distro, Func<ClaudeSource, TmCheck> tmState)
    {
        List<ClaudeSource> candidates = sources
            .Where(source => string.Equals(source.Distro, distro, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(source => source.IsDefaultUser)
            .ToList();
        if (candidates.Count == 0) return new LaunchUser(null, string.Format(NoClaudeFolderText, distro), null);

        TmCheck first = TmState.Missing;
        ClaudeSource? notChecked = null;
        for (int index = 0; index < candidates.Count; index++)
        {
            ClaudeSource candidate = candidates[index];
            TmCheck state = tmState(candidate);
            if (index == 0) first = state;
            // Not knowing about the default user is not a reason to start as someone else; for anyone else it is a reason to look on.
            if (state.State == TmState.Unchecked && candidate.IsDefaultUser) return Problem(state, candidate, distro);
            if (state.State == TmState.Unchecked) notChecked ??= candidate;
            if (state.State != TmState.Ours) continue;
            return new LaunchUser(candidate, null, index > 0 && candidates[0].IsDefaultUser ? NoteFor(candidate, candidates[0]) : null);
        }
        return notChecked is null ? Problem(first, candidates[0], distro) : Problem(TmState.Unchecked, notChecked, distro);
    }

    private static LaunchUser Problem(TmCheck state, ClaudeSource source, string distro) =>
        new(null, TmInstall.ProblemText(state, source.User!, distro, source.Home!), null);

    private static string NoteFor(ClaudeSource chosen, ClaudeSource defaultUser) =>
        $"Starting as {chosen.User}: the distro's default user, {defaultUser.User}, has no tm of cctokmon's to run.";
}
