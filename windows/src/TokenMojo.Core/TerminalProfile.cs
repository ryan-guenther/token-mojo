using System.Text.Json;

namespace TokenMojo.Core;

/// <summary>
/// The Windows Terminal profile that sessions on Windows itself open in, for their tab icon: wt.exe has no option for
/// an icon, only for a profile. It is a fragment, a file of its own that Windows Terminal reads beside its settings,
/// so the user's settings.json is never written. Hidden, so it stays out of the new-tab menu.
/// </summary>
public static class TerminalProfile
{
    public const string Name = "Claude Code (TokenMojo)";
    private const string Guid = "{6f1e5c8a-3b0d-4d7e-9a51-2c8b7e4f0d13}";
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>The fragment's contents; the profile's own command line is never used, since every launch gives one.</summary>
    /// <param name="iconPath">The icon file, a full Windows path.</param>
    public static string FragmentJson(string iconPath) => JsonSerializer.Serialize(new
    {
        profiles = new[]
        {
            new { guid = Guid, name = Name, icon = iconPath, hidden = true },
        },
    }, Options);

    /// <summary>
    /// Variables Claude Code sets for the processes it starts. wt.exe gives a new window the environment of whoever ran it,
    /// so a TokenMojo started from inside a Claude Code session would pass them on, and the new session would take itself
    /// for that session's child: among other things it stops saving its transcript, and cannot be resumed.
    /// </summary>
    public static readonly IReadOnlyList<string> ClaudeSessionVariables =
    [
        "CLAUDECODE", "CLAUDE_PID", "CLAUDE_CODE_ENTRYPOINT", "CLAUDE_CODE_CHILD_SESSION", "CLAUDE_CODE_SESSION_ID",
        "CLAUDE_CODE_SESSION_ATTENDED", "CLAUDE_CODE_MESSAGING_SOCKET", "CLAUDE_CODE_MESSAGING_TOKEN",
    ];
}
