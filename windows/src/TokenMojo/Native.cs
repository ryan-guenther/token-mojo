using System.Runtime.InteropServices;

namespace TokenMojo;

/// <summary>The few Win32 calls WPF has no API for: exact window placement, per-monitor DPI, DWM chrome and other programs' windows.</summary>
internal static partial class Native
{
    public const uint NoSizeNoZOrderNoActivate = 0x0001 | 0x0004 | 0x0010;
    public const int ShowRestore = 9;
    private const uint MonitorDefaultToNearest = 2;
    private const int EffectiveDpi = 0;
    private const int TextCapacity = 512;

    public static double ScaleAt(System.Drawing.Point point)
    {
        IntPtr monitor = MonitorFromPoint(new PointStruct(point.X, point.Y), MonitorDefaultToNearest);
        return GetDpiForMonitor(monitor, EffectiveDpi, out uint dpiX, out _) == 0 ? dpiX / 96.0 : 1.0;
    }

    public static System.Drawing.Point TopLeft(IntPtr window)
    {
        GetWindowRect(window, out RectStruct rect);
        return new System.Drawing.Point(rect.Left, rect.Top);
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    public static unsafe string ClassName(IntPtr window)
    {
        char* buffer = stackalloc char[TextCapacity];
        return new string(buffer, 0, GetClassNameW(window, buffer, TextCapacity));
    }

    public static unsafe string WindowText(IntPtr window)
    {
        char* buffer = stackalloc char[TextCapacity];
        return new string(buffer, 0, GetWindowTextW(window, buffer, TextCapacity));
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(IntPtr window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(IntPtr window);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(IntPtr window, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(IntPtr window);

    [LibraryImport("user32.dll")]
    public static partial IntPtr GetForegroundWindow();

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [LibraryImport("user32.dll")]
    private static unsafe partial int GetClassNameW(IntPtr window, char* buffer, int capacity);

    [LibraryImport("user32.dll")]
    private static unsafe partial int GetWindowTextW(IntPtr window, char* buffer, int capacity);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowRect(IntPtr window, out RectStruct rect);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromPoint(PointStruct point, uint flags);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct PointStruct(int X, int Y);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct RectStruct(int Left, int Top, int Right, int Bottom);
}
