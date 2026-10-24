namespace TokenMojo.Core;

/// <summary>
/// Unchecked: the distro did not answer, which says nothing about the tm. Unreadable: the script found the tm and could not
/// read it. CouldNotRun: wsl.exe or sh failed before the script got that far (an unknown user, a missing shell).
/// </summary>
public enum TmState { Ours, Missing, Foreign, Unchecked, Unreadable, CouldNotRun }

/// <param name="ExitCode">What the command exited with; only CouldNotRun is explained by it.</param>
public readonly record struct TmCheck(TmState State, int ExitCode = 0)
{
    public static implicit operator TmCheck(TmState state) => new(state);
}

/// <summary>Asks a running distro about the `tm` of a user.</summary>
public interface ITmInspector
{
    TmCheck Inspect(string distro, string user);
}

/// <summary>Whether the `tm` at ~/.local/bin/tm is the one install.sh puts there, which a foreign `tm` must never be run in place of.</summary>
public static class TmInstall
{
    // install.sh's own markers: the Windows launcher's second line, and the header of cctokmon-tm.sh itself.
    private const string LauncherMarker = "# tm launcher";
    private const string ScriptMarker = "# cctokmon-tm.sh";
    private const int MissingExitCode = 3;
    private const int NotAFileExitCode = 4;
    private const int UnreadableExitCode = 5;

    /// <summary>
    /// For `sh -c` as the user, inside the distro, where a symlink's absolute target resolves (over the share it does not).
    /// Prints the first 4 KB of the file, following the link, which holds both markers; a foreign tm may be a large binary.
    /// Exits 3 when there is no tm, 4 when it is not a regular file, 5 when it cannot be read; any other exit is not the script's.
    /// </summary>
    public const string InspectScript =
        "f=\"$HOME/.local/bin/tm\"; [ -e \"$f\" ] || exit 3; [ -f \"$f\" ] || exit 4; head -c 4096 -- \"$f\" || exit 5";

    /// <summary>Reads what <see cref="InspectScript"/> did; an exit code it does not use means it never ran.</summary>
    public static TmCheck FromScript(int exitCode, string output) => exitCode switch
    {
        0 => Classify(output),
        MissingExitCode => TmState.Missing,
        NotAFileExitCode => TmState.Foreign,
        UnreadableExitCode => TmState.Unreadable,
        _ => new TmCheck(TmState.CouldNotRun, exitCode),
    };

    /// <summary>Where install.sh puts tm under Git Bash's home.</summary>
    public static string LocalPath(string gitBashHome) => Path.Combine(gitBashHome, ".local", "bin", "tm");

    /// <summary>
    /// The tm in Git Bash's home, which Git Bash runs. install.sh writes a launcher script there rather than a
    /// symlink, so it is read directly: the first 4 KB, which hold the markers.
    /// </summary>
    public static TmState InspectLocal(string gitBashHome)
    {
        string path = LocalPath(gitBashHome);
        try
        {
            if (Directory.Exists(path)) return TmState.Foreign;
            if (!File.Exists(path)) return TmState.Missing;
            using FileStream file = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            byte[] head = new byte[4096];
            int read = file.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
            return Classify(System.Text.Encoding.UTF8.GetString(head, 0, read));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return TmState.Unreadable;
        }
    }

    public static TmState Classify(string content)
    {
        bool ours = content.Split('\n').Select(line => line.TrimEnd('\r'))
            .Any(line => line == LauncherMarker || line.StartsWith(ScriptMarker, StringComparison.Ordinal));
        return ours ? TmState.Ours : TmState.Foreign;
    }

    /// <summary>What to tell the user when `tm` cannot be run; null when it can.</summary>
    public static string? ProblemText(TmCheck check, string user, string distro, string home) => check.State switch
    {
        TmState.Ours => null,
        TmState.Missing =>
            $"Could not find tm for {user} in {distro}. Run install.sh in that distro, or try again if the distro was just starting.",
        TmState.Unreadable =>
            $"The tm for {user} in {distro} ({home}/.local/bin/tm) could not be read, so TokenMojo did not run it. Check its permissions.",
        TmState.Unchecked => $"Could not check tm for {user} in {distro}: the distro did not answer. Try again in a moment.",
        TmState.CouldNotRun => $"Could not check tm for {user} in {distro}: the check could not run as that user (exit code {check.ExitCode}).",
        _ => $"The tm for {user} in {distro} ({home}/.local/bin/tm) is not cctokmon's, so install.sh left it alone and TokenMojo did not run it. "
            + "Move it aside and run install.sh again.",
    };
}
