using System.Windows;
using System.Windows.Controls;
using TokenMojo.Core;

namespace TokenMojo;

public partial class DashboardView : UserControl
{
    public DashboardView() => InitializeComponent();

    /// <summary>Raised after a session's window was brought forward or its resume was started.</summary>
    public event EventHandler? SessionActed;

    public void Show(DashboardModel model)
    {
        DataContext = model;
        Panel.Visibility = Visibility.Visible;
        MessageText.Visibility = Visibility.Collapsed;
    }

    public void ShowMessage(string message)
    {
        MessageText.Text = message;
        MessageText.Visibility = Visibility.Visible;
        Panel.Visibility = Visibility.Collapsed;
    }

    private void OnRowClick(object sender, RoutedEventArgs e)
    {
        // A click on the row's context menu bubbles up to the row through the logical tree.
        if (e.OriginalSource is DependencyObject source && IsInsideMenu(source)) return;
        if (RowOf(sender) is { } row)
            Acted(SessionControl.Activate(row.Id, row.Status, row.Distro, row.Cwd));
    }

    // Whether Focus applies depends on the windows open now, so it is decided as the menu opens.
    private void OnRowMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu || RowOf(menu.PlacementTarget) is not { } row) return;
        bool hasWindow = SessionControl.HasWindow(row.Id);
        foreach (MenuItem item in menu.Items.OfType<MenuItem>())
        {
            item.IsEnabled = item.Tag switch
            {
                "focus" => hasWindow,
                "resume" => row.CanResume,
                "folder" => row.Distro is not null && row.Cwd is not null,
                _ => true,
            };
        }
    }

    private void OnRowFocus(object sender, RoutedEventArgs e)
    {
        if (RowOfMenuItem(sender) is { } row)
            Acted(SessionControl.Focus(row.Id));
    }

    private void OnRowResume(object sender, RoutedEventArgs e)
    {
        if (RowOfMenuItem(sender) is { Distro: { } distro } row)
            Acted(SessionControl.Resume(row.Id, distro, row.Cwd));
    }

    private void OnRowOpenFolder(object sender, RoutedEventArgs e)
    {
        if (RowOfMenuItem(sender) is { Distro: { } distro, Cwd: { } cwd })
            SessionControl.OpenFolder(distro, cwd);
    }

    private void OnRowCopyId(object sender, RoutedEventArgs e)
    {
        if (RowOfMenuItem(sender) is { } row)
            SessionControl.CopyId(row.Id);
    }

    private void Acted(bool succeeded)
    {
        if (succeeded) SessionActed?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsInsideMenu(DependencyObject element)
    {
        for (DependencyObject? current = element; current is not null; current = ParentOf(current))
        {
            if (current is MenuItem or System.Windows.Controls.ContextMenu) return true;
        }
        return false;
    }

    // Run text and other content elements have no visual parent, only a logical one.
    private static DependencyObject? ParentOf(DependencyObject element) =>
        element is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
            ? System.Windows.Media.VisualTreeHelper.GetParent(element)
            : LogicalTreeHelper.GetParent(element);

    private static SessionRowModel? RowOf(object? element) => (element as FrameworkElement)?.DataContext as SessionRowModel;

    private static SessionRowModel? RowOfMenuItem(object sender) =>
        (sender as MenuItem)?.Parent is ContextMenu menu ? RowOf(menu.PlacementTarget) : null;
}
