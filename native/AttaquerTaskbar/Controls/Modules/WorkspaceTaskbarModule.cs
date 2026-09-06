using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AttaquerTaskbar.Models;
using AttaquerTaskbar.Services;

namespace AttaquerTaskbar.Controls.Modules;

internal sealed class WorkspaceTaskbarModule : ITaskbarModule
{
    private readonly GlazeWmService _service;
    private readonly StackPanel _taskbarRoot;
    private readonly StackPanel _flyoutWorkspaceRow;
    private readonly TextBlock _flyoutStatus;
    private Brush _foreground = Brushes.White;
    private GlazeWmSnapshot _snapshot = GlazeWmSnapshot.Empty;
    private bool _compact;
    private bool _started;

    public WorkspaceTaskbarModule(GlazeWmService service)
    {
        _service = service;
        _taskbarRoot = TaskbarUi.HorizontalPanel();

        var heading = TaskbarUi.Text("Workspaces", 13);
        heading.FontWeight = FontWeights.SemiBold;
        _flyoutWorkspaceRow = TaskbarUi.HorizontalPanel();
        _flyoutWorkspaceRow.Margin = new Thickness(0, 7, 0, 0);
        _flyoutStatus = TaskbarUi.Text("GlazeWM IPC is unavailable", 10, trim: false);
        _flyoutStatus.Opacity = 0.62;
        _flyoutStatus.Margin = new Thickness(0, 7, 0, 0);
        _flyoutStatus.TextWrapping = TextWrapping.Wrap;

        var flyout = new StackPanel();
        flyout.Children.Add(heading);
        flyout.Children.Add(_flyoutWorkspaceRow);
        flyout.Children.Add(_flyoutStatus);
        FlyoutView = flyout;
        TaskbarView = _taskbarRoot;
        RebuildViews();
    }

    public string Id => "workspaces";

    public FrameworkElement TaskbarView { get; }

    public FrameworkElement FlyoutView { get; }

    public event EventHandler? FlyoutRequested;

    public void Start()
    {
        if (_started) return;
        _started = true;
        _service.StateChanged += ApplySnapshot;
        ApplySnapshot(_service.CurrentSnapshot);
    }

    public void Stop()
    {
        if (!_started) return;
        _started = false;
        _service.StateChanged -= ApplySnapshot;
    }

    public void ApplyLayout(bool compact, double availableWidth)
    {
        if (_compact == compact) return;
        _compact = compact;
        RebuildTaskbarButtons();
    }

    public void ApplyTheme(Brush foreground)
    {
        _foreground = foreground;
        _flyoutStatus.Foreground = foreground;
        RebuildViews();
    }

    private void ApplySnapshot(GlazeWmSnapshot snapshot)
    {
        _snapshot = snapshot;
        RebuildViews();
    }

    private void RebuildViews()
    {
        RebuildTaskbarButtons();
        RebuildFlyoutButtons();
        _flyoutStatus.Text = BuildStatus(_snapshot);
    }

    private void RebuildTaskbarButtons()
    {
        _taskbarRoot.Children.Clear();
        if (!_snapshot.IsAvailable || _snapshot.Workspaces.Count == 0)
        {
            var unavailable = TaskbarUi.TransparentButton();
            unavailable.Width = _compact ? 24 : 28;
            unavailable.Height = _compact ? 24 : 28;
            unavailable.Foreground = _foreground;
            unavailable.Content = TaskbarUi.Text("WM", _compact ? 9 : 10);
            unavailable.ToolTip = "GlazeWM IPC unavailable — click for setup details";
            unavailable.Click += (_, _) => FlyoutRequested?.Invoke(this, EventArgs.Empty);
            _taskbarRoot.Children.Add(unavailable);
            return;
        }

        foreach (var workspace in _snapshot.Workspaces)
            _taskbarRoot.Children.Add(CreateWorkspaceButton(workspace, _compact ? 22 : 26, flyout: false));
    }

    private void RebuildFlyoutButtons()
    {
        _flyoutWorkspaceRow.Children.Clear();
        foreach (var workspace in _snapshot.Workspaces)
            _flyoutWorkspaceRow.Children.Add(CreateWorkspaceButton(workspace, 34, flyout: true));
        _flyoutWorkspaceRow.Visibility = _snapshot.Workspaces.Count > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private Button CreateWorkspaceButton(WorkspaceSnapshot workspace, double size, bool flyout)
    {
        var button = TaskbarUi.TransparentButton();
        button.Width = size;
        button.Height = size;
        button.Margin = new Thickness(flyout ? 2 : 1, 0, flyout ? 2 : 1, 0);
        button.Foreground = _foreground;
        button.Background = workspace.HasFocus
            ? new SolidColorBrush(Color.FromArgb(0x58, 0x00, 0x78, 0xD4))
            : workspace.IsDisplayed
                ? new SolidColorBrush(Color.FromArgb(0x26, 0x80, 0x80, 0x80))
                : Brushes.Transparent;
        button.ToolTip = workspace.HasFocus
            ? $"Workspace {workspace.Name} (focused)"
            : $"Switch to workspace {workspace.Name}";

        var label = TaskbarUi.Text(workspace.Label, flyout ? 12 : _compact ? 9 : 10, trim: true);
        label.FontWeight = workspace.HasFocus ? FontWeights.SemiBold : FontWeights.Normal;
        label.MaxWidth = Math.Max(16, size - 4);
        button.Content = label;
        button.Click += async (_, _) => await _service.FocusWorkspaceAsync(workspace.Name);
        return button;
    }

    private static string BuildStatus(GlazeWmSnapshot snapshot)
    {
        if (!snapshot.IsAvailable)
            return "GlazeWM IPC is unavailable. Set ipc.enabled: true in your GlazeWM config, then reload GlazeWM.";
        if (snapshot.Workspaces.Count == 0) return "Connected, but GlazeWM reported no active workspaces.";
        if (!snapshot.AutoTileEnabled) return "Connected · automatic insertion direction is off.";
        return snapshot.AutoTileDirection is { Length: > 0 } direction
            ? $"Connected · automatic insertion direction: {direction}"
            : "Connected · automatic insertion direction is waiting for a focused tiled window.";
    }
}
