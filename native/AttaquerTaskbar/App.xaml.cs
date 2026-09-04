using AttaquerTaskbar.Diagnostics;
using AttaquerTaskbar.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace AttaquerTaskbar;

public partial class App : Application
{
    private static MainWindow? s_mainWindow;

    public static SystemMediaTransportService MediaService { get; private set; } = null!;
    public static FrameworkControlService ThermalService { get; private set; } = null!;

    public App()
    {
        DiagnosticLog.Write("Constructing the WinUI application object.");
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        InitializeComponent();
        DiagnosticLog.Write("WinUI application resources initialized.");
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            DiagnosticLog.Write("WinUI launch started.");
            var dispatcher = DispatcherQueue.GetForCurrentThread()
                ?? throw new InvalidOperationException("The UI dispatcher is unavailable.");

            MediaService = new SystemMediaTransportService(dispatcher);
            ThermalService = new FrameworkControlService(dispatcher);
            ThermalService.Start();
            DiagnosticLog.Write("Framework Control service started; media initialization deferred.");

            // Explorer may still be constructing its taskbar immediately after sign-in.
            await Task.Delay(750);
            await InitializeMainWindowAsync();
            _ = InitializeMediaServiceAsync();
            DiagnosticLog.Write("WinUI launch completed.");
        }
        catch (Exception exception)
        {
            DiagnosticLog.WriteException("WinUI launch failed", exception);
            throw;
        }
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

        DiagnosticLog.Write("Creating the taskbar window.");
        var window = new MainWindow();
        s_mainWindow = window;
        window.TaskbarContentHost.TaskbarWindowRecreationRequired += OnTaskbarRecreationRequired;

        DiagnosticLog.Write("Waiting for Deskband11Lib to attach to the taskbar layout.");
        await window.PrepareTaskbarContentAsync();
        DiagnosticLog.Write("Deskband11Lib attached to the taskbar layout.");
        window.Activate();
        DiagnosticLog.Write("Taskbar window activated.");
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
            throw;
        }
    }

    private static void OnUnhandledException(
        object sender,
        Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        DiagnosticLog.WriteException("Unhandled WinUI exception", args.Exception);

        // Windows Insider build 26220 raises this from WinUI's optional
        // presenter initialization on the dispatcher. The taskbar host uses
        // the raw HWND and does not require that limited-access feature.
        if (args.Exception.HResult == unchecked((int)0x80040111) &&
            args.Exception.ToString().Contains(
                "Windows.ApplicationModel.LimitedAccessFeatures",
                StringComparison.Ordinal))
        {
            args.Handled = true;
            DiagnosticLog.Write("Ignored unavailable optional WinUI limited-access feature.");
        }
    }

    private static void OnDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs args)
    {
        if (args.ExceptionObject is Exception exception)
            DiagnosticLog.WriteException("Unhandled application-domain exception", exception);
        else
            DiagnosticLog.Write($"Unhandled application-domain exception: {args.ExceptionObject}");
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args) =>
        DiagnosticLog.WriteException("Unobserved task exception", args.Exception);
}
