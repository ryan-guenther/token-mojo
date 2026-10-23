using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using TokenMojo.Core;
using Forms = System.Windows.Forms;

namespace TokenMojo;

/// <summary>The notification-area icon: polls the bridge files and keeps the icon, flyout and pop-out current.</summary>
internal sealed class TrayController : IDisposable
{
    // The bridge replaces the file via rename, so poll rather than watch it.
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DiscoveryInterval = TimeSpan.FromMinutes(1);
    private const string NoDataMessage =
        "No bridge data yet. Enable the statusline bridge with install.sh, then start a Claude Code session.";

    private static readonly TimeSpan BalloonDuration = TimeSpan.FromSeconds(10);

    private readonly string _windowsHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TokenMojo", "settings.json");
    private readonly ClaudeDirLocator _locator;
    private readonly Forms.NotifyIcon _notifyIcon = new();
    private readonly Forms.ToolStripMenuItem _launchAtLogin = new("Launch at Login");
    private readonly DispatcherTimer _timer = new() { Interval = RefreshInterval };
    private readonly FlyoutWindow _flyout = new();
    private PopOutWindow? _popOut;
    private IReadOnlyList<ClaudeSource> _sources = [];
    private DateTimeOffset _lastDiscovery = DateTimeOffset.MinValue;
    private BridgeSnapshot? _latest;
    private IReadOnlyDictionary<string, SeenStatus>? _alertBaseline;
    private string? _balloonSessionId;
    private bool _refreshing;

    public TrayController()
    {
        _locator = new ClaudeDirLocator(_windowsHome, new WslDistros(), Environment.GetEnvironmentVariable);
        _flyout.PopOutRequested += (_, _) => ShowPopOut();

        _launchAtLogin.Click += (_, _) => StartupRegistration.Toggle();
        Forms.ContextMenuStrip menu = new();
        menu.Items.Add(new Forms.ToolStripMenuItem("Open Dashboard", null, (_, _) => _flyout.Toggle()) { Font = new System.Drawing.Font(menu.Font, System.Drawing.FontStyle.Bold) });
        menu.Items.Add(new Forms.ToolStripMenuItem("Pop Out Window", null, (_, _) => ShowPopOut()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(_launchAtLogin);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Quit", null, (_, _) => Application.Current.Shutdown()));
        menu.Opening += (_, _) => _launchAtLogin.Checked = StartupRegistration.IsEnabled;

        _notifyIcon.ContextMenuStrip = menu;
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) _flyout.Toggle();
        };
        _notifyIcon.BalloonTipClicked += async (_, _) => await OnBalloonClickedAsync();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        Render(null, DateTimeOffset.Now);
        _notifyIcon.Visible = true;

        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();
        _ = RefreshAsync();
    }

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _timer.Stop();
        // Hide first, or the icon lingers in the tray until the mouse passes over it.
        _notifyIcon.Visible = false;
        _notifyIcon.Icon?.Dispose();
        _notifyIcon.Dispose();
    }

    private async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            // WSL shares are network paths, so read them off the UI thread.
            BridgeSnapshot snapshot = await Task.Run(Load);
            _latest = snapshot;
            Render(snapshot, DateTimeOffset.Now);
            RaiseAlerts(snapshot);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private BridgeSnapshot Load()
    {
        DateTimeOffset now = DateTimeOffset.Now;
        if (now - _lastDiscovery >= DiscoveryInterval)
        {
            _sources = _locator.Locate();
            _lastDiscovery = now;
        }
        try
        {
            // Read every refresh, so fixing a broken file takes effect without a restart.
            Settings settings = Settings.Load(_settingsPath);
            LoadedState? current = SessionStore.LoadCurrent(_sources);
            return new BridgeSnapshot(current, current is null ? [] : SessionStore.LoadSessions(_sources, now), null, settings);
        }
        catch (SettingsException e)
        {
            return new BridgeSnapshot(null, [], e.Message, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // The newest state file was replaced or its distro stopped mid-read; rediscover on the next tick.
            _lastDiscovery = DateTimeOffset.MinValue;
            Trace.TraceWarning("TokenMojo: cannot read bridge files: {0}", e.Message);
            return new BridgeSnapshot(null, [], e.Message, null);
        }
    }

    private void RaiseAlerts(BridgeSnapshot snapshot)
    {
        // Without data or settings there is nothing to compare, so keep the last baseline.
        if (snapshot.Current is null || snapshot.Settings is null) return;
        // The user is already looking at this session's window.
        IReadOnlyList<StatusAlert> alerts = StatusAlerts.Diff(_alertBaseline, snapshot.Sessions, snapshot.Settings.LongTurn)
            .Where(alert => !SessionControl.IsForeground(alert.SessionId))
            .ToList();
        _alertBaseline = StatusAlerts.Advance(_alertBaseline, snapshot.Sessions, DateTimeOffset.Now);
        if (alerts.Count == 0) return;
        AlertBalloon balloon = StatusAlerts.Balloon(alerts);
        // A balloon listing several sessions leads to the first.
        _balloonSessionId = alerts[0].SessionId;
        _notifyIcon.ShowBalloonTip((int)BalloonDuration.TotalMilliseconds, balloon.Title, balloon.Text, Forms.ToolTipIcon.Info);
    }

    private async Task OnBalloonClickedAsync()
    {
        SessionSnapshot? session = _latest?.Sessions.FirstOrDefault(candidate => candidate.Id == _balloonSessionId);
        if (session is null)
            _flyout.Toggle();
        else
            await SessionControl.ActivateAsync(SessionTarget.From(session));
    }

    private void Render(BridgeSnapshot? snapshot, DateTimeOffset now)
    {
        System.Drawing.Icon? previous = _notifyIcon.Icon;
        if (snapshot?.Current is not { } current)
        {
            _notifyIcon.Icon = TrayIcon.Render(new GlanceGauge("", null), Theme.TaskbarUsesLightTheme, waitingCount: 0);
            _notifyIcon.Text = "TokenMojo: no bridge data yet";
            string message = snapshot?.Error ?? NoDataMessage;
            _flyout.View.ShowMessage(message);
            _popOut?.View.ShowMessage(message);
        }
        else
        {
            IReadOnlyList<GlanceGauge> gauges = Gauges.Glance(current.State, now);
            DashboardModel model = Dashboard.Build(current, snapshot.Sessions, now, _windowsHome);
            _notifyIcon.Icon = TrayIcon.Render(Gauges.Worst(gauges), Theme.TaskbarUsesLightTheme, model.WaitingCount);
            _notifyIcon.Text = Gauges.Summary(gauges, current.State, model.WaitingCount);
            _flyout.View.Show(model);
            _popOut?.View.Show(model);
        }
        previous?.Dispose();
    }

    private void ShowPopOut()
    {
        if (_popOut is null)
        {
            _popOut = new PopOutWindow();
            Render(_latest, DateTimeOffset.Now);
        }
        _popOut.ShowFront();
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        Theme.Apply(Application.Current.Resources);
        WindowChrome.Apply(_flyout, round: true);
        if (_popOut is not null) WindowChrome.Apply(_popOut, round: false);
        Render(_latest, DateTimeOffset.Now);
    }

    private sealed record BridgeSnapshot(LoadedState? Current, IReadOnlyList<SessionSnapshot> Sessions, string? Error, Settings? Settings);
}
