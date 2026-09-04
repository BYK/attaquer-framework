using System.Runtime.InteropServices;
using AttaquerTaskbar.Diagnostics;
using Deskband11Lib.Core;
using Deskband11Lib.WinUI;
using Microsoft.UI.Xaml;
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

        TaskbarContentHost = new TaskbarContentHost(this, (FrameworkElement)Content, new()
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
