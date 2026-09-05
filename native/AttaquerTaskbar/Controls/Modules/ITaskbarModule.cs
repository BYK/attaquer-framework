using System.Windows;
using System.Windows.Media;

namespace AttaquerTaskbar.Controls.Modules;

internal interface ITaskbarModule
{
    string Id { get; }

    FrameworkElement TaskbarView { get; }

    FrameworkElement FlyoutView { get; }

    event EventHandler? FlyoutRequested;

    void Start();

    void Stop();

    void ApplyLayout(bool compact, double availableWidth);

    void ApplyTheme(Brush foreground);
}
