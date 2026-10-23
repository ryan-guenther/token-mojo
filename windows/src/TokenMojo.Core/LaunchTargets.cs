namespace TokenMojo.Core;

/// <param name="Source">The ~/.claude to launch from, whose user and home the session runs with; null when there is none to use.</param>
/// <param name="Problem">Why nothing can be launched; null when <paramref name="Source"/> is set.</param>
/// <param name="Note">Why the launch user is not the distro's default user, when that is not obvious.</param>
public sealed record LaunchUser(ClaudeSource? Source, string? Problem, string? Note);

/// <summary>Where the launcher starts a session: which distro, and which Linux user's home it uses.</summary>
public static class LaunchTargets
{
    private const string NoClaudeFolderText =
        "Could not find a Claude Code folder in {0}. Run install.sh in that distro, then reopen this window.";

    /// <summary>The configured distro when it is running, else the first running one; null when none is.</summary>
    public static string? ChooseDistro(string? configured, IReadOnlyList<string> running) =>
        running.FirstOrDefault(distro => string.Equals(distro, configured, StringComparison.OrdinalIgnoreCase))
        ?? running.FirstOrDefault();

    /// <summary>
    /// The distro's default user when it has a ~/.claude with a tm of cctokmon's, else the first user that has one.
    /// The default user comes first because it is who the distro share writes as and who a plain `wsl.exe` runs as.
    /// </summary>
    /// <param name="tmState">Inspects the tm of a user; asked at most once for each user, in order, until one is ours.</param>
    public static LaunchUser ChooseUser(IEnumerable<ClaudeSource> sources, string distro, Func<ClaudeSource, TmState> tmState)
    {
        List<ClaudeSource> candidates = sources
            .Where(source => string.Equals(source.Distro, distro, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(source => source.IsDefaultUser)
            .ToList();
        if (candidates.Count == 0) return new LaunchUser(null, string.Format(NoClaudeFolderText, distro), null);

        TmState first = TmState.Missing;
        ClaudeSource? notChecked = null;
        for (int index = 0; index < candidates.Count; index++)
        {
            ClaudeSource candidate = candidates[index];
            TmState state = tmState(candidate);
            if (index == 0) first = state;
            // Not knowing about the default user is not a reason to start as someone else; for anyone else it is a reason to look on.
            if (state == TmState.Unchecked && candidate.IsDefaultUser) return Problem(state, candidate, distro);
            if (state == TmState.Unchecked) notChecked ??= candidate;
            if (state != TmState.Ours) continue;
            return new LaunchUser(candidate, null, index > 0 && candidates[0].IsDefaultUser ? NoteFor(candidate, candidates[0]) : null);
        }
        return notChecked is null ? Problem(first, candidates[0], distro) : Problem(TmState.Unchecked, notChecked, distro);
    }

    private static LaunchUser Problem(TmState state, ClaudeSource source, string distro) =>
        new(null, TmInstall.ProblemText(state, source.User!, distro, source.Home!), null);

    private static string NoteFor(ClaudeSource chosen, ClaudeSource defaultUser) =>
        $"Starting as {chosen.User}: the distro's default user, {defaultUser.User}, has no tm of cctokmon's to run.";
}
