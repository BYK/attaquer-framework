using AttaquerTaskbar.Diagnostics;
using AttaquerTaskbar.Models;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace AttaquerTaskbar.Services;

/// <summary>
/// Reads and controls the system's current media session. This is based on
/// BarPlay's SystemMediaTransportService, reduced to the data used inline.
/// </summary>
public sealed class SystemMediaTransportService : IDisposable
{
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;
    private BitmapImage? _cachedThumbnail;
    private int _started;
    private bool _isDisposed;

    public SystemMediaTransportService(DispatcherQueue dispatcherQueue) =>
        _dispatcherQueue = dispatcherQueue;

    public MediaSnapshot CurrentSnapshot { get; private set; } = MediaSnapshot.Empty;

    public event Action<MediaSnapshot>? StateChanged;

    public async Task InitializeAsync()
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;

        try
        {
            _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _sessionManager.SessionsChanged += OnSessionsChanged;
            _sessionManager.CurrentSessionChanged += OnCurrentSessionChanged;
            UpdateCurrentSession();
            await RefreshSnapshotAsync();
        }
        catch (Exception exception)
        {
            DiagnosticLog.WriteException("Windows media-session broker initialization failed", exception);
            Interlocked.Exchange(ref _started, 0);
            Publish(MediaSnapshot.Empty);

            // The media broker can be unavailable for a moment immediately
            // after sign-in. Retry without delaying the taskbar itself.
            if (!_isDisposed)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
                if (!_isDisposed) _ = InitializeAsync();
            }
        }
    }

    public async Task<bool> SkipPreviousAsync() =>
        _currentSession is not null && await _currentSession.TrySkipPreviousAsync();

    public async Task<bool> SkipNextAsync() =>
        _currentSession is not null && await _currentSession.TrySkipNextAsync();

    public async Task<bool> TogglePlayPauseAsync() =>
        _currentSession is not null && await _currentSession.TryTogglePlayPauseAsync();

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        DetachCurrentSession();
        if (_sessionManager is not null)
        {
            _sessionManager.SessionsChanged -= OnSessionsChanged;
            _sessionManager.CurrentSessionChanged -= OnCurrentSessionChanged;
        }

        _refreshGate.Dispose();
        StateChanged = null;
    }

    private void OnSessionsChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        SessionsChangedEventArgs args) =>
        _dispatcherQueue.TryEnqueue(() =>
        {
            UpdateCurrentSession();
            _ = RefreshSnapshotAsync();
        });

    private void OnCurrentSessionChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args) =>
        _dispatcherQueue.TryEnqueue(() =>
        {
            UpdateCurrentSession();
            _ = RefreshSnapshotAsync();
        });

    private void OnMediaPropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs args)
    {
        _cachedThumbnail = null;
        _dispatcherQueue.TryEnqueue(() => _ = RefreshSnapshotAsync());
    }

    private void OnPlaybackInfoChanged(
        GlobalSystemMediaTransportControlsSession sender,
        PlaybackInfoChangedEventArgs args) =>
        _dispatcherQueue.TryEnqueue(() => _ = RefreshSnapshotAsync());

    private void UpdateCurrentSession()
    {
        DetachCurrentSession();
        _cachedThumbnail = null;
        _currentSession = _sessionManager?.GetCurrentSession();

        if (_currentSession is not null)
        {
            _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
        }
    }

    private void DetachCurrentSession()
    {
        if (_currentSession is null) return;
        _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        _currentSession = null;
    }

    private async Task RefreshSnapshotAsync()
    {
        if (_isDisposed || _sessionManager is null) return;
        if (!await _refreshGate.WaitAsync(0)) return;

        try
        {
            var session = _currentSession;
            if (session is null)
            {
                Publish(MediaSnapshot.Empty);
                return;
            }

            var properties = await session.TryGetMediaPropertiesAsync();
            if (_isDisposed || !ReferenceEquals(session, _currentSession)) return;

            var playbackInfo = session.GetPlaybackInfo();
            var controls = playbackInfo.Controls;

            if (_cachedThumbnail is null && properties.Thumbnail is not null)
            {
                try { _cachedThumbnail = await LoadThumbnailAsync(properties.Thumbnail); }
                catch { _cachedThumbnail = null; }
            }

            Publish(new MediaSnapshot(
                properties.Title ?? string.Empty,
                properties.Artist ?? string.Empty,
                properties.AlbumTitle ?? string.Empty,
                true,
                playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                controls.IsPreviousEnabled,
                controls.IsNextEnabled,
                controls.IsPlayEnabled || controls.IsPauseEnabled,
                _cachedThumbnail));
        }
        catch
        {
            // A session may disappear between the manager event and this read.
            if (_currentSession is null) Publish(MediaSnapshot.Empty);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private void Publish(MediaSnapshot snapshot)
    {
        if (_isDisposed) return;
        CurrentSnapshot = snapshot;
        _dispatcherQueue.TryEnqueue(() => StateChanged?.Invoke(snapshot));
    }

    private static async Task<BitmapImage?> LoadThumbnailAsync(
        IRandomAccessStreamReference thumbnailReference)
    {
        using var stream = await thumbnailReference.OpenReadAsync();
        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }
}
