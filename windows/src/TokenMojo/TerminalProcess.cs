using System.Diagnostics;
using TokenMojo.Core;

namespace TokenMojo;

internal static class TerminalProcess
{
    /// <summary>
    /// Starts wt.exe with the command's arguments passed as they are, never through a shell. Without <c>-d</c>,
    /// wt.exe opens the tab in its own working directory, so it gets the profile rather than the app's, which is
    /// wherever TokenMojo was started from and may since have been deleted.
    /// </summary>
    public static void Start(TerminalCommand command)
    {
        ProcessStartInfo startInfo = new(command.FileName)
        {
            UseShellExecute = false,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        foreach (string argument in command.Arguments)
            startInfo.ArgumentList.Add(argument);
        foreach (string variable in TerminalCommand.ClaudeSessionVariables)
            startInfo.Environment.Remove(variable);
        Process.Start(startInfo)?.Dispose();
    }
}
