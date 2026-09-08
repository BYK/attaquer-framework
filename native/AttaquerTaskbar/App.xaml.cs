using System.Windows;
using System.Windows.Threading;
using AttaquerTaskbar.Diagnostics;
using AttaquerTaskbar.Services;

namespace AttaquerTaskbar;

public partial class App : Application
{
    private static MainWindow? s_mainWindow;
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    public static SystemMediaTransportService MediaService { get; private set; } = null!;
    public static FrameworkControlService ThermalService { get; private set; } = null!;
    public static GlazeWmService GlazeWmService { get; private set; } = null!;
    public static SettingsService Settings { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        DiagnosticLog.Initialize();
        DiagnosticLog.Write("Runtime host: WPF.");

        try
        {
            DiagnosticLog.Write("Initializing WinRT COM wrappers.");
            WinRT.ComWrappersSupport.InitializeComWrappers();

            _singleInstanceMutex = new Mutex(
                initiallyOwned: true,
                name: @"Local\AttaquerTaskbarSingleInstance",
                createdNew: out var isFirstInstance);
            _ownsSingleInstanceMutex = isFirstInstance;
            if (!isFirstInstance)
            {
                DiagnosticLog.Write("Another instance is already running; exiting.");
                Shutdown();
                return;
            }

            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            base.OnStartup(e);

            Settings = new SettingsService();
            MediaService = new SystemMediaTransportService(Dispatcher);
            ThermalService = new FrameworkControlService(Dispatcher);
            GlazeWmService = new GlazeWmService(Dispatcher, Settings);
            ThermalService.Start();
            GlazeWmService.Start();
            DiagnosticLog.Write("Framework Control and GlazeWM services started; media initialization deferred.");

            await Task.Delay(750);
            await InitializeMainWindowAsync();
            _ = InitializeMediaServiceAsync();
            DiagnosticLog.Write("WPF launch completed.");
        }
        catch (Exception exception)
        {
            DiagnosticLog.WriteException("WPF launch failed", exception);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        MediaService?.Dispose();
        ThermalService?.Dispose();
        GlazeWmService?.Dispose();
        if (_ownsSingleInstanceMutex) _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static async Task InitializeMediaServiceAsync()
    {
        DiagnosticLog.Write("Media service initialization started.");
        try
        {
            await MediaService.InitializeAsync();
            DiagnosticLog.Write("Media service initialization returned.");
        }
        catch (Exception exception)
        {
            DiagnosticLog.WriteException("Media service initialization escaped its retry loop", exception);
        }
    }

    private static async Task InitializeMainWindowAsync()
    {
        if (s_mainWindow is not null) return;

        DiagnosticLog.Write("Creating the WPF taskbar window.");
        var window = new MainWindow();
        s_mainWindow = window;
        window.TaskbarContentHost.TaskbarWindowRecreationRequired += OnTaskbarRecreationRequired;

        DiagnosticLog.Write("Waiting for Deskband11Lib.Wpf to attach to the taskbar layout.");
        await window.PrepareTaskbarContentAsync();
        DiagnosticLog.Write("Deskband11Lib.Wpf attached to the taskbar layout.");
        window.Show();
        DiagnosticLog.Write("Taskbar window shown.");
    }

    private static async void OnTaskbarRecreationRequired(object? sender, EventArgs e)
    {
        try
        {
            DiagnosticLog.Write("Explorer requested taskbar-window recreation.");
            var oldWindow = s_mainWindow;
            if (oldWindow is not null)
            {
                oldWindow.TaskbarContentHost.TaskbarWindowRecreationRequired -= OnTaskbarRecreationRequired;
                oldWindow.TaskbarContentHost.Dispose();
                s_mainWindow = null;
            }

            await Task.Delay(750);
            await InitializeMainWindowAsync();

            if (oldWindow?.IsAlive == true) oldWindow.Close();
        }
        catch (Exception exception)
        {
            DiagnosticLog.WriteException("Taskbar-window recreation failed", exception);
        }
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs args)
    {
        DiagnosticLog.WriteException("Unhandled WPF dispatcher exception", args.Exception);
        args.Handled = true;
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs args)
    {
        if (args.ExceptionObject is Exception exception)
            DiagnosticLog.WriteException("Unhandled application-domain exception", exception);
        else
            DiagnosticLog.Write($"Unhandled application-domain exception: {args.ExceptionObject}");
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        DiagnosticLog.WriteException("Unobserved task exception", args.Exception);
        args.SetObserved();
    }
}
