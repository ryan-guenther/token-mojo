namespace TokenMojo;

internal sealed record TerminalWindow(IntPtr Handle, string Title);

/// <summary>Windows Terminal's top-level windows. Their title is the active tab's, which is where `tm` puts its tag.</summary>
internal static class TerminalWindows
{
    private const string WindowClass = "CASCADIA_HOSTING_WINDOW_CLASS";

    public static IReadOnlyList<TerminalWindow> List()
    {
        List<TerminalWindow> windows = new();
        Native.EnumWindows((window, _) =>
        {
            if (Native.IsWindowVisible(window) && Native.ClassName(window) == WindowClass)
                windows.Add(new TerminalWindow(window, Native.WindowText(window)));
            return true;
        }, IntPtr.Zero);
        return windows;
    }

    /// <summary>Brings the window forward, restoring it first when minimised. False when Windows refuses to switch to it.</summary>
    public static bool Focus(IntPtr window)
    {
        if (Native.IsIconic(window)) Native.ShowWindow(window, Native.ShowRestore);
        return Native.SetForegroundWindow(window);
    }

    /// <summary>The foreground window's title when it is a terminal window, else null.</summary>
    public static string? ForegroundTitle()
    {
        IntPtr window = Native.GetForegroundWindow();
        return Native.ClassName(window) == WindowClass ? Native.WindowText(window) : null;
    }
}
