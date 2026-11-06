using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;

namespace TokenMojo;

/// <summary>The always-on-top dashboard window, the Mac app's floating panel.</summary>
public partial class PopOutWindow : Window
{
    // Sent once a move or resize by the user ends, by mouse or keyboard.
    private const int ExitSizeMove = 0x0232;

    // The session rows a resized pop-out always has room for.
    private const int MinimumSessionRows = 3;

    private readonly IntPtr _handle;

    public PopOutWindow()
    {
        InitializeComponent();
        WindowChrome.Apply(this, round: false);
        _handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_handle).AddHook(OnMessage);
        LayoutUpdated += KeepMinimumSize;
        PopOutPlacement.Placement? placement = PopOutPlacement.Load();
        if (placement is null) return;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Native.SetWindowPos(_handle, IntPtr.Zero, placement.Position.X, placement.Position.Y, 0, 0,
            Native.NoSizeNoZOrderNoActivate);
        if (placement.HasSize)
        {
            SizeToContent = SizeToContent.Manual;
            Width = placement.Width;
            Height = placement.Height;
        }
    }

    public DashboardView View => Dashboard;

    public void ShowFront()
    {
        Show();
        Activate();
    }

    // Closing only hides it, so the next Pop Out reopens it where it was.
    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    // The window fits its content until the user first resizes it, which turns SizeToContent off; from then on
    // the dashboard fills it, down to a minimum recomputed after every layout, since the rings and the context bar
    // come and go with the data. The window frame is whatever the window has beyond the dashboard.
    private void KeepMinimumSize(object? sender, EventArgs e)
    {
        if (SizeToContent != SizeToContent.Manual) return;
        Dashboard.FillWindow();
        if (Dashboard.MinimumSize(MinimumSessionRows) is not { } content) return;
        MinWidth = content.Width + ActualWidth - Dashboard.ActualWidth;
        MinHeight = content.Height + ActualHeight - Dashboard.ActualHeight;
    }

    // Saved when a move or resize ends, not on close: closing only hides it, and a pop-out left open
    // until Quit or signing out must keep where it was moved.
    private IntPtr OnMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == ExitSizeMove) SavePlacement();
        return IntPtr.Zero;
    }

    // A size is saved only once the user has resized it, so a pop-out that was only moved keeps fitting its content.
    private void SavePlacement()
    {
        bool sized = SizeToContent == SizeToContent.Manual;
        try
        {
            PopOutPlacement.Save(new(Native.TopLeft(_handle), sized ? ActualWidth : 0, sized ? ActualHeight : 0));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("TokenMojo: cannot save the pop-out placement: {0}", e.Message);
        }
    }
}
