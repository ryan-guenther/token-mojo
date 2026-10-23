using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace TokenMojo;

internal sealed record WslResult(int ExitCode, string Output);

/// <summary>Runs wsl.exe and waits for it, so every call blocks: never on the UI thread.</summary>
internal static class WslProcess
{
    // wsl.exe can stall while the WSL service starts or updates; a missed answer is retried on the next discovery.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    /// <param name="outputEncoding">wsl.exe's own output (--list) is UTF-16LE; a command it runs writes UTF-8.</param>
    /// <param name="input">Written to the command's standard input, which is how text is handed over without a command line.</param>
    /// <returns>Null when wsl.exe is not installed or did not answer in time.</returns>
    public static WslResult? Run(IEnumerable<string> arguments, Encoding outputEncoding, byte[]? input = null)
    {
        ProcessStartInfo startInfo = new("wsl.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardInput = input is not null,
            StandardOutputEncoding = outputEncoding,
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);
        try
        {
            using Process process = Process.Start(startInfo)!;
            Stopwatch clock = Stopwatch.StartNew();
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            // Its own task, so a command that never reads its input cannot hold the call past the deadline.
            Task writing = input is null ? Task.CompletedTask : Task.Run(() => WriteInput(process, input));
            // One deadline for the whole call: the process, the input and the output.
            bool answered = process.WaitForExit(Timeout)
                && writing.Wait(Remaining(clock))
                && output.Wait(Remaining(clock));
            if (!answered)
            {
                Kill(process);
                Trace.TraceWarning("TokenMojo: wsl.exe did not answer within {0}", Timeout);
                return null;
            }
            return new WslResult(process.ExitCode, output.Result);
        }
        catch (Win32Exception)
        {
            // WSL is not installed.
            return null;
        }
    }

    private static TimeSpan Remaining(Stopwatch clock) => Timeout > clock.Elapsed ? Timeout - clock.Elapsed : TimeSpan.Zero;

    // Closing the input always sends EOF, so a failed write does not leave the command waiting for more.
    private static void WriteInput(Process process, byte[] input)
    {
        try
        {
            process.StandardInput.BaseStream.Write(input);
        }
        // The command exited without reading it all, or was killed; its exit code says whether that mattered.
        catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
        {
        }
        finally
        {
            try
            {
                process.StandardInput.Close();
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or InvalidOperationException)
            {
            }
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        // It ended on its own between the deadline and the kill.
        catch (InvalidOperationException)
        {
        }
    }
}
