using System.Windows;
using System.Windows.Media;

namespace AttaquerTaskbar.Controls;

public sealed class Sparkline : FrameworkElement
{
    private readonly Queue<double> _values = new();
    private readonly int _capacity;
    private readonly double _minimum;
    private readonly double _maximum;

    public Sparkline(int capacity, double minimum, double maximum)
    {
        _capacity = capacity;
        _minimum = minimum;
        _maximum = maximum;
        SnapsToDevicePixels = true;
    }

    public Brush LineBrush { get; set; } = Brushes.White;

    public double? Latest => _values.Count == 0 ? null : _values.Last();

    public void Add(double? value)
    {
        if (value is not double finite || !double.IsFinite(finite)) return;
        _values.Enqueue(Math.Clamp(finite, _minimum, _maximum));
        while (_values.Count > _capacity) _values.Dequeue();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) =>
        new(
            double.IsInfinity(availableSize.Width) ? 48 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 16 : availableSize.Height);

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (_values.Count == 0 || ActualWidth <= 1 || ActualHeight <= 1) return;

        var values = _values.ToArray();
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            for (var index = 0; index < values.Length; index++)
            {
                var x = values.Length == 1
                    ? ActualWidth
                    : index * (ActualWidth - 1) / (values.Length - 1);
                var normalized = (values[index] - _minimum) / (_maximum - _minimum);
                var y = (ActualHeight - 1) * (1 - normalized);
                var point = new Point(x, y);
                if (index == 0) context.BeginFigure(point, false, false);
                else context.LineTo(point, true, false);
            }
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(null, new Pen(LineBrush, 1.5), geometry);
    }
}
