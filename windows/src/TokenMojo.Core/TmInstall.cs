using System.Text;

namespace TokenMojo.Core;

public enum TmState { Ours, Missing, Foreign }

/// <summary>Whether the `tm` at ~/.local/bin/tm is the one install.sh puts there, which a foreign `tm` must never be run in place of.</summary>
public static class TmInstall
{
    // install.sh's own markers: the Windows launcher's second line, and the header of cctokmon-tm.sh itself.
    private const string LauncherMarker = "# tm launcher";
    private const string ScriptMarker = "# cctokmon-tm.sh";
    // Both markers are in the first lines; a foreign tm may be a large binary.
    private const int HeaderBytes = 4096;

    /// <summary>
    /// Reads the file as the distro share shows it. install.sh tells a symlink by its target, which the share does not
    /// reliably expose, but it serves a symlink's target content, which is cctokmon-tm.sh and carries the marker.
    /// A file that cannot be read counts as missing: the distro may have stopped.
    /// </summary>
    public static TmState Inspect(string path)
    {
        try
        {
            if (Directory.Exists(path)) return TmState.Foreign;
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            byte[] header = new byte[HeaderBytes];
            int read = stream.Read(header, 0, header.Length);
            return Classify(Encoding.UTF8.GetString(header, 0, read));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return TmState.Missing;
        }
    }

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
        _ => $"The tm for {user} in {distro} ({home}/.local/bin/tm) is not cctokmon's, so install.sh left it alone and TokenMojo did not run it. "
            + "Move it aside and run install.sh again.",
    };
}
