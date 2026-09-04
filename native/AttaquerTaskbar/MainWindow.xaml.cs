using System.Runtime.InteropServices;
using AttaquerTaskbar.Controls;
using AttaquerTaskbar.Diagnostics;
using Deskband11Lib.Core;
using Deskband11Lib.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinUIEx;

namespace AttaquerTaskbar;

public sealed partial class MainWindow : Window
{
    private const uint WindowEndSessionMessage = 0x0016;
    private readonly WindowSubclassProcedure _windowSubclassProcedure;

    public TaskbarContentHost TaskbarContentHost { get; }
    public bool IsAlive => this.IsWindowAlive();

    private delegate nint WindowSubclassProcedure(
        nint windowHandle,
        uint message,
        nint wParam,
        nint lParam,
        nuint subclassIdentifier,
        nuint referenceData);

    [LibraryImport("comctl32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowSubclass(
        nint windowHandle,
        WindowSubclassProcedure procedure,
        nuint subclassIdentifier,
        nuint referenceData);

    [LibraryImport("comctl32.dll")]
    private static partial nint DefSubclassProc(
        nint windowHandle,
        uint message,
        nint wParam,
        nint lParam);

    public MainWindow()
    {
        InitializeComponent();
        DiagnosticLog.Write("Main window XAML initialized.");

        try
        {
            SystemBackdrop = new TransparentTintBackdrop();
            DiagnosticLog.Write("Transparent taskbar backdrop initialized.");
        }
        catch (Exception exception)
        {
            // The backdrop is cosmetic. Keep launching if WinUIEx cannot
            // activate it in an unpackaged NativeAOT process.
            DiagnosticLog.WriteException("Transparent taskbar backdrop failed", exception);
        }

        try
        {
            TaskbarRoot.Children.Add(new TaskbarContent());
            DiagnosticLog.Write("Taskbar content XAML initialized.");
        }
        catch (Exception exception)
        {
            DiagnosticLog.WriteException("Taskbar content XAML failed", exception);
            TaskbarRoot.Children.Add(new TextBlock
            {
                Text = "Attaquer UI failed — see diagnostic log",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 6, 0)
            });
        }

        TaskbarContentHost = new TaskbarContentHost(this, TaskbarRoot, new()
        {
            PreferredWidth = 500,
            PreferredHeight = 48,
            Placement = TaskbarContentPlacement.Auto,
            AllowFixedSlotResize = true,
            AnimateLayoutChanges = false,
            LayoutRefreshInterval = TimeSpan.FromMilliseconds(250)
        });
        DiagnosticLog.Write("Deskband11Lib host created (500 x 48 preferred DIPs, automatic placement).");

        _windowSubclassProcedure = OnWindowSubclassProcedure;
        if (!SetWindowSubclass(this.GetWindowHandle(), _windowSubclassProcedure, 1, 0))
            DiagnosticLog.Write($"SetWindowSubclass failed with Win32 error {Marshal.GetLastPInvokeError()}.");
        else
            DiagnosticLog.Write("Window subclass installed.");
    }

    public Task PrepareTaskbarContentAsync() => TaskbarContentHost.AttachWhenLayoutReadyAsync();

    private void OnWindowClosed(object sender, WindowEventArgs e) => TaskbarContentHost.Dispose();

    private nint OnWindowSubclassProcedure(
        nint windowHandle,
        uint message,
        nint wParam,
        nint lParam,
        nuint subclassIdentifier,
        nuint referenceData)
    {
        if (message == WindowEndSessionMessage && wParam != 0) Environment.Exit(0);
        return DefSubclassProc(windowHandle, message, wParam, lParam);
    }
}
