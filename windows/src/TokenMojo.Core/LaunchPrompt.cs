using System.Text;

namespace TokenMojo.Core;

/// <summary>The prompt file that carries a launch's prompt to `tm --prompt-file`, which deletes it after reading.</summary>
public static class LaunchPrompt
{
    private const string DirectoryName = ".claude/cctokmon-launch";
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>The file as the shell sees it; the tilde is left for bash to expand.</summary>
    public static string LinuxPath(Guid id) => $"~/{DirectoryName}/{id:D}.txt";

    public static string DirectoryOnShare(string distro, string user, string shareRoot = WslPaths.DefaultShareRoot) =>
        WslPaths.SharePath(distro, $"{WslPaths.HomeFolder(user)}/{DirectoryName}", shareRoot);

    /// <summary>Writes the prompt as UTF-8 without a byte order mark and with LF line endings; returns the file's path.</summary>
    public static string Write(string directory, Guid id, string prompt)
    {
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, $"{id:D}.txt");
        string text = prompt.Replace("\r\n", "\n").Replace('\r', '\n');
        using FileStream stream = new(file, FileMode.CreateNew, FileAccess.Write);
        stream.Write(Utf8WithoutBom.GetBytes(text));
        return file;
    }

    public static void Delete(string file) => File.Delete(file);
}
