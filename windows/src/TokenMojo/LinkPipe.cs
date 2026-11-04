using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using TokenMojo.Core;

namespace TokenMojo;

/// <summary>
/// Hands a tokenmojo:// link from a second TokenMojo, started by Windows for the link, to the one already running.
/// The pipe is open only to the current user and named per user and logon session, like the single-instance mutex.
/// </summary>
internal static class LinkPipe
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(5);
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);
    private static readonly string Name = BuildName();

    /// <summary>Sends the link to the running TokenMojo. Returns what to show when it could not, else null.</summary>
    public static string? Send(string link)
    {
        try
        {
            using NamedPipeClientStream pipe = new(".", Name, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            pipe.Connect(ConnectTimeout);
            // This process was started for the user's click, so it may let the running one bring a window forward.
            if (Native.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out uint serverId)) Native.AllowSetForegroundWindow(serverId);
            byte[] bytes = Utf8.GetBytes(link);
            pipe.Write(bytes, 0, bytes.Length);
            pipe.Flush();
            return null;
        }
        catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("TokenMojo: cannot pass the link to the running TokenMojo: {0}", e.Message);
            return $"Could not pass the link to the running TokenMojo: {e.Message}";
        }
    }

    /// <summary>
    /// Receives links until <paramref name="stop"/> is cancelled, one sender at a time, handing each to
    /// <paramref name="received"/> on the pipe's thread. Never throws.
    /// </summary>
    public static async Task ServeAsync(Action<string> received, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                string? link = await ReceiveOneAsync(stop);
                if (link is not null) received(link);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or OperationCanceledException)
            {
                // A sender that gave up or wrote too slowly, or a pipe that could not be made: wait a moment, then make a fresh one.
                Trace.TraceWarning("TokenMojo: cannot receive a link: {0}", e.Message);
                try
                {
                    await Task.Delay(RetryDelay, stop);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private static string BuildName()
    {
        using WindowsIdentity user = WindowsIdentity.GetCurrent();
        using Process current = Process.GetCurrentProcess();
        return $"TokenMojo-{user.User?.Value}-{current.SessionId}";
    }

    // A link is at most MaxLength characters, so anything longer is read only that far and refused by the parser.
    private static async Task<string?> ReceiveOneAsync(CancellationToken stop)
    {
        await using NamedPipeServerStream pipe = new(
            Name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.WaitForConnectionAsync(stop);
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
        timeout.CancelAfter(ReadTimeout);
        using StreamReader reader = new(pipe, Utf8);
        char[] buffer = new char[TokenMojoLink.MaxLength + 1];
        int length = 0;
        while (length < buffer.Length)
        {
            int read = await reader.ReadAsync(buffer.AsMemory(length), timeout.Token);
            if (read == 0) break;
            length += read;
        }
        return length == 0 ? null : new string(buffer, 0, length);
    }
}
