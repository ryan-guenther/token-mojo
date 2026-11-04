using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TokenMojo.Core;

namespace TokenMojo;

/// <summary>Edits settings.json. Saving checks the fields as loading does and writes only what differs from the defaults.</summary>
public partial class SettingsWindow : Window
{
    private const string FirstAvailableText = "First available";
    private const string InvalidFileText = "{0} Saving replaces the file with the values shown.";

    private readonly string _settingsPath;
    private readonly Settings _original = new();

    /// <param name="targets">Where sessions can be started now (<see cref="LaunchTargets.Targets"/>).</param>
    public SettingsWindow(string settingsPath, IReadOnlyList<string> targets)
    {
        _settingsPath = settingsPath;
        InitializeComponent();
        WindowChrome.Apply(this, round: false);
        PreviewKeyDown += (_, e) =>
        {
            // IsCancel does nothing for a window shown with Show().
            if (e.Key == Key.Escape) Close();
        };
        try
        {
            _original = Settings.Load(settingsPath);
        }
        catch (SettingsException e)
        {
            ShowError(string.Format(InvalidFileText, e.Message));
        }
        Fill(SettingsForm.From(_original), targets);
    }

    /// <summary>Raised after the file has been written, so the dashboard can pick the new values up at once.</summary>
    public event EventHandler? Saved;

    /// <summary>Brings the window forward, restoring it first when it was minimised.</summary>
    public void Bring()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void Fill(SettingsForm form, IReadOnlyList<string> targets)
    {
        LaunchAtLoginCheck.IsChecked = StartupRegistration.IsEnabled;
        OpenPopOutCheck.IsChecked = form.OpenPopOutAtStart;
        LongTurnBox.Text = form.LongTurnSeconds;
        HoursBox.Text = form.SessionHours;
        RowsBox.Text = form.SessionRows;
        DefaultFolderBox.Text = form.DefaultFolder;
        RepoRootsBox.Text = form.RepoRoots;
        WindowsDefaultFolderBox.Text = form.WindowsDefaultFolder;
        WindowsRepoRootsBox.Text = form.WindowsRepoRoots;
        TeamsBox.Text = form.LinearTeams;
        ActionsBox.Text = form.Actions;
        AllowedOwnersBox.Text = form.AllowedOwners;

        DistroBox.Items.Add(new ComboBoxItem { Content = FirstAvailableText, Tag = null });
        foreach (string target in targets) DistroBox.Items.Add(new ComboBoxItem { Content = target, Tag = target });
        // A configured distro that is not running now is kept, not silently dropped.
        if (form.Distro is { } configured && !targets.Any(target => string.Equals(target, configured, StringComparison.OrdinalIgnoreCase)))
            DistroBox.Items.Add(new ComboBoxItem { Content = $"{configured} (not running)", Tag = configured });
        DistroBox.SelectedItem = DistroBox.Items.Cast<ComboBoxItem>()
            .First(item => string.Equals(item.Tag as string, form.Distro, StringComparison.OrdinalIgnoreCase));
    }

    private SettingsForm Read() => new(
        OpenPopOutCheck.IsChecked == true,
        LongTurnBox.Text,
        HoursBox.Text,
        RowsBox.Text,
        (DistroBox.SelectedItem as ComboBoxItem)?.Tag as string,
        DefaultFolderBox.Text,
        RepoRootsBox.Text,
        WindowsDefaultFolderBox.Text,
        WindowsRepoRootsBox.Text,
        TeamsBox.Text,
        ActionsBox.Text,
        AllowedOwnersBox.Text);

    private void OnSave(object sender, RoutedEventArgs e)
    {
        SettingsResult result = Read().Apply(_original);
        if (result.Settings is not { } settings)
        {
            ShowError(result.Problem!);
            return;
        }
        try
        {
            settings.Save(_settingsPath);
            if ((LaunchAtLoginCheck.IsChecked == true) != StartupRegistration.IsEnabled) StartupRegistration.Toggle();
        }
        catch (Exception error) when (error is SettingsException or System.IO.IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            ShowError(error.Message);
            return;
        }
        Saved?.Invoke(this, EventArgs.Empty);
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void ShowError(string text)
    {
        ErrorText.Text = text;
        ErrorText.Visibility = Visibility.Visible;
    }
}
