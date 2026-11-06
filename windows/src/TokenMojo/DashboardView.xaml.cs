using System.Windows;
using System.Windows.Controls;
using TokenMojo.Core;

namespace TokenMojo;

public partial class DashboardView : UserControl
{
    // The session whose row the left button went down on; a click is a press and a release on the same session.
    // By id, because the refresh rebuilds the row elements and one can be replaced between press and release.
    private string? _pressedSessionId;

    // The width the tiles are laid out for; FillWindow lets the view grow past it.
    private readonly double _naturalWidth;

    // Every session row is the same height; known once one has been laid out.
    private double _rowHeight;

    public DashboardView()
    {
        InitializeComponent();
        _naturalWidth = Width;
    }

    /// <summary>Raised after a session's window was brought forward or its resume was started.</summary>
    public event EventHandler? SessionActed;

    /// <summary>Raised when the "+" in the Sessions header is clicked.</summary>
    public event EventHandler? NewSessionRequested;

    public void Show(DashboardModel model)
    {
        DataContext = model;
        Panel.Visibility = Visibility.Visible;
        MessageText.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Fills a window the user sizes, in place of the fixed width and session list height that keep the flyout on screen.
    /// </summary>
    public void FillWindow()
    {
        Width = double.NaN;
        SessionScroll.MaxHeight = double.PositiveInfinity;
    }

    /// <summary>
    /// The smallest size that shows every tile at its natural width and the given number of session rows,
    /// or null while a message is shown or before the first layout.
    /// Until a row has been laid out, the session list counts at its full height.
    /// </summary>
    public Size? MinimumSize(int rows)
    {
        if (Panel.Visibility != Visibility.Visible || !IsLoaded) return null;
        if (FirstRow() is { ActualHeight: > 0 } row) _rowHeight = row.ActualHeight;
        double list = _rowHeight > 0 ? rows * _rowHeight : SessionScroll.ExtentHeight;
        return new Size(_naturalWidth, ActualHeight - SessionScroll.ActualHeight + list);
    }

    private FrameworkElement? FirstRow()
    {
        ItemsControl list = OpenRows.Items.Count > 0 ? OpenRows : ClosedRows;
        return list.ItemContainerGenerator.ContainerFromIndex(0) as FrameworkElement;
    }

    public void ShowMessage(string message)
    {
        MessageText.Text = message;
        MessageText.Visibility = Visibility.Visible;
        Panel.Visibility = Visibility.Collapsed;
    }

    private void OnNewSession(object sender, RoutedEventArgs e) => NewSessionRequested?.Invoke(this, EventArgs.Empty);

    private void OnRowPress(object sender, RoutedEventArgs e)
    {
        _pressedSessionId = e.OriginalSource is DependencyObject source && IsInsideMenu(source) ? null : RowOf(sender)?.Id;
    }

    // Leaving the row cancels the click, so a stale press cannot pair with a later release.
    private void OnRowLeave(object sender, RoutedEventArgs e) => _pressedSessionId = null;

    private async void OnRowClick(object sender, RoutedEventArgs e)
    {
        string? pressedId = _pressedSessionId;
        _pressedSessionId = null;
        // A click on the row's context menu bubbles up to the row through the logical tree. A release with no
        // press on this row is a click that dismissed the menu, or a drag from another row.
        if (e.OriginalSource is DependencyObject source && IsInsideMenu(source)) return;
        if (RowOf(sender) is { } row && row.Id == pressedId)
            Acted(await SessionControl.ActivateAsync(SessionTarget.From(row)));
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
                "folder" => row.Cwd is not null,
                _ => true,
            };
        }
    }

    private void OnRowFocus(object sender, RoutedEventArgs e)
    {
        if (RowOfMenuItem(sender) is { } row)
            Acted(SessionControl.Focus(row.Id));
    }

    private async void OnRowResume(object sender, RoutedEventArgs e)
    {
        if (RowOfMenuItem(sender) is { } row)
            Acted(await SessionControl.ResumeAsync(SessionTarget.From(row)));
    }

    private async void OnRowOpenFolder(object sender, RoutedEventArgs e)
    {
        if (RowOfMenuItem(sender) is { } row)
            await SessionControl.OpenFolderAsync(SessionTarget.From(row));
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
