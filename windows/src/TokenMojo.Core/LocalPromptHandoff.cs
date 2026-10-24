namespace TokenMojo.Core;

/// <summary>
/// The prompt file for a session on Windows itself, written straight into the user's own profile, where Git Bash's
/// `tm` reads it as ~/.claude/cctokmon-launch. The distro and user are not used: there is only one user.
/// </summary>
public sealed class LocalPromptHandoff(string windowsHome) : IPromptHandoff
{
    public void Deposit(string distro, string user, Guid id, string prompt)
    {
        string path = PathOf(id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // CreateNew: a prompt file is never reused, so an existing one is an error.
        using FileStream file = new(path, FileMode.CreateNew, FileAccess.Write);
        file.Write(LaunchPrompt.Encode(prompt));
    }

    public void Discard(string distro, string user, Guid id) => File.Delete(PathOf(id));

    public string PathOf(Guid id) => Path.Combine(windowsHome, ".claude", "cctokmon-launch", $"{id:D}.txt");
}
