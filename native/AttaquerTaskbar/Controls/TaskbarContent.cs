using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AttaquerTaskbar.Models;
using AttaquerTaskbar.Services;
using Microsoft.Win32;

namespace AttaquerTaskbar.Controls;

public sealed class TaskbarContent : UserControl
{
    private const double CompactHeightThreshold = 40;
    private static readonly FontFamily SymbolFont = new("Segoe Fluent Icons");

    private readonly SolidColorBrush _coolBrush = Brush(0x30, 0x8C, 0x4A);
    private readonly SolidColorBrush _warmBrush = Brush(0xC0, 0x6C, 0x00);
    private readonly SolidColorBrush _hotBrush = Brush(0xD1, 0x34, 0x38);
    private readonly SolidColorBrush _extremeBrush = Brush(0xA8, 0x00, 0x00);
    private readonly SolidColorBrush _unavailableBrush = Brush(0x80, 0x80, 0x80);
    private readonly SystemMediaTransportService _mediaService;
    private readonly FrameworkControlService _thermalService;

    private readonly Grid _layoutRoot;
    private readonly Button _thermalButton;
    private readonly StackPanel _thermalPanel;
    private readonly StackPanel _fanPair;
    private readonly TextBlock _cpuLabel;
    private readonly TextBlock _temperatureText;
    private readonly TextBlock _fanLabel;
    private readonly TextBlock _fanText;
    private readonly Border _mediaSeparator;
    private readonly Grid _mediaGrid;
    private readonly Border _artworkHost;
    private readonly TextBlock _artworkPlaceholder;
    private readonly Image _artworkImage;
    private readonly StackPanel _normalMetadata;
    private readonly TextBlock _titleText;
    private readonly TextBlock _descriptionText;
    private readonly TextBlock _compactMetadata;
    private readonly Button _previousButton;
    private readonly TextBlock _previousIcon;
    private readonly Button _playPauseButton;
    private readonly TextBlock _playPauseIcon;
    private readonly Button _nextButton;
    private readonly TextBlock _nextIcon;
    private readonly MenuItem _runAtStartupMenuItem;

    private MediaSnapshot _mediaSnapshot = MediaSnapshot.Empty;
    private bool _isCompact;
    private bool _isLoaded;

    public TaskbarContent()
    {
        _mediaService = App.MediaService;
        _thermalService = App.ThermalService;

        _cpuLabel = SecondaryText("CPU", 9);
        _temperatureText = StatusText("--°");
        _fanLabel = SecondaryText("FAN", 9);
        _fanText = StatusText("--");
        _thermalPanel = HorizontalPanel();
        _thermalPanel.Children.Add(Pair(_cpuLabel, _temperatureText));
        _fanPair = Pair(_fanLabel, _fanText);
        _fanPair.Margin = new Thickness(5, 0, 0, 0);
        _thermalPanel.Children.Add(_fanPair);
        _thermalButton = TransparentButton();
        _thermalButton.Padding = new Thickness(4, 0, 4, 0);
        _thermalButton.Content = _thermalPanel;
        _thermalButton.Click += OnOpenFrameworkControlClick;

        _artworkPlaceholder = Symbol("\uE93C", 16);
        _artworkPlaceholder.Opacity = 0.65;
        _artworkImage = new Image
        {
            Stretch = Stretch.UniformToFill,
            Visibility = Visibility.Collapsed,
            SnapsToDevicePixels = true
        };
        var artworkGrid = new Grid();
        artworkGrid.Children.Add(_artworkPlaceholder);
        artworkGrid.Children.Add(_artworkImage);
        _artworkHost = new Border
        {
            Width = 32,
            Height = 32,
            Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromArgb(0x18, 0x80, 0x80, 0x80)),
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            Child = artworkGrid
        };

        _titleText = MetadataText("Nothing playing", 12);
        _descriptionText = MetadataText(string.Empty, 10);
        _descriptionText.Opacity = 0.65;
        _normalMetadata = new StackPanel();
        _normalMetadata.Children.Add(_titleText);
        _normalMetadata.Children.Add(_descriptionText);
        _compactMetadata = MetadataText("Nothing playing", 11);
        _compactMetadata.VerticalAlignment = VerticalAlignment.Center;
        _compactMetadata.Visibility = Visibility.Collapsed;
        var metadataGrid = new Grid { MinWidth = 20, VerticalAlignment = VerticalAlignment.Center };
        metadataGrid.Children.Add(_normalMetadata);
        metadataGrid.Children.Add(_compactMetadata);

        _previousIcon = Symbol("\uE892", 15);
        _previousButton = InlineButton(_previousIcon, "Previous", OnPreviousClick);
        _playPauseIcon = Symbol("\uE768", 15);
        _playPauseButton = InlineButton(_playPauseIcon, "Play or pause", OnPlayPauseClick);
        _nextIcon = Symbol("\uE893", 15);
        _nextButton = InlineButton(_nextIcon, "Next", OnNextClick);
        var transportPanel = HorizontalPanel();
        transportPanel.Margin = new Thickness(5, 0, 0, 0);
        transportPanel.Children.Add(_previousButton);
        _playPauseButton.Margin = new Thickness(1, 0, 1, 0);
        transportPanel.Children.Add(_playPauseButton);
        transportPanel.Children.Add(_nextButton);

        _mediaGrid = new Grid();
        _mediaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _mediaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _mediaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        AddToColumn(_mediaGrid, _artworkHost, 0);
        AddToColumn(_mediaGrid, metadataGrid, 1);
        AddToColumn(_mediaGrid, transportPanel, 2);

        _mediaSeparator = new Border
        {
            Width = 1,
            Margin = new Thickness(4, 7, 4, 7),
            Background = new SolidColorBrush(Color.FromArgb(0x38, 0x80, 0x80, 0x80))
        };

        _layoutRoot = new Grid
        {
            Margin = new Thickness(4, 0, 4, 0),
            Background = Brushes.Transparent
        };
        _layoutRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _layoutRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _layoutRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddToColumn(_layoutRoot, _thermalButton, 0);
        AddToColumn(_layoutRoot, _mediaSeparator, 1);
        AddToColumn(_layoutRoot, _mediaGrid, 2);

        var contextMenu = new ContextMenu();
        contextMenu.Opened += OnContextMenuOpened;
        var openFrameworkControl = new MenuItem { Header = "Open Framework Control" };
        openFrameworkControl.Click += OnOpenFrameworkControlClick;
        _runAtStartupMenuItem = new MenuItem { Header = "Run at startup", IsCheckable = true };
        _runAtStartupMenuItem.Click += OnRunAtStartupClick;
        var exit = new MenuItem { Header = "Exit" };
        exit.Click += OnExitClick;
        contextMenu.Items.Add(openFrameworkControl);
        contextMenu.Items.Add(_runAtStartupMenuItem);
        contextMenu.Items.Add(new Separator());
        contextMenu.Items.Add(exit);
        _layoutRoot.ContextMenu = contextMenu;

        Content = _layoutRoot;
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isLoaded) return;
        _isLoaded = true;

        _mediaService.StateChanged += ApplyMediaSnapshot;
        _thermalService.StateChanged += ApplyThermalSnapshot;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        ApplySystemTheme();
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
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) =>
        Dispatcher.BeginInvoke(ApplySystemTheme);

    private void ApplySystemTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        var light = key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
        Foreground = light ? Brush(0x20, 0x20, 0x20) : Brushes.White;
        _thermalButton.Foreground = Foreground;
        _previousButton.Foreground = Foreground;
        _playPauseButton.Foreground = Foreground;
        _nextButton.Foreground = Foreground;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyDensity(e.NewSize.Height < CompactHeightThreshold);

    private void ApplyDensity(bool compact)
    {
        _isCompact = compact;
        _layoutRoot.Margin = compact ? new Thickness(2, 0, 2, 0) : new Thickness(4, 0, 4, 0);
        _mediaSeparator.Margin = compact
            ? new Thickness(2, 5, 2, 5)
            : new Thickness(4, 7, 4, 7);
        _thermalButton.Padding = compact ? new Thickness(2, 0, 2, 0) : new Thickness(4, 0, 4, 0);
        _fanPair.Margin = new Thickness(compact ? 3 : 5, 0, 0, 0);
        _cpuLabel.FontSize = _fanLabel.FontSize = compact ? 8 : 9;
        _temperatureText.FontSize = _fanText.FontSize = compact ? 10 : 11;

        var artworkSize = compact ? 24 : 32;
        _artworkHost.Width = _artworkHost.Height = artworkSize;
        _artworkHost.Margin = new Thickness(0, 0, compact ? 3 : 5, 0);
        _artworkHost.CornerRadius = new CornerRadius(compact ? 3 : 4);
        _artworkPlaceholder.FontSize = compact ? 13 : 16;
        _normalMetadata.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        _compactMetadata.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;

        var buttonSize = compact ? 24 : 28;
        SetButtonDensity(_previousButton, _previousIcon, buttonSize, compact ? 13 : 15);
        SetButtonDensity(_playPauseButton, _playPauseIcon, buttonSize, compact ? 13 : 15);
        SetButtonDensity(_nextButton, _nextIcon, buttonSize, compact ? 13 : 15);
        ApplyMediaSnapshot(_mediaSnapshot);
    }

    private static void SetButtonDensity(Button button, TextBlock icon, double size, double iconSize)
    {
        button.Width = button.Height = size;
        icon.FontSize = iconSize;
    }

    private void ApplyMediaSnapshot(MediaSnapshot snapshot)
    {
        _mediaSnapshot = snapshot;
        if (!snapshot.HasSession)
        {
            _artworkHost.Visibility = Visibility.Collapsed;
            _titleText.Text = _compactMetadata.Text = "Nothing playing";
            _descriptionText.Text = string.Empty;
            _descriptionText.Visibility = Visibility.Collapsed;
        }
        else
        {
            _artworkHost.Visibility = Visibility.Visible;
            _titleText.Text = string.IsNullOrWhiteSpace(snapshot.Title) ? "Unknown title" : snapshot.Title;
            var description = BuildDescription(snapshot.Artist, snapshot.Album);
            _descriptionText.Text = description;
            _descriptionText.Visibility = string.IsNullOrWhiteSpace(description)
                ? Visibility.Collapsed
                : Visibility.Visible;
            _compactMetadata.Text = BuildCompactText(snapshot.Title, snapshot.Artist);
        }

        _artworkImage.Source = CreateBitmap(snapshot.Thumbnail);
        _artworkImage.Visibility = snapshot.HasSession && snapshot.Thumbnail is not null
            ? Visibility.Visible
            : Visibility.Collapsed;
        _artworkPlaceholder.Visibility = _artworkImage.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
        _previousButton.IsEnabled = snapshot.CanSkipPrevious;
        _nextButton.IsEnabled = snapshot.CanSkipNext;
        _playPauseButton.IsEnabled = snapshot.CanPlayPause;
        _playPauseIcon.Text = snapshot.IsPlaying ? "\uE769" : "\uE768";

        var hideSecondaryControls = ActualWidth > 0 && ActualWidth < (_isCompact ? 330 : 380);
        _previousButton.Visibility = hideSecondaryControls ? Visibility.Collapsed : Visibility.Visible;
        _nextButton.Visibility = hideSecondaryControls ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ApplyThermalSnapshot(ThermalSnapshot snapshot)
    {
        if (!snapshot.IsAvailable)
        {
            _temperatureText.Text = "--°";
            _fanText.Text = "--";
            _temperatureText.Foreground = _fanText.Foreground = _unavailableBrush;
            _thermalButton.ToolTip = "Framework Control is unavailable at 127.0.0.1:30912";
            return;
        }

        _temperatureText.Text = snapshot.TemperatureCelsius is double temperature
            ? $"{Math.Round(temperature):0}°"
            : "--°";
        _temperatureText.Foreground = snapshot.TemperatureCelsius is double temp
            ? TemperatureBrush(temp)
            : _unavailableBrush;
        _fanText.Text = snapshot.FanPercent is int percent
            ? $"{percent}%"
            : snapshot.FanRpm is int rpm ? $"{rpm} rpm" : "--";
        _fanText.Foreground = snapshot.FanPercent is int fanPercent
            ? FanBrush(fanPercent)
            : snapshot.FanRpm is not null ? _coolBrush : _unavailableBrush;

        var fanDetail = snapshot.FanPercent is int fanDuty && snapshot.FanRpm is int fanRpm
            ? $"Fan: {fanDuty}% ({fanRpm} RPM)"
            : snapshot.FanRpm is int rawRpm
                ? $"Fan: {rawRpm} RPM (run Framework Control calibration for %)"
                : "Fan: unavailable";
        _thermalButton.ToolTip =
            $"CPU: {_temperatureText.Text}\n{fanDetail}\nClick to open Framework Control";
    }

    private async void OnPreviousClick(object sender, RoutedEventArgs e) =>
        await _mediaService.SkipPreviousAsync();

    private async void OnPlayPauseClick(object sender, RoutedEventArgs e) =>
        await _mediaService.TogglePlayPauseAsync();

    private async void OnNextClick(object sender, RoutedEventArgs e) =>
        await _mediaService.SkipNextAsync();

    private static void OnOpenFrameworkControlClick(object sender, RoutedEventArgs e) =>
        FrameworkControlService.OpenDashboard();

    private void OnContextMenuOpened(object sender, RoutedEventArgs e) =>
        _runAtStartupMenuItem.IsChecked = StartupService.IsEnabled();

    private void OnRunAtStartupClick(object sender, RoutedEventArgs e) =>
        _runAtStartupMenuItem.IsChecked = StartupService.SetEnabled(_runAtStartupMenuItem.IsChecked);

    private static void OnExitClick(object sender, RoutedEventArgs e) => Environment.Exit(0);

    private SolidColorBrush TemperatureBrush(double temperature) =>
        temperature switch
        {
            < 60 => _coolBrush,
            < 75 => _warmBrush,
            < 90 => _hotBrush,
            _ => _extremeBrush
        };

    private SolidColorBrush FanBrush(int percent) =>
        percent switch
        {
            < 35 => _coolBrush,
            < 55 => _warmBrush,
            < 80 => _hotBrush,
            _ => _extremeBrush
        };

    private static Button TransparentButton()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(
            Border.BackgroundProperty,
            new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(
            ContentPresenter.ContentProperty,
            new TemplateBindingExtension(ContentControl.ContentProperty));
        presenter.SetValue(
            ContentPresenter.HorizontalAlignmentProperty,
            HorizontalAlignment.Center);
        presenter.SetValue(
            ContentPresenter.VerticalAlignmentProperty,
            VerticalAlignment.Center);
        border.AppendChild(presenter);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        template.Triggers.Add(new Trigger
        {
            Property = IsMouseOverProperty,
            Value = true,
            Setters =
            {
                new Setter(
                    Control.BackgroundProperty,
                    new SolidColorBrush(Color.FromArgb(0x28, 0x80, 0x80, 0x80)))
            }
        });
        template.Triggers.Add(new Trigger
        {
            Property = Button.IsPressedProperty,
            Value = true,
            Setters =
            {
                new Setter(
                    Control.BackgroundProperty,
                    new SolidColorBrush(Color.FromArgb(0x48, 0x80, 0x80, 0x80)))
            }
        });
        template.Triggers.Add(new Trigger
        {
            Property = IsEnabledProperty,
            Value = false,
            Setters =
            {
                new Setter(OpacityProperty, 0.35),
                new Setter(Control.BackgroundProperty, Brushes.Transparent)
            }
        });

        return new Button
        {
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Focusable = false,
            Template = template
        };
    }

    private static Button InlineButton(UIElement content, string tooltip, RoutedEventHandler handler)
    {
        var button = TransparentButton();
        button.Width = button.Height = 28;
        button.Content = content;
        button.ToolTip = tooltip;
        button.Click += handler;
        return button;
    }

    private static TextBlock Symbol(string glyph, double fontSize) => new()
    {
        Text = glyph,
        FontFamily = SymbolFont,
        FontSize = fontSize,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static TextBlock SecondaryText(string text, double fontSize) => new()
    {
        Text = text,
        FontSize = fontSize,
        VerticalAlignment = VerticalAlignment.Center,
        Opacity = 0.65
    };

    private static TextBlock StatusText(string text) => new()
    {
        Text = text,
        FontSize = 11,
        FontWeight = FontWeights.SemiBold,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static TextBlock MetadataText(string text, double fontSize) => new()
    {
        Text = text,
        FontSize = fontSize,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static StackPanel HorizontalPanel() => new()
    {
        Orientation = Orientation.Horizontal,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static StackPanel Pair(FrameworkElement first, FrameworkElement second)
    {
        var panel = HorizontalPanel();
        second.SetValue(MarginProperty, new Thickness(2, 0, 0, 0));
        panel.Children.Add(first);
        panel.Children.Add(second);
        return panel;
    }

    private static void AddToColumn(Grid grid, FrameworkElement element, int column)
    {
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private static BitmapImage? CreateBitmap(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0) return null;

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static string BuildDescription(string artist, string album)
    {
        if (!string.IsNullOrWhiteSpace(artist) && !string.IsNullOrWhiteSpace(album))
            return $"{artist} · {album}";
        return !string.IsNullOrWhiteSpace(artist) ? artist : album;
    }

    private static string BuildCompactText(string title, string artist)
    {
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(artist)) return "Unknown media";
        if (string.IsNullOrWhiteSpace(artist)) return title;
        if (string.IsNullOrWhiteSpace(title)) return artist;
        return $"{title} · {artist}";
    }

    private static SolidColorBrush Brush(byte red, byte green, byte blue) =>
        new(Color.FromRgb(red, green, blue));
}
