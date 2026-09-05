using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AttaquerTaskbar.Controls.Modules;
using AttaquerTaskbar.Models;
using AttaquerTaskbar.Services;
using Microsoft.Win32;

namespace AttaquerTaskbar.Controls;

public sealed class TaskbarContent : UserControl
{
    private const double CompactHeightThreshold = 40;

    private readonly SettingsService _settings;
    private readonly ThermalTaskbarModule _thermalModule;
    private readonly MediaTaskbarModule _mediaModule;
    private readonly ITaskbarModule[] _modules;
    private readonly Grid _layoutRoot;
    private readonly Border _separator;
    private readonly Button _emptySettingsButton;
    private readonly Popup _flyout;
    private readonly Border _flyoutBorder;
    private readonly Border _thermalFlyoutSection;
    private readonly Border _mediaFlyoutSection;
    private readonly SettingsPanel _settingsPanel;
    private readonly Button _settingsButton;
    private readonly MenuItem _runAtStartupMenuItem;

    private bool _loaded;
    private bool _showingSettings;

    public TaskbarContent()
    {
        _settings = App.Settings;
        _thermalModule = new ThermalTaskbarModule(App.ThermalService, _settings);
        _mediaModule = new MediaTaskbarModule(App.MediaService);
        _modules = [_thermalModule, _mediaModule];
        foreach (var module in _modules) module.FlyoutRequested += OnFlyoutRequested;

        _separator = new Border
        {
            Width = 1,
            Margin = new Thickness(4, 7, 4, 7),
            Background = new SolidColorBrush(Color.FromArgb(0x38, 0x80, 0x80, 0x80))
        };

        _emptySettingsButton = TaskbarUi.TransparentButton();
        _emptySettingsButton.Padding = new Thickness(8, 0, 8, 0);
        _emptySettingsButton.Content = TaskbarUi.Symbol("\uE713", 15);
        _emptySettingsButton.ToolTip = "Attaquer Taskbar settings";
        _emptySettingsButton.Click += (_, _) => OpenFlyout(showSettings: true);
        _emptySettingsButton.Visibility = Visibility.Collapsed;

        _layoutRoot = new Grid
        {
            Margin = new Thickness(4, 0, 4, 0),
            Background = Brushes.Transparent
        };
        _layoutRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _layoutRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _layoutRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        TaskbarUi.AddToColumn(_layoutRoot, _thermalModule.TaskbarView, 0);
        TaskbarUi.AddToColumn(_layoutRoot, _separator, 1);
        TaskbarUi.AddToColumn(_layoutRoot, _mediaModule.TaskbarView, 2);
        Grid.SetColumnSpan(_emptySettingsButton, 3);
        _layoutRoot.Children.Add(_emptySettingsButton);

        _settingsButton = TaskbarUi.InlineButton(
            TaskbarUi.Symbol("\uE713", 15),
            "Settings",
            OnSettingsButtonClick,
            30);
        var header = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = TaskbarUi.Text("Attaquer Taskbar", 16);
        heading.FontWeight = FontWeights.SemiBold;
        header.Children.Add(heading);
        Grid.SetColumn(_settingsButton, 1);
        header.Children.Add(_settingsButton);

        _thermalFlyoutSection = new Border { Child = _thermalModule.FlyoutView };
        _mediaFlyoutSection = new Border { Child = _mediaModule.FlyoutView };
        _settingsPanel = new SettingsPanel(_settings) { Visibility = Visibility.Collapsed };
        var flyoutContent = new StackPanel();
        flyoutContent.Children.Add(header);
        flyoutContent.Children.Add(_thermalFlyoutSection);
        flyoutContent.Children.Add(_mediaFlyoutSection);
        flyoutContent.Children.Add(_settingsPanel);
        var scrollViewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 600,
            Content = flyoutContent
        };
        _flyoutBorder = new Border
        {
            Width = 440,
            Padding = new Thickness(14),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 4,
                Opacity = 0.35
            },
            Child = scrollViewer
        };
        _flyout = new Popup
        {
            PlacementTarget = this,
            Placement = PlacementMode.Top,
            VerticalOffset = -6,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
            Child = _flyoutBorder
        };

        var contextMenu = new ContextMenu();
        contextMenu.Opened += OnContextMenuOpened;
        var settingsItem = new MenuItem { Header = "Settings" };
        settingsItem.Click += (_, _) => OpenFlyout(showSettings: true);
        var openFrameworkControl = new MenuItem { Header = "Open Framework Control" };
        openFrameworkControl.Click += (_, _) => FrameworkControlService.OpenDashboard();
        _runAtStartupMenuItem = new MenuItem { Header = "Run at startup", IsCheckable = true };
        _runAtStartupMenuItem.Click += OnRunAtStartupClick;
        var exit = new MenuItem { Header = "Exit" };
        exit.Click += (_, _) => Environment.Exit(0);
        contextMenu.Items.Add(settingsItem);
        contextMenu.Items.Add(openFrameworkControl);
        contextMenu.Items.Add(_runAtStartupMenuItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(exit);
        _layoutRoot.ContextMenu = contextMenu;

        Content = _layoutRoot;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_loaded) return;
        _loaded = true;
        foreach (var module in _modules) module.Start();
        _settings.Changed += OnSettingsChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        ApplySystemTheme();
        ApplyVisibility(_settings.Current);
        ApplyLayout(ActualHeight < CompactHeightThreshold, ActualWidth);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_loaded) return;
        _loaded = false;
        _flyout.IsOpen = false;
        foreach (var module in _modules) module.Stop();
        _settings.Changed -= OnSettingsChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    private void OnFlyoutRequested(object? sender, EventArgs e) => OpenFlyout(showSettings: false);

    private void OpenFlyout(bool showSettings)
    {
        _showingSettings = showSettings;
        _settingsPanel.Visibility = showSettings ? Visibility.Visible : Visibility.Collapsed;
        _settingsPanel.Refresh();
        _settingsButton.Background = showSettings
            ? new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x80, 0x80))
            : Brushes.Transparent;
        ApplySystemTheme();
        _flyout.IsOpen = true;
    }

    private void OnSettingsButtonClick(object sender, RoutedEventArgs e)
    {
        _showingSettings = !_showingSettings;
        _settingsPanel.Visibility = _showingSettings ? Visibility.Visible : Visibility.Collapsed;
        if (_showingSettings) _settingsPanel.Refresh();
        _settingsButton.Background = _showingSettings
            ? new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x80, 0x80))
            : Brushes.Transparent;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyLayout(e.NewSize.Height < CompactHeightThreshold, e.NewSize.Width);

    private void ApplyLayout(bool compact, double width)
    {
        _layoutRoot.Margin = compact ? new Thickness(2, 0, 2, 0) : new Thickness(4, 0, 4, 0);
        _separator.Margin = compact
            ? new Thickness(2, 5, 2, 5)
            : new Thickness(4, 7, 4, 7);
        foreach (var module in _modules) module.ApplyLayout(compact, width);
    }

    private void OnSettingsChanged(TaskbarSettings settings) => ApplyVisibility(settings);

    private void ApplyVisibility(TaskbarSettings settings)
    {
        _thermalModule.TaskbarView.Visibility = settings.ShowThermal ? Visibility.Visible : Visibility.Collapsed;
        _mediaModule.TaskbarView.Visibility = settings.ShowMedia ? Visibility.Visible : Visibility.Collapsed;
        _thermalFlyoutSection.Visibility = settings.ShowThermal ? Visibility.Visible : Visibility.Collapsed;
        _mediaFlyoutSection.Visibility = settings.ShowMedia ? Visibility.Visible : Visibility.Collapsed;
        _separator.Visibility = settings.ShowThermal && settings.ShowMedia
            ? Visibility.Visible
            : Visibility.Collapsed;
        _emptySettingsButton.Visibility = !settings.ShowThermal && !settings.ShowMedia
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) =>
        Dispatcher.BeginInvoke(ApplySystemTheme);

    private void ApplySystemTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var light = key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
        var foreground = light ? Brush(0x20, 0x20, 0x20) : Brushes.White;
        Foreground = foreground;
        _flyoutBorder.Foreground = foreground;
        _flyoutBorder.Background = light ? Brush(0xF7, 0xF7, 0xF7) : Brush(0x24, 0x24, 0x24);
        _flyoutBorder.BorderBrush = light ? Brush(0xD0, 0xD0, 0xD0) : Brush(0x4A, 0x4A, 0x4A);
        _emptySettingsButton.Foreground = foreground;
        _settingsButton.Foreground = foreground;
        foreach (var module in _modules) module.ApplyTheme(foreground);
    }

    private void OnContextMenuOpened(object sender, RoutedEventArgs e) =>
        _runAtStartupMenuItem.IsChecked = StartupService.IsEnabled();

    private void OnRunAtStartupClick(object sender, RoutedEventArgs e) =>
        _runAtStartupMenuItem.IsChecked = StartupService.SetEnabled(_runAtStartupMenuItem.IsChecked);

    private static SolidColorBrush Brush(byte red, byte green, byte blue) =>
        new(Color.FromRgb(red, green, blue));
}
