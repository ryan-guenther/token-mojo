using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Win32;
using TokenMojo.Core;

namespace TokenMojo;

/// <summary>
/// Starts a Claude Code session in a new terminal window: a prompt, a folder and an optional worktree.
/// A window of its own, because the flyout closes when it loses focus, which the folder dialog would cause.
/// Anything that touches the WSL share runs off the UI thread; failures are shown, never thrown.
/// </summary>
public partial class LauncherWindow : Window
{
    private const string NoDistroText = "No WSL distro is running. Open one, then reopen this window.";
    private const string NotOnDistroText =
        "Pick a folder inside {0}. Folders on other drives are not available to sessions started here.";
    private const string FolderInvalidText = "The folder must be ~, a path starting with ~/, or a path starting with /.";
    private const string WorktreeNameMissingText = "Enter a worktree name, or untick Worktree.";

    private readonly string _settingsPath;
    private readonly ClaudeDirLocator _locator;
    private readonly IWslDistros _wsl;
    private Settings? _settings;
    private IReadOnlyList<ClaudeSource> _sources = [];
    private IReadOnlyList<SessionSnapshot> _sessions = [];
    private IReadOnlyList<RepoInfo> _repos = [];
    private string? _distro;
    private string? _user;
    private string? _home;
    // Why the launch user is not the distro's default one; shown beside the link hint.
    private string? _userNote;
    // The problem with the launch user, shown again when Start is pressed.
    private string? _userProblem;
    // Set while the code fills in a field, so only the user's own edits mark it as chosen.
    private bool _applying;
    private bool _folderChosen;
    private bool _worktreeChosen;
    private bool _starting;
    // True from a distro change until its user has been chosen; Start waits.
    private bool _choosing;
    private bool _closed;
    // Each distro change starts a scan; only the latest one may update the window.
    private int _generation;

    public LauncherWindow(string settingsPath, ClaudeDirLocator locator, IWslDistros wsl)
    {
        _settingsPath = settingsPath;
        _locator = locator;
        _wsl = wsl;
        InitializeComponent();
        WindowChrome.Apply(this, round: false);
        FolderBox.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(OnFolderTextChanged));
        Loaded += async (_, _) => await InitializeAsync();
        // A launch in flight is never half-abandoned: the window stays until it has finished.
        Closing += (_, e) => e.Cancel = _starting;
        Closed += (_, _) => _closed = true;
    }

    /// <summary>Brings the window forward, restoring it first when it was minimised.</summary>
    public void Bring()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        PromptBox.Focus();
    }

    private async Task InitializeAsync()
    {
        PromptBox.Focus();
        LauncherData data;
        try
        {
            data = await Task.Run(LoadData);
        }
        catch (SettingsException e)
        {
            ShowError(e.Message);
            return;
        }
        if (_closed) return;
        _settings = data.Settings;
        _sources = data.Sources;
        _sessions = data.Sessions;
        _distro = LaunchTargets.ChooseDistro(data.Settings.Distro, data.Distros);
        if (_distro is null)
        {
            ShowError(NoDistroText);
            return;
        }
        ShowDistros(data.Distros, _distro);
        await ChooseDistroAsync();
    }

    // Settings are read each time, so fixing a broken file takes effect on the next open.
    private LauncherData LoadData()
    {
        Settings settings = Settings.Load(_settingsPath);
        IReadOnlyList<string> distros = _wsl.Running();
        IReadOnlyList<ClaudeSource> sources = _locator.Locate();
        return new LauncherData(settings, distros, sources, SessionStore.LoadSessions(sources, DateTimeOffset.Now));
    }

    private void ShowDistros(IReadOnlyList<string> distros, string chosen)
    {
        if (distros.Count < 2) return;
        _applying = true;
        DistroBox.ItemsSource = distros;
        DistroBox.SelectedItem = chosen;
        _applying = false;
        DistroCaption.Visibility = Visibility.Visible;
        DistroBox.Visibility = Visibility.Visible;
    }

    private async Task ChooseDistroAsync()
    {
        int generation = ++_generation;
        string distro = _distro!;
        // Nothing of the previous distro may be used to start a session in this one.
        _user = null;
        _home = null;
        _userNote = null;
        _userProblem = null;
        _choosing = true;
        UpdateStartEnabled();
        LaunchUser choice = await ChooseUserAsync(distro);
        if (_closed || generation != _generation) return;
        _repos = [];
        // A new home starts a new generation, which the scan below must carry.
        ApplyUser(choice, distro);
        generation = _generation;
        _choosing = false;
        UpdateStartEnabled();
        await ScanReposAsync(distro, generation);
    }

    // Only the generation it started in may use the answer: a newer distro or home has its own scan.
    private async Task ScanReposAsync(string distro, int generation)
    {
        Settings settings = _settings!;
        string? home = _home;
        IReadOnlyList<RepoInfo> repos = await Task.Run(() => RepoScanner.Scan(distro, home, settings.RepoRoots));
        if (_closed || generation != _generation) return;
        _repos = repos;
        ShowFolders(FolderList.Build(settings.DefaultFolder, distro, home, _sessions, repos));
        ApplyHint();
    }

    private void UpdateStartEnabled() => StartButton.IsEnabled = !_starting && !_choosing;

    // Asks the distro about each candidate's tm.
    private Task<LaunchUser> ChooseUserAsync(string distro)
    {
        IReadOnlyList<ClaudeSource> sources = _sources;
        WslTmInspector inspector = new();
        return Task.Run(() => LaunchTargets.ChooseUser(sources, distro, source => inspector.Inspect(distro, source.User!)));
    }

    // Usable at once with the default and recent folders; the scan adds the repositories when it finishes.
    // True when the home changed, which invalidates a scan in flight (it ends its generation) and empties the repository list.
    private bool ApplyUser(LaunchUser choice, string distro)
    {
        bool homeChanged = _home != choice.Source?.Home;
        _user = choice.Source?.User;
        _home = choice.Source?.Home;
        if (homeChanged)
        {
            _generation++;
            _repos = [];
        }
        _userNote = choice.Note;
        _userProblem = choice.Problem;
        HideError();
        if (choice.Problem is not null) ShowError(choice.Problem);
        ShowFolders(FolderList.Build(_settings!.DefaultFolder, distro, _home, _sessions, _repos));
        ApplyHint();
        return homeChanged;
    }

    // Only the list changes: replacing the items can clear an editable box, and the user may be typing in it
    // while the scan finishes, so the text and the caret are put back as they were.
    private void ShowFolders(IReadOnlyList<string> folders)
    {
        FolderBox.ApplyTemplate();
        TextBox? editor = FolderBox.Template.FindName("PART_EditableTextBox", FolderBox) as TextBox;
        string text = FolderBox.Text;
        int selectionStart = editor?.SelectionStart ?? 0;
        int selectionLength = editor?.SelectionLength ?? 0;
        _applying = true;
        FolderBox.ItemsSource = folders;
        if (FolderBox.Text != text) FolderBox.Text = text;
        editor?.Select(selectionStart, selectionLength);
        _applying = false;
    }

    // A link in the prompt fills in the folder and worktree unless the user already chose them.
    private void ApplyHint()
    {
        if (_settings is null) return;
        LaunchHint hint = LaunchHints.From(PromptBox.Text, _settings, _repos);
        string[] lines = new[] { hint.Description, _userNote }.OfType<string>().ToArray();
        HintText.Text = string.Join("\n", lines);
        HintText.Visibility = lines.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _applying = true;
        if (!_folderChosen)
            FolderBox.Text = hint.Folder is { } folder ? LinuxFolder.Abbreviate(folder, _home) : _settings.DefaultFolder;
        if (!_worktreeChosen)
        {
            WorktreeCheck.IsChecked = hint.UseWorktree;
            WorktreeBox.Text = hint.Worktree ?? "";
            WorktreeBox.IsEnabled = hint.UseWorktree;
        }
        _applying = false;
    }

    private async Task StartAsync()
    {
        if (_starting || _settings is null || _distro is not { } distro) return;
        HideError();
        string folder = FolderBox.Text.Trim();
        string? worktree = WorktreeCheck.IsChecked == true ? WorktreeBox.Text.Trim() : null;
        if (!LinuxFolder.IsValid(folder))
        {
            ShowError(FolderInvalidText);
            return;
        }
        if (worktree is "")
        {
            ShowError(WorktreeNameMissingText);
            return;
        }
        _starting = true;
        UpdateStartEnabled();
        CancelButton.IsEnabled = false;
        string? error;
        try
        {
            error = await StartSessionAsync(distro, folder, worktree);
        }
        // This is the edge of an async void handler: nothing may escape it, and the window must stay closable.
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException or AggregateException)
        {
            System.Diagnostics.Trace.TraceWarning("TokenMojo: cannot start a session: {0}", e.Message);
            error = $"Could not start the session: {e.Message}";
        }
        finally
        {
            _starting = false;
            if (!_closed) CancelButton.IsEnabled = true;
        }
        if (_closed) return;
        if (error is null)
        {
            Close();
            return;
        }
        ShowError(error);
        UpdateStartEnabled();
    }

    // Returns what to show the user when the session could not be started, else null.
    private async Task<string?> StartSessionAsync(string distro, string folder, string? worktree)
    {
        if (_user is null)
        {
            // A check that failed (or found nothing) when the window opened must not stick: Start asks again.
            int generation = _generation;
            LaunchUser choice = await ChooseUserAsync(distro);
            if (_closed || generation != _generation) return "The distro changed; press Start again.";
            if (ApplyUser(choice, distro)) _ = ScanReposAsync(distro, _generation);
            if (choice.Source is null) return choice.Problem;
        }
        LaunchRequest request = new(distro, _user!, folder, worktree, PromptBox.Text, _home);
        return await Task.Run(() => TryLaunch(request));
    }

    private static string? TryLaunch(LaunchRequest request)
    {
        try
        {
            // Checked again: it was inspected when the window opened, which may be a while ago.
            if (SessionControl.TmProblem(request.Distro, request.User, request.Home) is { } problem) return problem;
            string? start = LinuxFolder.Expand(request.Folder, request.Home);
            if (start is null || !Directory.Exists(WslPaths.SharePath(request.Distro, start)))
                return $"The folder does not exist in {request.Distro}: {request.Folder}";
            SessionLauncher.Launch(request, new WslPromptHandoff(), TerminalProcess.Start);
            return null;
        }
        catch (Exception e) when (e is Win32Exception or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            System.Diagnostics.Trace.TraceWarning("TokenMojo: cannot start a session: {0}", e.Message);
            return $"Could not start the session: {e.Message}";
        }
    }

    private async void OnStart(object sender, RoutedEventArgs e) => await StartAsync();

    // IsCancel only closes a window shown with ShowDialog, and this one is shown with Show. Closing is refused while a launch is in flight.
    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    // Enter starts; Shift+Enter falls through to the box, which adds a line.
    private async void OnPromptKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || (Keyboard.Modifiers & ModifierKeys.Shift) != 0) return;
        e.Handled = true;
        if (StartButton.IsEnabled) await StartAsync();
    }

    private void OnPromptChanged(object sender, TextChangedEventArgs e) => ApplyHint();

    private void OnFolderTextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_applying) _folderChosen = true;
    }

    private void OnWorktreeToggled(object sender, RoutedEventArgs e)
    {
        _worktreeChosen = true;
        bool on = WorktreeCheck.IsChecked == true;
        WorktreeBox.IsEnabled = on;
        if (on) WorktreeBox.Focus();
    }

    private void OnWorktreeNameChanged(object sender, TextChangedEventArgs e)
    {
        if (!_applying) _worktreeChosen = true;
    }

    private async void OnDistroChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_applying || DistroBox.SelectedItem is not string distro) return;
        _distro = distro;
        _folderChosen = false;
        await ChooseDistroAsync();
    }

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        if (_distro is not { } distro) return;
        OpenFolderDialog dialog = new()
        {
            Title = $"Choose a folder in {distro}",
            InitialDirectory = WslPaths.SharePath(distro, _home ?? "/"),
        };
        if (dialog.ShowDialog(this) != true) return;
        if (WslPaths.ToLinuxPath(dialog.FolderName, distro) is not { } linuxPath)
        {
            ShowError(string.Format(NotOnDistroText, distro));
            return;
        }
        HideError();
        _applying = true;
        FolderBox.Text = LinuxFolder.Abbreviate(linuxPath, _home);
        _applying = false;
        _folderChosen = true;
    }

    private void ShowError(string text)
    {
        ErrorText.Text = text;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void HideError() => ErrorText.Visibility = Visibility.Collapsed;

    private sealed record LauncherData(
        Settings Settings, IReadOnlyList<string> Distros, IReadOnlyList<ClaudeSource> Sources, IReadOnlyList<SessionSnapshot> Sessions);
}
