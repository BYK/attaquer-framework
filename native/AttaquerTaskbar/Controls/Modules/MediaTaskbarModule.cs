using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AttaquerTaskbar.Models;
using AttaquerTaskbar.Services;

namespace AttaquerTaskbar.Controls.Modules;

internal sealed class MediaTaskbarModule : ITaskbarModule
{
    private readonly SystemMediaTransportService _service;
    private readonly Grid _taskbarRoot;
    private readonly Border _taskbarArtworkHost;
    private readonly Image _taskbarArtwork;
    private readonly TextBlock _taskbarArtworkPlaceholder;
    private readonly StackPanel _normalMetadata;
    private readonly TextBlock _title;
    private readonly TextBlock _description;
    private readonly TextBlock _compactMetadata;
    private readonly Button _previousButton;
    private readonly TextBlock _previousIcon;
    private readonly Button _playPauseButton;
    private readonly TextBlock _playPauseIcon;
    private readonly Button _nextButton;
    private readonly TextBlock _nextIcon;

    private readonly Border _flyoutArtworkHost;
    private readonly Image _flyoutArtwork;
    private readonly TextBlock _flyoutArtworkPlaceholder;
    private readonly TextBlock _flyoutTitle;
    private readonly TextBlock _flyoutDescription;
    private readonly Slider _timeline;
    private readonly TextBlock _position;
    private readonly TextBlock _duration;
    private readonly Button _flyoutPreviousButton;
    private readonly TextBlock _flyoutPreviousIcon;
    private readonly Button _flyoutPlayPauseButton;
    private readonly TextBlock _flyoutPlayPauseIcon;
    private readonly Button _flyoutNextButton;
    private readonly TextBlock _flyoutNextIcon;

    private MediaSnapshot _snapshot = MediaSnapshot.Empty;
    private bool _compact;
    private bool _started;
    private bool _updatingTimeline;

    public MediaTaskbarModule(SystemMediaTransportService service)
    {
        _service = service;

        (_taskbarArtworkHost, _taskbarArtwork, _taskbarArtworkPlaceholder) = CreateArtwork(32, 16);
        _taskbarArtworkHost.Margin = new Thickness(0, 0, 5, 0);
        _title = TaskbarUi.Text("Nothing playing", 12, trim: true);
        _description = TaskbarUi.Text(string.Empty, 10, trim: true);
        _description.Opacity = 0.65;
        _normalMetadata = new StackPanel();
        _normalMetadata.Children.Add(_title);
        _normalMetadata.Children.Add(_description);
        _compactMetadata = TaskbarUi.Text("Nothing playing", 11, trim: true);
        _compactMetadata.Visibility = Visibility.Collapsed;
        var metadataGrid = new Grid { MinWidth = 20 };
        metadataGrid.Children.Add(_normalMetadata);
        metadataGrid.Children.Add(_compactMetadata);
        var metadataButton = TaskbarUi.TransparentButton();
        metadataButton.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        metadataButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        metadataButton.Content = metadataGrid;
        metadataButton.Click += (_, _) => FlyoutRequested?.Invoke(this, EventArgs.Empty);

        _previousIcon = TaskbarUi.Symbol("\uE892", 15);
        _previousButton = TaskbarUi.InlineButton(_previousIcon, "Previous", OnPreviousClick);
        _playPauseIcon = TaskbarUi.Symbol("\uE768", 15);
        _playPauseButton = TaskbarUi.InlineButton(_playPauseIcon, "Play or pause", OnPlayPauseClick);
        _nextIcon = TaskbarUi.Symbol("\uE893", 15);
        _nextButton = TaskbarUi.InlineButton(_nextIcon, "Next", OnNextClick);
        var transport = TaskbarUi.HorizontalPanel();
        transport.Margin = new Thickness(5, 0, 0, 0);
        transport.Children.Add(_previousButton);
        _playPauseButton.Margin = new Thickness(1, 0, 1, 0);
        transport.Children.Add(_playPauseButton);
        transport.Children.Add(_nextButton);

        _taskbarRoot = new Grid();
        _taskbarRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _taskbarRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _taskbarRoot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _taskbarArtworkHost.Cursor = Cursors.Hand;
        _taskbarArtworkHost.MouseLeftButtonUp += (_, _) => FlyoutRequested?.Invoke(this, EventArgs.Empty);
        TaskbarUi.AddToColumn(_taskbarRoot, _taskbarArtworkHost, 0);
        TaskbarUi.AddToColumn(_taskbarRoot, metadataButton, 1);
        TaskbarUi.AddToColumn(_taskbarRoot, transport, 2);
        TaskbarView = _taskbarRoot;

        (_flyoutArtworkHost, _flyoutArtwork, _flyoutArtworkPlaceholder) = CreateArtwork(72, 28);
        _flyoutArtworkHost.Margin = new Thickness(0, 0, 12, 0);
        _flyoutTitle = TaskbarUi.Text("Nothing playing", 16, trim: true);
        _flyoutTitle.FontWeight = FontWeights.SemiBold;
        _flyoutDescription = TaskbarUi.Text(string.Empty, 12, trim: true);
        _flyoutDescription.Opacity = 0.68;

        _timeline = new Slider
        {
            Minimum = 0,
            Maximum = 1,
            Value = 0,
            IsMoveToPointEnabled = true,
            Margin = new Thickness(0, 8, 0, 0)
        };
        _timeline.PreviewMouseLeftButtonUp += OnTimelineReleased;
        _position = TaskbarUi.Text("0:00", 10);
        _position.Opacity = 0.65;
        _duration = TaskbarUi.Text("0:00", 10);
        _duration.HorizontalAlignment = HorizontalAlignment.Right;
        _duration.Opacity = 0.65;
        var timeLabels = new Grid();
        timeLabels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        timeLabels.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        timeLabels.Children.Add(_position);
        Grid.SetColumn(_duration, 1);
        timeLabels.Children.Add(_duration);

        _flyoutPreviousIcon = TaskbarUi.Symbol("\uE892", 18);
        _flyoutPreviousButton = TaskbarUi.InlineButton(_flyoutPreviousIcon, "Previous", OnPreviousClick, 36);
        _flyoutPlayPauseIcon = TaskbarUi.Symbol("\uE768", 20);
        _flyoutPlayPauseButton = TaskbarUi.InlineButton(_flyoutPlayPauseIcon, "Play or pause", OnPlayPauseClick, 40);
        _flyoutNextIcon = TaskbarUi.Symbol("\uE893", 18);
        _flyoutNextButton = TaskbarUi.InlineButton(_flyoutNextIcon, "Next", OnNextClick, 36);
        var flyoutTransport = TaskbarUi.HorizontalPanel();
        flyoutTransport.HorizontalAlignment = HorizontalAlignment.Center;
        flyoutTransport.Margin = new Thickness(0, 7, 0, 0);
        flyoutTransport.Children.Add(_flyoutPreviousButton);
        _flyoutPlayPauseButton.Margin = new Thickness(5, 0, 5, 0);
        flyoutTransport.Children.Add(_flyoutPlayPauseButton);
        flyoutTransport.Children.Add(_flyoutNextButton);

        var metadata = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        metadata.Children.Add(_flyoutTitle);
        metadata.Children.Add(_flyoutDescription);
        var mediaHeader = new Grid();
        mediaHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        mediaHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mediaHeader.Children.Add(_flyoutArtworkHost);
        Grid.SetColumn(metadata, 1);
        mediaHeader.Children.Add(metadata);

        var flyout = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        flyout.Children.Add(mediaHeader);
        flyout.Children.Add(_timeline);
        flyout.Children.Add(timeLabels);
        flyout.Children.Add(flyoutTransport);
        FlyoutView = flyout;
    }

    public string Id => "media";

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
        _compact = compact;
        var artworkSize = compact ? 24 : 32;
        _taskbarArtworkHost.Width = _taskbarArtworkHost.Height = artworkSize;
        _taskbarArtworkHost.Margin = new Thickness(0, 0, compact ? 3 : 5, 0);
        _taskbarArtworkHost.CornerRadius = new CornerRadius(compact ? 3 : 4);
        _taskbarArtworkPlaceholder.FontSize = compact ? 13 : 16;
        _normalMetadata.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        _compactMetadata.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;

        var buttonSize = compact ? 24 : 28;
        SetButtonDensity(_previousButton, _previousIcon, buttonSize, compact ? 13 : 15);
        SetButtonDensity(_playPauseButton, _playPauseIcon, buttonSize, compact ? 13 : 15);
        SetButtonDensity(_nextButton, _nextIcon, buttonSize, compact ? 13 : 15);
        var hideSecondaryControls = availableWidth > 0 && availableWidth < (compact ? 330 : 380);
        _previousButton.Visibility = hideSecondaryControls ? Visibility.Collapsed : Visibility.Visible;
        _nextButton.Visibility = hideSecondaryControls ? Visibility.Collapsed : Visibility.Visible;
    }

    public void ApplyTheme(Brush foreground)
    {
        _taskbarRoot.Foreground = foreground;
        foreach (var button in new[]
                 {
                     _previousButton, _playPauseButton, _nextButton,
                     _flyoutPreviousButton, _flyoutPlayPauseButton, _flyoutNextButton
                 })
            button.Foreground = foreground;
    }

    private void ApplySnapshot(MediaSnapshot snapshot)
    {
        _snapshot = snapshot;
        var title = snapshot.HasSession
            ? string.IsNullOrWhiteSpace(snapshot.Title) ? "Unknown title" : snapshot.Title
            : "Nothing playing";
        var description = snapshot.HasSession
            ? BuildDescription(snapshot.Artist, snapshot.Album)
            : string.Empty;
        _title.Text = _flyoutTitle.Text = title;
        _description.Text = _flyoutDescription.Text = description;
        _description.Visibility = _flyoutDescription.Visibility = string.IsNullOrWhiteSpace(description)
            ? Visibility.Collapsed
            : Visibility.Visible;
        _compactMetadata.Text = snapshot.HasSession
            ? BuildCompactText(snapshot.Title, snapshot.Artist)
            : "Nothing playing";

        var bitmap = CreateBitmap(snapshot.Thumbnail);
        ApplyArtwork(_taskbarArtworkHost, _taskbarArtwork, _taskbarArtworkPlaceholder, bitmap, snapshot.HasSession);
        ApplyArtwork(_flyoutArtworkHost, _flyoutArtwork, _flyoutArtworkPlaceholder, bitmap, snapshot.HasSession);

        foreach (var button in new[] { _previousButton, _flyoutPreviousButton })
            button.IsEnabled = snapshot.CanSkipPrevious;
        foreach (var button in new[] { _nextButton, _flyoutNextButton })
            button.IsEnabled = snapshot.CanSkipNext;
        foreach (var button in new[] { _playPauseButton, _flyoutPlayPauseButton })
            button.IsEnabled = snapshot.CanPlayPause;
        var playPauseGlyph = snapshot.IsPlaying ? "\uE769" : "\uE768";
        _playPauseIcon.Text = _flyoutPlayPauseIcon.Text = playPauseGlyph;

        _updatingTimeline = true;
        var duration = Math.Max(0, snapshot.EndTime.TotalSeconds);
        _timeline.Maximum = Math.Max(1, duration);
        _timeline.Value = Math.Clamp(snapshot.Position.TotalSeconds, 0, _timeline.Maximum);
        _timeline.IsEnabled = snapshot.CanSeek && duration > 0;
        _position.Text = FormatTime(snapshot.Position);
        _duration.Text = FormatTime(snapshot.EndTime);
        _updatingTimeline = false;
    }

    private async void OnTimelineReleased(object sender, MouseButtonEventArgs e)
    {
        if (_updatingTimeline || !_snapshot.CanSeek) return;
        await _service.SeekAsync(TimeSpan.FromSeconds(_timeline.Value));
    }

    private async void OnPreviousClick(object sender, RoutedEventArgs e) =>
        await _service.SkipPreviousAsync();

    private async void OnPlayPauseClick(object sender, RoutedEventArgs e) =>
        await _service.TogglePlayPauseAsync();

    private async void OnNextClick(object sender, RoutedEventArgs e) =>
        await _service.SkipNextAsync();

    private static void SetButtonDensity(Button button, TextBlock icon, double size, double iconSize)
    {
        button.Width = button.Height = size;
        icon.FontSize = iconSize;
    }

    private static (Border Host, Image Image, TextBlock Placeholder) CreateArtwork(double size, double placeholderSize)
    {
        var placeholder = TaskbarUi.Symbol("\uE93C", placeholderSize);
        placeholder.Opacity = 0.65;
        var image = new Image
        {
            Stretch = Stretch.UniformToFill,
            Visibility = Visibility.Collapsed,
            SnapsToDevicePixels = true
        };
        var content = new Grid();
        content.Children.Add(placeholder);
        content.Children.Add(image);
        var host = new Border
        {
            Width = size,
            Height = size,
            Background = new SolidColorBrush(Color.FromArgb(0x18, 0x80, 0x80, 0x80)),
            CornerRadius = new CornerRadius(size <= 32 ? 4 : 7),
            ClipToBounds = true,
            Child = content
        };
        return (host, image, placeholder);
    }

    private static void ApplyArtwork(
        Border host,
        Image image,
        TextBlock placeholder,
        BitmapImage? bitmap,
        bool hasSession)
    {
        host.Visibility = hasSession ? Visibility.Visible : Visibility.Collapsed;
        image.Source = bitmap;
        image.Visibility = hasSession && bitmap is not null ? Visibility.Visible : Visibility.Collapsed;
        placeholder.Visibility = image.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
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

    private static string FormatTime(TimeSpan time)
    {
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{(int)time.TotalMinutes}:{time.Seconds:00}";
    }
}
