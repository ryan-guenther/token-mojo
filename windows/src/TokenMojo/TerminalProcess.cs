using System.Diagnostics;
using TokenMojo.Core;

namespace TokenMojo;

internal static class TerminalProcess
{
    /// <summary>Starts wt.exe with the command's arguments passed as they are, never through a shell.</summary>
    public static void Start(TerminalCommand command)
    {
        ProcessStartInfo startInfo = new(command.FileName) { UseShellExecute = false };
        foreach (string argument in command.Arguments)
            startInfo.ArgumentList.Add(argument);
        foreach (string variable in TerminalProfile.ClaudeSessionVariables)
            startInfo.Environment.Remove(variable);
        Process.Start(startInfo)?.Dispose();
    }
}
