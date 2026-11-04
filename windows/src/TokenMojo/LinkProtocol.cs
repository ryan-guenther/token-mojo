using System.Diagnostics;
using System.Security;
using Microsoft.Win32;

namespace TokenMojo;

/// <summary>
/// Registers tokenmojo:// for the current user, so a link opens this exe with the link as its argument. Written on every
/// start, only where it differs, so a moved exe repairs its own registration.
/// </summary>
internal static class LinkProtocol
{
    private const string ClassKey = @"Software\Classes\tokenmojo";
    private const string CommandKey = ClassKey + @"\shell\open\command";

    /// <summary>Never throws: without the registration links do nothing, which is no reason to stop the tray.</summary>
    public static void Register()
    {
        if (Environment.ProcessPath is not { } exe)
        {
            Trace.TraceWarning("TokenMojo: cannot register tokenmojo:// links: the exe path is unknown");
            return;
        }
        try
        {
            using RegistryKey scheme = Registry.CurrentUser.CreateSubKey(ClassKey);
            SetIfDifferent(scheme, "", "URL:TokenMojo");
            SetIfDifferent(scheme, "URL Protocol", "");
            using RegistryKey command = Registry.CurrentUser.CreateSubKey(CommandKey);
            SetIfDifferent(command, "", $"\"{exe}\" \"%1\"");
        }
        catch (Exception e) when (e is UnauthorizedAccessException or SecurityException or System.IO.IOException)
        {
            Trace.TraceWarning("TokenMojo: cannot register tokenmojo:// links: {0}", e.Message);
        }
    }

    private static void SetIfDifferent(RegistryKey key, string name, string value)
    {
        if (key.GetValue(name) as string != value) key.SetValue(name, value);
    }
}
