namespace TokenMojo.Core;

/// <summary>Unchecked: the distro did not answer, which says nothing about the tm. Unreadable: it answered, but the tm could not be read.</summary>
public enum TmState { Ours, Missing, Foreign, Unchecked, Unreadable }

/// <summary>Asks a running distro about the `tm` of a user.</summary>
public interface ITmInspector
{
    TmState Inspect(string distro, string user);
}

/// <summary>Whether the `tm` at ~/.local/bin/tm is the one install.sh puts there, which a foreign `tm` must never be run in place of.</summary>
public static class TmInstall
{
    // install.sh's own markers: the Windows launcher's second line, and the header of cctokmon-tm.sh itself.
    private const string LauncherMarker = "# tm launcher";
    private const string ScriptMarker = "# cctokmon-tm.sh";
    private const int MissingExitCode = 3;
    private const int NotAFileExitCode = 4;

    /// <summary>
    /// For `sh -c` as the user, inside the distro, where a symlink's absolute target resolves (over the share it does not).
    /// Prints the first 4 KB of the file, following the link, which holds both markers; a foreign tm may be a large binary.
    /// Exits 3 when there is no tm, 4 when it is not a regular file.
    /// </summary>
    public const string InspectScript =
        "f=\"$HOME/.local/bin/tm\"; [ -e \"$f\" ] || exit 3; [ -f \"$f\" ] || exit 4; head -c 4096 -- \"$f\"";

    /// <summary>Reads what <see cref="InspectScript"/> did; an exit code it does not use means the tm exists but could not be read.</summary>
    public static TmState FromScript(int exitCode, string output) => exitCode switch
    {
        0 => Classify(output),
        MissingExitCode => TmState.Missing,
        NotAFileExitCode => TmState.Foreign,
        _ => TmState.Unreadable,
    };

    public static TmState Classify(string content)
    {
        bool ours = content.Split('\n').Select(line => line.TrimEnd('\r'))
            .Any(line => line == LauncherMarker || line.StartsWith(ScriptMarker, StringComparison.Ordinal));
        return ours ? TmState.Ours : TmState.Foreign;
    }

    /// <summary>What to tell the user when `tm` cannot be run; null when it can.</summary>
    public static string? ProblemText(TmState state, string user, string distro, string home) => state switch
    {
        TmState.Ours => null,
        TmState.Missing =>
            $"Could not find tm for {user} in {distro}. Run install.sh in that distro, or try again if the distro was just starting.",
        TmState.Unreadable =>
            $"The tm for {user} in {distro} ({home}/.local/bin/tm) could not be read, so TokenMojo did not run it. Check its permissions.",
        TmState.Unchecked => $"Could not check tm for {user} in {distro}: the distro did not answer. Try again in a moment.",
        _ => $"The tm for {user} in {distro} ({home}/.local/bin/tm) is not cctokmon's, so install.sh left it alone and TokenMojo did not run it. "
            + "Move it aside and run install.sh again.",
    };
}
