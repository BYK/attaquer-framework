using System.Windows;
using System.Windows.Interop;
using AttaquerTaskbar.Diagnostics;
using Deskband11Lib.Core;
using Deskband11Lib.Wpf;

namespace AttaquerTaskbar;

public partial class MainWindow : Window
{
    internal TaskbarContentHost TaskbarContentHost { get; }

    public bool IsAlive => TaskbarWindowHelper.IsWindowAlive(new WindowInteropHelper(this).Handle);

    public MainWindow()
    {
        InitializeComponent();
        DiagnosticLog.Write("WPF taskbar content initialized.");

        TaskbarContentHost = new TaskbarContentHost(this, (FrameworkElement)Content, new()
        {
            PreferredWidth = 500,
            PreferredHeight = 48,
            Placement = TaskbarContentPlacement.Auto,
            AllowFixedSlotResize = true,
            AnimateLayoutChanges = false,
            LayoutRefreshInterval = TimeSpan.FromMilliseconds(250)
        });
        DiagnosticLog.Write("Deskband11Lib.Wpf host created (500 x 48 preferred DIPs, automatic placement).");
    }

    public Task PrepareTaskbarContentAsync() => TaskbarContentHost.AttachWhenLayoutReadyAsync();

    protected override void OnClosed(EventArgs e)
    {
        TaskbarContentHost.Dispose();
        base.OnClosed(e);
    }
}
