using System.Windows;
using System.Windows.Controls;
using AttaquerTaskbar.Models;
using AttaquerTaskbar.Services;

namespace AttaquerTaskbar.Controls;

internal sealed class SettingsPanel : StackPanel
{
    private readonly SettingsService _settings;
    private readonly ComboBox _labelMode;
    private readonly ComboBox _valueMode;
    private readonly CheckBox _showThermal;
    private readonly CheckBox _showMedia;
    private readonly CheckBox _runAtStartup;
    private bool _refreshing;

    public SettingsPanel(SettingsService settings)
    {
        _settings = settings;
        Margin = new Thickness(0, 12, 0, 0);

        var separator = new Border
        {
            Height = 1,
            Margin = new Thickness(0, 0, 0, 12),
            Opacity = 0.25
        };
        separator.SetResourceReference(BackgroundProperty, SystemColors.ControlTextBrushKey);
        Children.Add(separator);

        var title = TaskbarUi.Text("Settings", 14);
        title.FontWeight = FontWeights.SemiBold;
        title.Margin = new Thickness(0, 0, 0, 8);
        Children.Add(title);

        _labelMode = CreateComboBox(
            ("Auto", MetricLabelMode.Auto),
            ("Icons", MetricLabelMode.Icons),
            ("Text", MetricLabelMode.Text));
        _labelMode.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _labelMode.SelectedValue is MetricLabelMode mode)
                _settings.Update(value => value.MetricLabels = mode);
        };
        Children.Add(SettingRow(
            "Metric labels",
            "Auto uses icons on the small taskbar and text on the standard taskbar.",
            _labelMode));

        _valueMode = CreateComboBox(
            ("Numbers", MetricValueMode.Numbers),
            ("Sparklines", MetricValueMode.Sparklines));
        _valueMode.SelectionChanged += (_, _) =>
        {
            if (!_refreshing && _valueMode.SelectedValue is MetricValueMode mode)
                _settings.Update(value => value.MetricValues = mode);
        };
        Children.Add(SettingRow(
            "Thermal values",
            "Sparklines show the last two minutes in the taskbar; current values remain in this flyout.",
            _valueMode));

        var modulesTitle = TaskbarUi.Text("Built-in modules", 11);
        modulesTitle.FontWeight = FontWeights.SemiBold;
        modulesTitle.Margin = new Thickness(0, 10, 0, 4);
        Children.Add(modulesTitle);

        _showThermal = new CheckBox { Content = "Thermal", Margin = new Thickness(0, 3, 0, 3) };
        _showThermal.Checked += (_, _) => SetModuleVisibility(thermal: true);
        _showThermal.Unchecked += (_, _) => SetModuleVisibility(thermal: false);
        Children.Add(_showThermal);

        _showMedia = new CheckBox { Content = "Now playing", Margin = new Thickness(0, 3, 0, 3) };
        _showMedia.Checked += (_, _) => SetModuleVisibility(media: true);
        _showMedia.Unchecked += (_, _) => SetModuleVisibility(media: false);
        Children.Add(_showMedia);

        _runAtStartup = new CheckBox { Content = "Run at startup", Margin = new Thickness(0, 9, 0, 0) };
        _runAtStartup.Checked += (_, _) => SetStartup(true);
        _runAtStartup.Unchecked += (_, _) => SetStartup(false);
        Children.Add(_runAtStartup);

        var note = TaskbarUi.Text(
            "Thermal and Media use the same internal module contract, so more built-ins can be added without changing the taskbar host.",
            10,
            trim: false);
        note.TextWrapping = TextWrapping.Wrap;
        note.Opacity = 0.6;
        note.Margin = new Thickness(0, 12, 0, 0);
        Children.Add(note);
        Refresh();
    }

    public void Refresh()
    {
        _refreshing = true;
        try
        {
            _labelMode.SelectedValue = _settings.Current.MetricLabels;
            _valueMode.SelectedValue = _settings.Current.MetricValues;
            _showThermal.IsChecked = _settings.Current.ShowThermal;
            _showMedia.IsChecked = _settings.Current.ShowMedia;
            _runAtStartup.IsChecked = StartupService.IsEnabled();
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void SetModuleVisibility(bool? thermal = null, bool? media = null)
    {
        if (_refreshing) return;
        _settings.Update(settings =>
        {
            if (thermal is not null) settings.ShowThermal = thermal.Value;
            if (media is not null) settings.ShowMedia = media.Value;
        });
    }

    private void SetStartup(bool enabled)
    {
        if (_refreshing) return;
        _refreshing = true;
        _runAtStartup.IsChecked = StartupService.SetEnabled(enabled);
        _refreshing = false;
    }

    private static ComboBox CreateComboBox<T>(params (string Label, T Value)[] options)
        where T : struct, Enum
    {
        var comboBox = new ComboBox
        {
            Width = 120,
            HorizontalAlignment = HorizontalAlignment.Right,
            SelectedValuePath = "Tag"
        };
        foreach (var option in options)
            comboBox.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Value });
        return comboBox;
    }

    private static Grid SettingRow(string label, string description, FrameworkElement control)
    {
        var text = new StackPanel { Margin = new Thickness(0, 3, 12, 3) };
        text.Children.Add(TaskbarUi.Text(label, 11));
        var detail = TaskbarUi.Text(description, 9);
        detail.TextWrapping = TextWrapping.Wrap;
        detail.Opacity = 0.58;
        text.Children.Add(detail);

        var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(text);
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }
}
