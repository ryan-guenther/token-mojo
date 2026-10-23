namespace TokenMojo.Core;

/// <param name="Worktree">A name for `claude -w`; null starts in the folder itself.</param>
/// <param name="Prompt">Sent to Claude unchanged; blank sends nothing.</param>
public sealed record LaunchRequest(string Distro, string User, string Folder, string? Worktree, string Prompt);

public static class SessionLauncher
{
    /// <summary>Writes the prompt file, then hands the terminal command to <paramref name="start"/>.</summary>
    /// <param name="promptDirectory">The distro share folder the prompt file goes in, which `tm` reads it from.</param>
    /// <param name="start">Opens the terminal; whatever it throws is rethrown after the prompt file is removed.</param>
    public static void Launch(LaunchRequest request, string promptDirectory, Action<TerminalCommand> start)
    {
        Guid? promptId = string.IsNullOrWhiteSpace(request.Prompt) ? null : Guid.NewGuid();
        // Built first, so a request that would be refused leaves no file behind.
        LaunchCommand command = LaunchCommand.Build(request.Distro, request.User, request.Folder, request.Worktree, promptId);
        string? promptFile = promptId is { } id ? LaunchPrompt.Write(promptDirectory, id, request.Prompt) : null;
        bool started = false;
        try
        {
            start(command);
            started = true;
        }
        finally
        {
            // Once started, tm owns the file and deletes it.
            if (!started && promptFile is not null) LaunchPrompt.Delete(promptFile);
        }
    }
}
