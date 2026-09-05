using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace AttaquerTaskbar.Controls;

internal static class TaskbarUi
{
    internal static readonly FontFamily SymbolFont = new("Segoe Fluent Icons");

    internal static Button TransparentButton()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(ContentControl.ContentProperty));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);

        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        template.Triggers.Add(new Trigger
        {
            Property = UIElement.IsMouseOverProperty,
            Value = true,
            Setters =
            {
                new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x28, 0x80, 0x80, 0x80)))
            }
        });
        template.Triggers.Add(new Trigger
        {
            Property = Button.IsPressedProperty,
            Value = true,
            Setters =
            {
                new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x48, 0x80, 0x80, 0x80)))
            }
        });
        template.Triggers.Add(new Trigger
        {
            Property = UIElement.IsEnabledProperty,
            Value = false,
            Setters =
            {
                new Setter(UIElement.OpacityProperty, 0.35),
                new Setter(Control.BackgroundProperty, Brushes.Transparent)
            }
        });

        return new Button
        {
            MinWidth = 0,
            MinHeight = 0,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Focusable = false,
            Template = template
        };
    }

    internal static Button InlineButton(UIElement content, string tooltip, RoutedEventHandler handler, double size = 28)
    {
        var button = TransparentButton();
        button.Width = button.Height = size;
        button.Content = content;
        button.ToolTip = tooltip;
        button.Click += handler;
        return button;
    }

    internal static TextBlock Symbol(string glyph, double fontSize) => new()
    {
        Text = glyph,
        FontFamily = SymbolFont,
        FontSize = fontSize,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };

    internal static TextBlock Text(string text, double fontSize, bool trim = false) => new()
    {
        Text = text,
        FontSize = fontSize,
        TextTrimming = trim ? TextTrimming.CharacterEllipsis : TextTrimming.None,
        VerticalAlignment = VerticalAlignment.Center
    };

    internal static StackPanel HorizontalPanel() => new()
    {
        Orientation = Orientation.Horizontal,
        VerticalAlignment = VerticalAlignment.Center
    };

    internal static Border FanIcon(double size)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri("pack://siteoforigin:,,,/Assets/icons8-fan-32.png", UriKind.Absolute);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();

        return new Border
        {
            Width = size,
            Height = size,
            Background = Brushes.White,
            OpacityMask = new ImageBrush(image) { Stretch = Stretch.Uniform },
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    internal static Grid ThermometerIcon(double size)
    {
        var brush = new SolidColorBrush(Colors.White);
        var grid = new Grid
        {
            Width = size,
            Height = size,
            VerticalAlignment = VerticalAlignment.Center,
            Tag = brush
        };
        grid.Children.Add(new Border
        {
            Width = size * 0.36,
            Height = size * 0.72,
            Margin = new Thickness(0, 0, 0, size * 0.2),
            VerticalAlignment = VerticalAlignment.Top,
            BorderBrush = brush,
            BorderThickness = new Thickness(Math.Max(1, size * 0.08)),
            CornerRadius = new CornerRadius(size * 0.18)
        });
        grid.Children.Add(new Rectangle
        {
            Width = Math.Max(1, size * 0.12),
            Height = size * 0.5,
            Margin = new Thickness(0, size * 0.18, 0, size * 0.18),
            VerticalAlignment = VerticalAlignment.Center,
            Fill = brush
        });
        grid.Children.Add(new Ellipse
        {
            Width = size * 0.54,
            Height = size * 0.54,
            VerticalAlignment = VerticalAlignment.Bottom,
            Fill = brush
        });
        return grid;
    }

    internal static void SetIconBrush(FrameworkElement icon, Brush brush)
    {
        if (icon is Border border) border.Background = brush;
        if (icon is Grid { Tag: SolidColorBrush iconBrush } && brush is SolidColorBrush solid)
            iconBrush.Color = solid.Color;
    }

    internal static void AddToColumn(Grid grid, UIElement element, int column)
    {
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }
}
