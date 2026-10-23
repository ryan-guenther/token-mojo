using System.Diagnostics;
using System.Text;
using TokenMojo.Core;

namespace TokenMojo;

/// <summary>Reads a user's tm from inside the distro, as that user, so a symlink to an absolute Linux path resolves.</summary>
internal sealed class WslTmInspector : ITmInspector
{
    public TmState Inspect(string distro, string user)
    {
        // --exec skips the user's shell, so the fixed script reaches sh as it is.
        WslResult? result = WslProcess.Run(["-d", distro, "-u", user, "--exec", "sh", "-c", TmInstall.InspectScript], new UTF8Encoding(false));
        if (result is null)
        {
            Trace.TraceWarning("TokenMojo: could not check tm for {0} in {1}", user, distro);
            return TmState.Unchecked;
        }
        TmState state = TmInstall.FromScript(result.ExitCode, result.Output);
        if (state == TmState.Unreadable)
            Trace.TraceWarning("TokenMojo: tm for {0} in {1} could not be read (exit code {2})", user, distro, result.ExitCode);
        return state;
    }
}
