using System.Diagnostics;
using System.Text;
using TokenMojo.Core;

namespace TokenMojo;

/// <summary>Reads a user's tm from inside the distro, as that user, so a symlink to an absolute Linux path resolves.</summary>
internal sealed class WslTmInspector : ITmInspector
{
    public TmCheck Inspect(string distro, string user)
    {
        // --exec skips the user's shell, so the fixed script reaches sh as it is.
        WslResult? result = WslProcess.Run(["-d", distro, "-u", user, "--exec", "sh", "-c", TmInstall.InspectScript], new UTF8Encoding(false));
        if (result is null)
        {
            Trace.TraceWarning("TokenMojo: could not check tm for {0} in {1}", user, distro);
            return TmState.Unchecked;
        }
        TmCheck check = TmInstall.FromScript(result.ExitCode, result.Output);
        if (check.State is TmState.Unreadable or TmState.CouldNotRun)
            Trace.TraceWarning("TokenMojo: tm for {0} in {1}: {2} (exit code {3})", user, distro, check.State, result.ExitCode);
        return check;
    }
}
