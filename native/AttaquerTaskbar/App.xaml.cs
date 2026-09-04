using AttaquerTaskbar.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace AttaquerTaskbar;

public partial class App : Application
{
    private static MainWindow? s_mainWindow;

    public static SystemMediaTransportService MediaService { get; private set; } = null!;
    public static FrameworkControlService ThermalService { get; private set; } = null!;

    public App() => InitializeComponent();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("The UI dispatcher is unavailable.");

        MediaService = new SystemMediaTransportService(dispatcher);
        ThermalService = new FrameworkControlService(dispatcher);
        ThermalService.Start();
        _ = MediaService.InitializeAsync();

        // Explorer may still be constructing its taskbar immediately after sign-in.
        await Task.Delay(750);
        await InitializeMainWindowAsync();
    }

    private static async Task InitializeMainWindowAsync()
    {
        if (s_mainWindow is not null) return;

        var window = new MainWindow();
        s_mainWindow = window;
        window.TaskbarContentHost.TaskbarWindowRecreationRequired += OnTaskbarRecreationRequired;

        await window.PrepareTaskbarContentAsync();
        window.Activate();
    }

    private static async void OnTaskbarRecreationRequired(object? sender, EventArgs e)
    {
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
}
