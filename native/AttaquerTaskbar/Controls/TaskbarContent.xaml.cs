using AttaquerTaskbar.Models;
using AttaquerTaskbar.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace AttaquerTaskbar.Controls;

public sealed partial class TaskbarContent : UserControl
{
    // Windows' compact taskbar is approximately 32 DIPs high; the standard
    // taskbar is 48 DIPs. Leave room for intermediate DPI/preview variants.
    private const double CompactHeightThreshold = 40;

    private static readonly SolidColorBrush CoolBrush = Brush(0x30, 0x8C, 0x4A);
    private static readonly SolidColorBrush WarmBrush = Brush(0xC0, 0x6C, 0x00);
    private static readonly SolidColorBrush HotBrush = Brush(0xD1, 0x34, 0x38);
    private static readonly SolidColorBrush ExtremeBrush = Brush(0xA8, 0x00, 0x00);
    private static readonly SolidColorBrush UnavailableBrush = Brush(0x80, 0x80, 0x80);

    private readonly SystemMediaTransportService _mediaService = App.MediaService;
    private readonly FrameworkControlService _thermalService = App.ThermalService;
    private MediaSnapshot _mediaSnapshot = MediaSnapshot.Empty;
    private bool _isCompact;
    private bool _isLoaded;

    public TaskbarContent() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isLoaded) return;
        _isLoaded = true;

        _mediaService.StateChanged += ApplyMediaSnapshot;
        _thermalService.StateChanged += ApplyThermalSnapshot;
        ApplyMediaSnapshot(_mediaService.CurrentSnapshot);
        ApplyThermalSnapshot(_thermalService.CurrentSnapshot);
        ApplyDensity(ActualHeight < CompactHeightThreshold);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded) return;
        _isLoaded = false;
        _mediaService.StateChanged -= ApplyMediaSnapshot;
        _thermalService.StateChanged -= ApplyThermalSnapshot;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyDensity(e.NewSize.Height < CompactHeightThreshold);

    private void ApplyDensity(bool compact)
    {
        _isCompact = compact;

        LayoutRoot.Padding = compact
            ? new Thickness(2, 0, 2, 0)
            : new Thickness(4, 0, 4, 0);
        LayoutRoot.ColumnSpacing = compact ? 2 : 4;
        MediaGrid.ColumnSpacing = compact ? 3 : 5;
        MediaSeparator.Margin = compact
            ? new Thickness(0, 5, 0, 5)
            : new Thickness(0, 7, 0, 7);
        ThermalButton.Padding = compact
            ? new Thickness(2, 0, 2, 0)
            : new Thickness(4, 0, 4, 0);
        ThermalPanel.Spacing = compact ? 3 : 5;

        CpuLabel.FontSize = FanLabel.FontSize = compact ? 8 : 9;
        TemperatureText.FontSize = FanText.FontSize = compact ? 10 : 11;

        var artworkSize = compact ? 24 : 32;
        ArtworkHost.Width = ArtworkHost.Height = artworkSize;
        ArtworkHost.CornerRadius = new CornerRadius(compact ? 3 : 4);
        ArtworkPlaceholder.FontSize = compact ? 13 : 16;

        NormalMetadata.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactMetadata.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;

        var buttonSize = compact ? 24 : 28;
        SetButtonDensity(PreviousButton, PreviousIcon, buttonSize, compact ? 13 : 15);
        SetButtonDensity(PlayPauseButton, PlayIcon, buttonSize, compact ? 13 : 15);
        PauseIcon.FontSize = compact ? 13 : 15;
        SetButtonDensity(NextButton, NextIcon, buttonSize, compact ? 13 : 15);

        ApplyMediaSnapshot(_mediaSnapshot);
    }

    private static void SetButtonDensity(
        Button button,
        FontIcon icon,
        double buttonSize,
        double iconSize)
    {
        button.Width = button.Height = buttonSize;
        button.CornerRadius = new CornerRadius(buttonSize <= 24 ? 3 : 4);
        icon.FontSize = iconSize;
    }

    private void ApplyMediaSnapshot(MediaSnapshot snapshot)
    {
        _mediaSnapshot = snapshot;

        if (!snapshot.HasSession)
        {
            ArtworkHost.Visibility = Visibility.Collapsed;
            TitleText.Text = CompactMetadata.Text = "Nothing playing";
            DescriptionText.Text = string.Empty;
            DescriptionText.Visibility = Visibility.Collapsed;
        }
        else
        {
            ArtworkHost.Visibility = Visibility.Visible;
            TitleText.Text = string.IsNullOrWhiteSpace(snapshot.Title)
                ? "Unknown title"
                : snapshot.Title;

            var description = BuildDescription(snapshot.Artist, snapshot.Album);
            DescriptionText.Text = description;
            DescriptionText.Visibility = string.IsNullOrWhiteSpace(description)
                ? Visibility.Collapsed
                : Visibility.Visible;
            CompactMetadata.Text = BuildCompactText(snapshot.Title, snapshot.Artist);
        }

        ArtworkImage.Source = snapshot.Thumbnail;
        ArtworkImage.Visibility = snapshot.HasSession && snapshot.Thumbnail is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
        ArtworkPlaceholder.Visibility = ArtworkImage.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;

        PreviousButton.IsEnabled = snapshot.CanSkipPrevious;
        NextButton.IsEnabled = snapshot.CanSkipNext;
        PlayPauseButton.IsEnabled = snapshot.CanPlayPause;
        PlayIcon.Visibility = snapshot.IsPlaying ? Visibility.Collapsed : Visibility.Visible;
        PauseIcon.Visibility = snapshot.IsPlaying ? Visibility.Visible : Visibility.Collapsed;

        // In a very narrow allocated slot, retain telemetry and the primary
        // transport action before spending width on secondary controls.
        var hideSecondaryControls = ActualWidth > 0 && ActualWidth < (_isCompact ? 330 : 380);
        PreviousButton.Visibility = hideSecondaryControls ? Visibility.Collapsed : Visibility.Visible;
        NextButton.Visibility = hideSecondaryControls ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ApplyThermalSnapshot(ThermalSnapshot snapshot)
    {
        if (!snapshot.IsAvailable)
        {
            TemperatureText.Text = "--°";
            FanText.Text = "--";
            TemperatureText.Foreground = FanText.Foreground = UnavailableBrush;
            ToolTipService.SetToolTip(
                ThermalButton,
                "Framework Control is unavailable at 127.0.0.1:30912");
            return;
        }

        TemperatureText.Text = snapshot.TemperatureCelsius is double temperature
            ? $"{Math.Round(temperature):0}°"
            : "--°";
        TemperatureText.Foreground = snapshot.TemperatureCelsius is double temp
            ? TemperatureBrush(temp)
            : UnavailableBrush;

        FanText.Text = snapshot.FanPercent is int percent
            ? $"{percent}%"
            : snapshot.FanRpm is int rpm
                ? $"{rpm} rpm"
                : "--";
        FanText.Foreground = snapshot.FanPercent is int fanPercent
            ? FanBrush(fanPercent)
            : snapshot.FanRpm is not null
                ? CoolBrush
                : UnavailableBrush;

        var fanDetail = snapshot.FanPercent is int fanDuty && snapshot.FanRpm is int fanRpm
            ? $"Fan: {fanDuty}% ({fanRpm} RPM)"
            : snapshot.FanRpm is int rawRpm
                ? $"Fan: {rawRpm} RPM (run Framework Control calibration for %)"
                : "Fan: unavailable";
        ToolTipService.SetToolTip(
            ThermalButton,
            $"CPU: {TemperatureText.Text}\n{fanDetail}\nClick to open Framework Control");
    }

    private async void OnPreviousClick(object sender, RoutedEventArgs e) =>
        await _mediaService.SkipPreviousAsync();

    private async void OnPlayPauseClick(object sender, RoutedEventArgs e) =>
        await _mediaService.TogglePlayPauseAsync();

    private async void OnNextClick(object sender, RoutedEventArgs e) =>
        await _mediaService.SkipNextAsync();

    private void OnOpenFrameworkControlClick(object sender, RoutedEventArgs e) =>
        FrameworkControlService.OpenDashboard();

    private void OnContextMenuOpened(object sender, object e) =>
        RunAtStartupMenuItem.IsChecked = StartupService.IsEnabled();

    private void OnRunAtStartupClick(object sender, RoutedEventArgs e) =>
        RunAtStartupMenuItem.IsChecked = StartupService.SetEnabled(RunAtStartupMenuItem.IsChecked);

    private static void OnExitClick(object sender, RoutedEventArgs e) => Environment.Exit(0);

    private static string BuildDescription(string artist, string album)
    {
        if (!string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(album))
            return $"{artist} · {album}";
        if (!string.IsNullOrWhiteSpace(artist)) return artist;
        return album;
    }

    private static string BuildCompactText(string title, string artist)
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(artist))
            return "Unknown media";
        if (string.IsNullOrWhiteSpace(artist)) return title;
        if (string.IsNullOrWhiteSpace(title)) return artist;
        return $"{title} · {artist}";
    }

    private static SolidColorBrush TemperatureBrush(double temperature) =>
        temperature switch
        {
            < 60 => CoolBrush,
            < 75 => WarmBrush,
            < 90 => HotBrush,
            _ => ExtremeBrush
        };

    private static SolidColorBrush FanBrush(int percent) =>
        percent switch
        {
            < 35 => CoolBrush,
            < 55 => WarmBrush,
            < 80 => HotBrush,
            _ => ExtremeBrush
        };

    private static SolidColorBrush Brush(byte red, byte green, byte blue) =>
        new(Color.FromArgb(0xFF, red, green, blue));
}
