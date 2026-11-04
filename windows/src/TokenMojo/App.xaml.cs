using System.Windows;

namespace TokenMojo;

public partial class App : Application
{
    private const string LinkPrefix = "tokenmojo:";

    private readonly CancellationTokenSource _stopPipe = new();
    private Mutex? _singleInstance;
    private TrayController? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Windows starts the exe with a tokenmojo:// link as its argument when one is opened.
        string? link = e.Args.FirstOrDefault(argument => argument.StartsWith(LinkPrefix, StringComparison.OrdinalIgnoreCase));
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\TokenMojo", out bool createdNew);
        if (!createdNew)
        {
            if (link is not null && LinkPipe.Send(link) is { } problem)
                MessageBox.Show(problem, "TokenMojo", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown();
            return;
        }
        Theme.Apply(Resources);
        LinkProtocol.Register();
        TrayController tray = new();
        _tray = tray;
        _ = Task.Run(() => LinkPipe.ServeAsync(
            received => Dispatcher.InvokeAsync(() => tray.FollowLinkAsync(received)), _stopPipe.Token));
        if (link is not null) _ = tray.FollowLinkAsync(link);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _stopPipe.Cancel();
        _tray?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
