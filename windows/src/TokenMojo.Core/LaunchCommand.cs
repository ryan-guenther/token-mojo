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
        // The same rule as tm, which would refuse the name anyway; failing here shows it in the launcher.
        if (worktree is not null && !ValidWorktree(worktree))
            throw new ArgumentException($"Invalid worktree name '{worktree}': use letters, digits, '.', '_' and '-'", nameof(worktree));
        List<string> words = ["~/.local/bin/tm"];
        if (worktree is not null) words.AddRange(["--wt", worktree]);
        if (promptFile is { } id) words.AddRange(["--prompt-file", LaunchPrompt.LinuxPath(id)]);
        return new LaunchCommand("wt.exe", NewWindowArguments(distro, user, folder, string.Join(' ', words)));
    }

    private static bool ValidWorktree(string name) =>
        WorktreeName().IsMatch(name) && !name.StartsWith('-') && name is not ("." or "..");

    [GeneratedRegex(@"\A[A-Za-z0-9._-]+\z")]
    private static partial Regex WorktreeName();
}
