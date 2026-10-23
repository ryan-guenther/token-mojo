namespace TokenMojo.Core;

/// <summary>A wt.exe invocation that opens a new Windows Terminal window running a command in a WSL distro.</summary>
public record TerminalCommand(string FileName, IReadOnlyList<string> Arguments)
{
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
