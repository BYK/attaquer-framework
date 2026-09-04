using AttaquerTaskbar.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace AttaquerTaskbar;

public static class Program
{
    private const string SingleInstanceKey = "AttaquerTaskbarSingleInstance";

    [STAThread]
    private static int Main(string[] args)
    {
        DiagnosticLog.Initialize();

        try
        {
            DiagnosticLog.Write("Initializing WinRT COM wrappers.");
            WinRT.ComWrappersSupport.InitializeComWrappers();

            DiagnosticLog.Write("Registering the single-instance key.");
            if (!AppInstance.FindOrRegisterForKey(SingleInstanceKey).IsCurrent)
            {
                DiagnosticLog.Write("Another instance is already registered; exiting.");
                return 0;
            }

            DiagnosticLog.Write("Starting the WinUI application.");
            Application.Start(_ =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                new App();
            });
            DiagnosticLog.Write("The WinUI message loop ended normally.");
            return 0;
        }
        catch (Exception exception)
        {
            DiagnosticLog.WriteException("Fatal startup failure", exception);
            return 1;
        }
    }
}
