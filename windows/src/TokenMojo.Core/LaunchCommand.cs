using System.Text.RegularExpressions;

namespace TokenMojo.Core;

/// <summary>The wt.exe invocation that starts a new session with `tm` in a new Windows Terminal window.</summary>
public sealed partial record LaunchCommand(string FileName, IReadOnlyList<string> Arguments) : TerminalCommand(FileName, Arguments)
{
    /// <param name="folder">Where the session starts; <c>~</c>, <c>~/sub</c> or an absolute path, which wsl.exe takes as it is.</param>
    /// <param name="worktree">A name for `claude -w`; null starts in the folder itself.</param>
    /// <param name="promptFile">The prompt file written for this launch; null when there is no prompt.</param>
    public static LaunchCommand Build(string distro, string? user, string folder, string? worktree, Guid? promptFile)
    {
        if (!LinuxFolder.IsValid(folder))
            throw new ArgumentException($"Not a folder wsl.exe can start in: '{folder}'", nameof(folder));
        return new LaunchCommand("wt.exe", NewWindowArguments(distro, user, folder, TmCommand(worktree, promptFile)));
    }

    /// <summary>The same `tm` run by Git Bash, for Claude Code on Windows itself.</summary>
    /// <param name="bash">Git's bin\bash.exe.</param>
    /// <param name="folder">The full Windows path the session starts in.</param>
    public static LaunchCommand BuildNative(string bash, string folder, string? worktree, Guid? promptFile)
    {
        if (!WindowsFolder.IsValid(folder) || folder.StartsWith(WindowsFolder.Home, StringComparison.Ordinal))
            throw new ArgumentException($"Not a full Windows path: '{folder}'", nameof(folder));
        return new LaunchCommand("wt.exe", NativeWindowArguments(bash, folder, TmCommand(worktree, promptFile)));
    }

    // tm by its install path, with the tilde left for bash: a login shell in WSL does not see a PATH that only .bashrc sets.
    // There are no quotes, so wt.exe, wsl.exe and Git Bash have nothing to unescape.
    private static string TmCommand(string? worktree, Guid? promptFile)
    {
        // The same rule as tm, which would refuse the name anyway; failing here shows it in the launcher.
        if (worktree is not null && !ValidWorktree(worktree))
            throw new ArgumentException($"Invalid worktree name '{worktree}': use letters, digits, '.', '_' and '-'", nameof(worktree));
        List<string> words = ["~/.local/bin/tm"];
        if (worktree is not null) words.AddRange(["--wt", worktree]);
        if (promptFile is { } id) words.AddRange(["--prompt-file", LaunchPrompt.LinuxPath(id)]);
        return string.Join(' ', words);
    }

    private static bool ValidWorktree(string name) =>
        WorktreeName().IsMatch(name) && !name.StartsWith('-') && name is not ("." or "..");

    [GeneratedRegex(@"\A[A-Za-z0-9._-]+\z")]
    private static partial Regex WorktreeName();
}
