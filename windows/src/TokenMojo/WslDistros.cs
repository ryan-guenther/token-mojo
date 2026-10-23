using System.Text;
using TokenMojo.Core;

namespace TokenMojo;

internal sealed class WslDistros : IWslDistros
{
    public IReadOnlyList<string> Running()
    {
        // wsl.exe writes UTF-16LE.
        WslResult? result = WslProcess.Run(["--list", "--running", "--quiet"], Encoding.Unicode);
        // Non-zero when no distro is running.
        return result is { ExitCode: 0 }
            ? result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [];
    }
}
