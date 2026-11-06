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

    private readonly IntPtr _handle;

    public PopOutWindow()
    {
        InitializeComponent();
        WindowChrome.Apply(this, round: false);
        _handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(_handle).AddHook(OnMessage);
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
            Dashboard.FillWindow();
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

    // Saved when a move or resize ends, not on close: closing only hides it, and a pop-out left open
    // until Quit or signing out must keep where it was moved.
    private IntPtr OnMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == ExitSizeMove) SavePlacement();
        return IntPtr.Zero;
    }

    // The window fits its content until the user first resizes it, which turns SizeToContent off.
    // Only then is a size saved, so a pop-out that was only moved keeps fitting its content.
    private void SavePlacement()
    {
        bool sized = SizeToContent == SizeToContent.Manual;
        if (sized) Dashboard.FillWindow();
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
