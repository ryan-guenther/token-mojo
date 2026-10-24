using System.Diagnostics;

namespace TokenMojo.Core;

/// <param name="Worktree">A name for `claude -w`; null starts in the folder itself.</param>
/// <param name="Prompt">Sent to Claude unchanged; blank sends nothing.</param>
/// <param name="Home">The user's home folder, which a folder written with a tilde is relative to.</param>
public sealed record LaunchRequest(string Distro, string User, string Folder, string? Worktree, string Prompt, string? Home = null);

/// <summary>Puts a prompt file where the launch user's `tm` will read it, as that user.</summary>
public interface IPromptHandoff
{
    /// <exception cref="IOException">The file could not be created.</exception>
    void Deposit(string distro, string user, Guid id, string prompt);

    /// <exception cref="IOException">The file could not be removed.</exception>
    void Discard(string distro, string user, Guid id);
}

public static class SessionLauncher
{
    /// <summary>Deposits the prompt file, then hands the terminal command to <paramref name="start"/>.</summary>
    /// <param name="start">Opens the terminal; whatever it throws is rethrown after the prompt file is removed.</param>
    public static void Launch(LaunchRequest request, IPromptHandoff handoff, Action<TerminalCommand> start) =>
        Launch(request, promptId => LaunchCommand.Build(request.Distro, request.User, request.Folder, request.Worktree, promptId), handoff, start);

    /// <summary>The same for Claude Code on Windows itself: the request's folder is a full Windows path, and Git Bash runs `tm`.</summary>
    public static void LaunchNative(LaunchRequest request, string bash, IPromptHandoff handoff, Action<TerminalCommand> start) =>
        Launch(request, promptId => LaunchCommand.BuildNative(bash, request.Folder, request.Worktree, promptId), handoff, start);

    private static void Launch(LaunchRequest request, Func<Guid?, LaunchCommand> build, IPromptHandoff handoff, Action<TerminalCommand> start)
    {
        Guid? promptId = string.IsNullOrWhiteSpace(request.Prompt) ? null : Guid.NewGuid();
        // Built first, so a request that would be refused leaves no file behind.
        LaunchCommand command = build(promptId);
        if (promptId is { } id) Deposit(handoff, request, id);
        bool started = false;
        try
        {
            start(command);
            started = true;
        }
        finally
        {
            // Once started, tm owns the file and deletes it.
            if (!started && promptId is { } unread) TryDiscard(handoff, request, unread);
        }
    }

    // A deposit that failed midway may have left part of the file.
    private static void Deposit(IPromptHandoff handoff, LaunchRequest request, Guid id)
    {
        try
        {
            handoff.Deposit(request.Distro, request.User, id, request.Prompt);
        }
        catch (IOException)
        {
            TryDiscard(handoff, request, id);
            throw;
        }
    }

    // The failure being reported is the one that stopped the launch, so one while cleaning up must not replace it.
    private static void TryDiscard(IPromptHandoff handoff, LaunchRequest request, Guid id)
    {
        try
        {
            handoff.Discard(request.Distro, request.User, id);
        }
        catch (IOException e)
        {
            Trace.TraceWarning("TokenMojo: cannot remove prompt file {0} in {1}: {2}", id, request.Distro, e.Message);
        }
    }
}
