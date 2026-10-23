using System.Diagnostics;
using System.Text;
using TokenMojo.Core;

namespace TokenMojo;

/// <summary>Asks a running distro for its users with one command, run as the distro's default user.</summary>
internal sealed class WslAccounts : ILinuxAccounts
{
    public LinuxAccounts? Query(string distro)
    {
        // --exec skips the user's shell, so the command text reaches sh as it is. No -u: the default user is what is asked for.
        WslResult? result = WslProcess.Run(["-d", distro, "--exec", "sh", "-c", LinuxAccounts.QueryCommand], new UTF8Encoding(false));
        if (result is null) return null;
        if (result.ExitCode != 0)
        {
            Trace.TraceWarning("TokenMojo: reading the users of {0} failed with exit code {1}", distro, result.ExitCode);
            return null;
        }
        return LinuxAccounts.Parse(result.Output);
    }
}
