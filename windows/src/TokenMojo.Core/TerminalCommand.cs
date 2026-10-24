namespace TokenMojo.Core;

/// <summary>A wt.exe invocation that opens a new Windows Terminal window running a command in a WSL distro or in Git Bash.</summary>
public record TerminalCommand(string FileName, IReadOnlyList<string> Arguments)
{
    /// <param name="bash">Git's bin\bash.exe, which puts Git's tools on PATH.</param>
    /// <param name="folder">The full Windows path the window starts in.</param>
    /// <param name="shellCommand">Run by a plain (not login) shell, since a login shell's profile changes to the home folder. One wt.exe argument, so no semicolons.</param>
    protected static List<string> NativeWindowArguments(string bash, string folder, string shellCommand)
    {
        // Git Bash expands a tilde in an unquoted argument before bash sees it, and the expanded home folder may hold a
        // space that then splits the command. An argument without spaces reaches it unquoted, so `exec` makes sure it has one.
        List<string> arguments = ["-w", "new", "-p", TerminalProfile.Name, "-d", folder, bash, "-c", $"exec {shellCommand}"];
        return arguments.Select(EscapeForWindowsTerminal).ToList();
    }

    /// <param name="user">The Linux user to run as; null runs as the distro's default user.</param>
    /// <param name="folder">Where the shell starts, in a form wsl.exe takes for --cd.</param>
    /// <param name="shellCommand">Run by a login shell. It is one wt.exe argument, so it must hold no semicolons.</param>
    protected static List<string> NewWindowArguments(string distro, string? user, string folder, string shellCommand)
    {
        List<string> arguments = ["-w", "new", "wsl.exe", "-d", distro];
        if (user is not null) arguments.AddRange(["-u", user]);
        arguments.AddRange(["--cd", folder, "--", "bash", "-lc", shellCommand]);
        return arguments.Select(EscapeForWindowsTerminal).ToList();
    }

    // wt.exe reads an unescaped semicolon as the start of its next command.
    private static string EscapeForWindowsTerminal(string argument) => argument.Replace(";", @"\;");
}
