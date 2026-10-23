using System.Text;

namespace TokenMojo.Core;

/// <summary>
/// The prompt file that carries a launch's prompt to `tm --prompt-file`, which deletes it after reading.
/// It is created by a command run as the launch user, with the prompt on standard input, so the user who reads
/// and deletes it also owns it (the distro share would write it as the distro's default user), and the prompt
/// is never on a command line.
/// </summary>
public static class LaunchPrompt
{
    private const string PromptDirectory = "$HOME/.claude/cctokmon-launch";
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>The file as `tm` is told to read it; the tilde is left for bash to expand.</summary>
    public static string LinuxPath(Guid id) => $"~/.claude/cctokmon-launch/{id:D}.txt";

    /// <summary>The prompt as UTF-8 without a byte order mark and with LF line endings.</summary>
    public static byte[] Encode(string prompt) =>
        Utf8WithoutBom.GetBytes(prompt.Replace("\r\n", "\n").Replace('\r', '\n'));

    /// <summary>
    /// For `sh -c` as the launch user: stores standard input in the prompt file, readable by that user only.
    /// Noclobber makes an existing file an error, as a prompt file is never reused.
    /// </summary>
    public static string DepositScript(Guid id) =>
        $"umask 077; mkdir -p \"{PromptDirectory}\" && set -C && cat > \"{PromptDirectory}/{id:D}.txt\"";

    /// <summary>For `sh -c` as the launch user: removes a prompt file that no `tm` is going to read.</summary>
    public static string DiscardScript(Guid id) => $"rm -f -- \"{PromptDirectory}/{id:D}.txt\"";
}
