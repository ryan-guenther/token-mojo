using System.IO;
using System.Text;
using TokenMojo.Core;

namespace TokenMojo;

/// <summary>Creates the prompt file as the launch user: a command run with -u, fed the prompt on standard input.</summary>
internal sealed class WslPromptHandoff : IPromptHandoff
{
    public void Deposit(string distro, string user, Guid id, string prompt) =>
        Run(distro, user, LaunchPrompt.DepositScript(id), LaunchPrompt.Encode(prompt), "store the prompt");

    public void Discard(string distro, string user, Guid id) =>
        Run(distro, user, LaunchPrompt.DiscardScript(id), [], "remove the prompt file");

    // --exec skips the user's shell, so the script reaches sh as it is. The prompt is never an argument.
    private static void Run(string distro, string user, string script, byte[] input, string purpose)
    {
        WslResult? result = WslProcess.Run(["-d", distro, "-u", user, "--exec", "sh", "-c", script], new UTF8Encoding(false), input);
        if (result is null) throw new IOException($"wsl.exe did not answer while trying to {purpose} in {distro}.");
        if (result.ExitCode != 0)
            throw new IOException($"Could not {purpose} as {user} in {distro} (exit code {result.ExitCode}).");
    }
}
