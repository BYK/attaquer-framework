using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AttaquerTaskbar.Models;
using AttaquerTaskbar.Services;

namespace AttaquerTaskbar.Controls.Modules;

internal sealed class ThermalTaskbarModule : ITaskbarModule
{
    private readonly SolidColorBrush _coolBrush = Brush(0x30, 0x8C, 0x4A);
    private readonly SolidColorBrush _warmBrush = Brush(0xC0, 0x6C, 0x00);
    private readonly SolidColorBrush _hotBrush = Brush(0xD1, 0x34, 0x38);
    private readonly SolidColorBrush _extremeBrush = Brush(0xA8, 0x00, 0x00);
    private readonly SolidColorBrush _unavailableBrush = Brush(0x80, 0x80, 0x80);
    private readonly FrameworkControlService _service;
    private readonly SettingsService _settings;
    private readonly Button _taskbarButton;
    private readonly StackPanel _compactPanel;
    private readonly MetricVisual _cpuMetric;
    private readonly MetricVisual _fanMetric;
    private readonly Sparkline _expandedCpuSparkline;
    private readonly Sparkline _expandedFanSparkline;
    private readonly TextBlock _expandedTemperature;
    private readonly TextBlock _expandedFan;
    private readonly TextBlock _expandedFanDetail;

    private ThermalSnapshot _snapshot = ThermalSnapshot.Unavailable;
    private Brush _foreground = Brushes.White;
    private bool _compact;
    private bool _started;

    public ThermalTaskbarModule(FrameworkControlService service, SettingsService settings)
    {
        _service = service;
        _settings = settings;

        _cpuMetric = CreateMetric(
            "CPU",
            TaskbarUi.ThermometerIcon(13),
            new Sparkline(60, 25, 105));
        _fanMetric = CreateMetric(
            "FAN",
            TaskbarUi.FanIcon(13),
            new Sparkline(60, 0, 100));
        _fanMetric.Root.Margin = new Thickness(5, 0, 0, 0);

        _compactPanel = TaskbarUi.HorizontalPanel();
        _compactPanel.Children.Add(_cpuMetric.Root);
        _compactPanel.Children.Add(_fanMetric.Root);
        _taskbarButton = TaskbarUi.TransparentButton();
        _taskbarButton.Padding = new Thickness(4, 0, 4, 0);
        _taskbarButton.Content = _compactPanel;
        _taskbarButton.Click += (_, _) => FlyoutRequested?.Invoke(this, EventArgs.Empty);
        TaskbarView = _taskbarButton;

        _expandedTemperature = ValueText("--°", 22);
        _expandedFan = ValueText("--", 22);
        _expandedFanDetail = TaskbarUi.Text("Waiting for Framework Control", 10);
        _expandedFanDetail.Opacity = 0.65;
        _expandedCpuSparkline = new Sparkline(60, 25, 105) { Width = 150, Height = 34 };
        _expandedFanSparkline = new Sparkline(60, 0, 100) { Width = 150, Height = 34 };

        var flyout = new Grid();
        flyout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        flyout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = TaskbarUi.Text("Thermals", 14);
        title.FontWeight = FontWeights.SemiBold;
        heading.Children.Add(title);
        var openButton = TaskbarUi.TransparentButton();
        openButton.Padding = new Thickness(8, 4, 8, 4);
        openButton.Content = TaskbarUi.Text("Open Framework Control", 11);
        openButton.Click += (_, _) => FrameworkControlService.OpenDashboard();
        Grid.SetColumn(openButton, 1);
        heading.Children.Add(openButton);
        flyout.Children.Add(heading);

        var cards = new Grid();
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var cpuCard = CreateCard(
            "CPU temperature",
            TaskbarUi.ThermometerIcon(16),
            _expandedTemperature,
            _expandedCpuSparkline,
            null);
        cpuCard.Margin = new Thickness(0, 0, 4, 0);
        cards.Children.Add(cpuCard);
        var fanCard = CreateCard(
            "Fan",
            TaskbarUi.FanIcon(16),
            _expandedFan,
            _expandedFanSparkline,
            _expandedFanDetail);
        fanCard.Margin = new Thickness(4, 0, 0, 0);
        Grid.SetColumn(fanCard, 1);
        cards.Children.Add(fanCard);
        Grid.SetRow(cards, 1);
        flyout.Children.Add(cards);
        FlyoutView = flyout;
    }

    public string Id => "thermal";

    public FrameworkElement TaskbarView { get; }

    public FrameworkElement FlyoutView { get; }

    public event EventHandler? FlyoutRequested;

    public void Start()
    {
        if (_started) return;
        _started = true;
        _service.StateChanged += ApplySnapshot;
        _settings.Changed += OnSettingsChanged;
        ApplySnapshot(_service.CurrentSnapshot);
        ApplyDisplayMode();
    }

    public void Stop()
    {
        if (!_started) return;
        _started = false;
        _service.StateChanged -= ApplySnapshot;
        _settings.Changed -= OnSettingsChanged;
    }

    public void ApplyLayout(bool compact, double availableWidth)
    {
        _compact = compact;
        _taskbarButton.Padding = compact ? new Thickness(2, 0, 2, 0) : new Thickness(4, 0, 4, 0);
        _fanMetric.Root.Margin = new Thickness(compact ? 3 : 5, 0, 0, 0);
        foreach (var metric in new[] { _cpuMetric, _fanMetric })
        {
            metric.Label.FontSize = compact ? 8 : 9;
            metric.Value.FontSize = compact ? 10 : 11;
            metric.Icon.Width = metric.Icon.Height = compact ? 12 : 13;
            metric.Sparkline.Width = compact ? 34 : 44;
            metric.Sparkline.Height = compact ? 13 : 16;
        }

        ApplyDisplayMode();
    }

    public void ApplyTheme(Brush foreground)
    {
        _foreground = foreground;
        _taskbarButton.Foreground = foreground;
        TaskbarUi.SetIconBrush(_cpuMetric.Icon, foreground);
        TaskbarUi.SetIconBrush(_fanMetric.Icon, foreground);
    }

    private void OnSettingsChanged(TaskbarSettings settings) => ApplyDisplayMode();

    private void ApplyDisplayMode()
    {
        var settings = _settings.Current;
        var showIcons = settings.MetricLabels == MetricLabelMode.Icons ||
            settings.MetricLabels == MetricLabelMode.Auto && _compact;
        var showSparklines = settings.MetricValues == MetricValueMode.Sparklines;

        foreach (var metric in new[] { _cpuMetric, _fanMetric })
        {
            metric.Icon.Visibility = showIcons ? Visibility.Visible : Visibility.Collapsed;
            metric.Label.Visibility = showIcons ? Visibility.Collapsed : Visibility.Visible;
            metric.Value.Visibility = showSparklines ? Visibility.Collapsed : Visibility.Visible;
            metric.Sparkline.Visibility = showSparklines ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void ApplySnapshot(ThermalSnapshot snapshot)
    {
        _snapshot = snapshot;
        _cpuMetric.Sparkline.Add(snapshot.TemperatureCelsius);
        _fanMetric.Sparkline.Add(snapshot.FanPercent);
        _expandedCpuSparkline.Add(snapshot.TemperatureCelsius);
        _expandedFanSparkline.Add(snapshot.FanPercent);

        if (!snapshot.IsAvailable)
        {
            _cpuMetric.Value.Text = _expandedTemperature.Text = "--°";
            _fanMetric.Value.Text = _expandedFan.Text = "--";
            _expandedFanDetail.Text = "Framework Control unavailable";
            SetMetricBrush(_cpuMetric, _expandedCpuSparkline, _expandedTemperature, _unavailableBrush);
            SetMetricBrush(_fanMetric, _expandedFanSparkline, _expandedFan, _unavailableBrush);
            _taskbarButton.ToolTip = "Framework Control is unavailable at 127.0.0.1:30912";
            return;
        }

        _cpuMetric.Value.Text = _expandedTemperature.Text = snapshot.TemperatureCelsius is double temperature
            ? $"{Math.Round(temperature):0}°"
            : "--°";
        var temperatureBrush = snapshot.TemperatureCelsius is double temp
            ? TemperatureBrush(temp)
            : _unavailableBrush;
        SetMetricBrush(_cpuMetric, _expandedCpuSparkline, _expandedTemperature, temperatureBrush);

        _fanMetric.Value.Text = _expandedFan.Text = snapshot.FanPercent is int percent
            ? $"{percent}%"
            : snapshot.FanRpm is int rpm ? $"{rpm} rpm" : "--";
        var fanBrush = snapshot.FanPercent is int fanPercent
            ? FanBrush(fanPercent)
            : snapshot.FanRpm is not null ? _coolBrush : _unavailableBrush;
        SetMetricBrush(_fanMetric, _expandedFanSparkline, _expandedFan, fanBrush);

        var fanDetail = snapshot.FanPercent is int fanDuty && snapshot.FanRpm is int fanRpm
            ? $"{fanRpm:N0} RPM · {fanDuty}% duty"
            : snapshot.FanRpm is int rawRpm
                ? $"{rawRpm:N0} RPM · calibrate for %"
                : "Fan unavailable";
        _expandedFanDetail.Text = fanDetail;
        _taskbarButton.ToolTip =
            $"CPU: {_cpuMetric.Value.Text}\nFan: {fanDetail}\nClick for details";
    }

    private static MetricVisual CreateMetric(string label, FrameworkElement icon, Sparkline sparkline)
    {
        var labelText = TaskbarUi.Text(label, 9);
        labelText.Opacity = 0.65;
        var value = ValueText("--", 11);
        value.Margin = new Thickness(2, 0, 0, 0);
        icon.Margin = new Thickness(0, 0, 4, 0);
        sparkline.Width = 44;
        sparkline.Height = 16;
        sparkline.Margin = new Thickness(3, 0, 0, 0);
        sparkline.Visibility = Visibility.Collapsed;

        var root = TaskbarUi.HorizontalPanel();
        root.Children.Add(icon);
        root.Children.Add(labelText);
        root.Children.Add(value);
        root.Children.Add(sparkline);
        return new MetricVisual(root, labelText, icon, value, sparkline);
    }

    private static Border CreateCard(
        string label,
        FrameworkElement icon,
        TextBlock value,
        Sparkline sparkline,
        TextBlock? detail)
    {
        icon.Margin = new Thickness(0, 0, 6, 0);
        var heading = TaskbarUi.HorizontalPanel();
        heading.Children.Add(icon);
        var labelText = TaskbarUi.Text(label, 11);
        labelText.Opacity = 0.7;
        heading.Children.Add(labelText);

        var valueRow = new Grid { Margin = new Thickness(0, 5, 0, 0) };
        valueRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        valueRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        valueRow.Children.Add(value);
        sparkline.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(sparkline, 1);
        valueRow.Children.Add(sparkline);

        var content = new StackPanel();
        content.Children.Add(heading);
        content.Children.Add(valueRow);
        if (detail is not null)
        {
            detail.Margin = new Thickness(0, 3, 0, 0);
            content.Children.Add(detail);
        }

        return new Border
        {
            Padding = new Thickness(10),
            Background = new SolidColorBrush(Color.FromArgb(0x16, 0x80, 0x80, 0x80)),
            CornerRadius = new CornerRadius(7),
            Child = content
        };
    }

    private static TextBlock ValueText(string text, double size) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = FontWeights.SemiBold,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static void SetMetricBrush(
        MetricVisual metric,
        Sparkline expandedSparkline,
        TextBlock expandedValue,
        Brush brush)
    {
        metric.Value.Foreground = brush;
        metric.Sparkline.LineBrush = brush;
        metric.Sparkline.InvalidateVisual();
        expandedValue.Foreground = brush;
        expandedSparkline.LineBrush = brush;
        expandedSparkline.InvalidateVisual();
    }

    private Brush TemperatureBrush(double temperature) => temperature switch
    {
        < 60 => _coolBrush,
        < 75 => _warmBrush,
        < 90 => _hotBrush,
        _ => _extremeBrush
    };

    private Brush FanBrush(int percent) => percent switch
    {
        < 35 => _coolBrush,
        < 55 => _warmBrush,
        < 80 => _hotBrush,
        _ => _extremeBrush
    };

    private static SolidColorBrush Brush(byte red, byte green, byte blue) =>
        new(Color.FromRgb(red, green, blue));

    private sealed record MetricVisual(
        StackPanel Root,
        TextBlock Label,
        FrameworkElement Icon,
        TextBlock Value,
        Sparkline Sparkline);
}
