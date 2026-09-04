using AttaquerTaskbar.Diagnostics;
using Deskband11Lib.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Win32;

namespace AttaquerTaskbar.TaskbarHosting;

/// <summary>
/// WinUI facade for Deskband11Lib.Core that deliberately avoids AppWindow.
/// Deskband11Lib.WinUI configures AppWindow.Presenter, which activates
/// Windows.ApplicationModel.LimitedAccessFeatures and fails in some
/// unpackaged processes. The core host already applies the required HWND
/// parent and child-window styles, so presenter mutation is not required.
/// </summary>
internal sealed class TaskbarContentHost : TaskbarContentHostBase
{
    private readonly FrameworkElement _contentElement;

    public TaskbarContentHost(
        Window window,
        FrameworkElement contentElement,
        TaskbarContentHostOptions? options = null)
        : this(contentElement, CreateState(window, contentElement, options)) { }

    private TaskbarContentHost(FrameworkElement contentElement, ConstructionState state)
        : base(state.PlatformAdapter, state.Options)
    {
        _contentElement = contentElement;
        _contentElement.SizeChanged += OnContentElementSizeChanged;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _contentElement.SizeChanged -= OnContentElementSizeChanged;
        base.Dispose(disposing);
    }

    private void OnContentElementSizeChanged(object sender, SizeChangedEventArgs e) =>
        NotifyContentSizeChanged();

    private static ConstructionState CreateState(
        Window window,
        FrameworkElement contentElement,
        TaskbarContentHostOptions? options)
    {
        var resolvedOptions = options ?? new TaskbarContentHostOptions();
        return new ConstructionState(
            new WinUiTaskbarHostPlatformAdapter(window, contentElement, resolvedOptions),
            resolvedOptions);
    }

    private sealed record ConstructionState(
        WinUiTaskbarHostPlatformAdapter PlatformAdapter,
        TaskbarContentHostOptions Options);
}

internal sealed class WinUiTaskbarHostPlatformAdapter : ITaskbarHostPlatformAdapter
{
    private readonly Window _window;
    private readonly FrameworkElement _contentElement;
    private readonly TaskbarContentHostOptions _options;
    private ElementTheme _originalContentTheme;
    private bool _isPrepared;

    public WinUiTaskbarHostPlatformAdapter(
        Window window,
        FrameworkElement contentElement,
        TaskbarContentHostOptions options)
    {
        _window = window;
        _contentElement = contentElement;
        _options = options;
    }

    public nint WindowHandle => WinRT.Interop.WindowNative.GetWindowHandle(_window);

    public double RequestedWidth
    {
        get
        {
            if (_options.PreferredWidth > 0) return _options.PreferredWidth;
            if (!double.IsNaN(_contentElement.Width) && _contentElement.Width > 0)
                return _contentElement.Width;
            if (_contentElement.ActualWidth > 0) return _contentElement.ActualWidth;
            return _options.PreferredWidth;
        }
    }

    public double RequestedHeight
    {
        get
        {
            if (_options.PreferredHeight > 0) return _options.PreferredHeight;
            if (!double.IsNaN(_contentElement.Height) && _contentElement.Height > 0)
                return _contentElement.Height;
            if (_contentElement.ActualHeight > 0) return _contentElement.ActualHeight;
            return _options.PreferredHeight;
        }
    }

    public void PrepareWindowForChildHosting()
    {
        if (_isPrepared) return;

        _originalContentTheme = _contentElement.RequestedTheme;
        _contentElement.RequestedTheme = IsSystemLightTheme()
            ? ElementTheme.Light
            : ElementTheme.Dark;
        _isPrepared = true;
        DiagnosticLog.Write("Prepared WinUI content for HWND hosting without AppWindow presenter APIs.");
    }

    public void RestoreWindowAfterChildHosting()
    {
        if (!_isPrepared) return;
        _contentElement.RequestedTheme = _originalContentTheme;
        _isPrepared = false;
    }

    public void ApplyContentBounds(double maxWidth, double width, double height)
    {
        _contentElement.MaxWidth = maxWidth;
        _contentElement.Width = width;
        _contentElement.Height = height;
    }

    public void RunOnDispatcher(Action action) =>
        _window.DispatcherQueue.TryEnqueue(() => action());

    public ITaskbarHostTimer CreateTimer(TimeSpan interval, Action tick) =>
        new DispatcherQueueTaskbarHostTimer(_window.DispatcherQueue, interval, tick);

    private static bool IsSystemLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
    }
}

internal sealed class DispatcherQueueTaskbarHostTimer : ITaskbarHostTimer
{
    private readonly DispatcherQueueTimer _timer;
    private readonly Action _tick;

    public DispatcherQueueTaskbarHostTimer(
        DispatcherQueue dispatcherQueue,
        TimeSpan interval,
        Action tick)
    {
        _tick = tick;
        _timer = dispatcherQueue.CreateTimer();
        _timer.Interval = interval;
        _timer.Tick += OnTimerTick;
    }

    public bool IsRunning => _timer.IsRunning;

    public TimeSpan Interval
    {
        get => _timer.Interval;
        set => _timer.Interval = value;
    }

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
    }

    private void OnTimerTick(DispatcherQueueTimer sender, object e) => _tick();
}
