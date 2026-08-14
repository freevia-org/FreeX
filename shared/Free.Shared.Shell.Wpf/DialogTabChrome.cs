using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Free.Shared.Shell;

namespace Free.Shared.Shell.Wpf;

/// <summary>Applies the shared classic tabbed-dialog header/body seam contract to WPF.</summary>
public static class DialogTabChrome
{
    private static readonly Brush PaneBorderBrush = CreateBrush(DialogTabChromeMetrics.PaneBorderHex);
    private static readonly Brush InactiveTabBorderBrush = CreateBrush(DialogTabChromeMetrics.InactiveTabBorderHex);
    private static readonly Brush InactiveTabBackgroundBrush = CreateBrush(DialogTabChromeMetrics.InactiveTabBackgroundHex);
    private static readonly Brush SelectedTabBackgroundBrush = CreateBrush(DialogTabChromeMetrics.SelectedTabBackgroundHex);

    public static void Apply(TabControl tabControl)
    {
        ArgumentNullException.ThrowIfNull(tabControl);

        tabControl.Padding = new Thickness(0);
        tabControl.BorderBrush = PaneBorderBrush;
        tabControl.BorderThickness = new Thickness(
            DialogTabChromeMetrics.PaneBorderThickness,
            DialogTabChromeMetrics.PaneBorderThickness,
            DialogTabChromeMetrics.PaneBorderThickness,
            DialogTabChromeMetrics.PaneBorderThickness);

        var style = new Style(typeof(TabItem));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, InactiveTabBorderBrush));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(
            DialogTabChromeMetrics.PaneBorderThickness,
            DialogTabChromeMetrics.PaneBorderThickness,
            DialogTabChromeMetrics.PaneBorderThickness,
            0)));
        style.Setters.Add(new Setter(Control.BackgroundProperty, InactiveTabBackgroundBrush));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty,
            new Thickness(0, 0, -DialogTabChromeMetrics.AdjacentTabOverlap, 0)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 2, 6, 2)));

        var selected = new Trigger
        {
            Property = Selector.IsSelectedProperty,
            Value = true,
        };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, SelectedTabBackgroundBrush));
        selected.Setters.Add(new Setter(Control.BorderBrushProperty, PaneBorderBrush));
        selected.Setters.Add(new Setter(FrameworkElement.MarginProperty,
            new Thickness(0, 0,
                -DialogTabChromeMetrics.AdjacentTabOverlap,
                -DialogTabChromeMetrics.SelectedTabContentOverlap)));
        selected.Setters.Add(new Setter(Panel.ZIndexProperty, 1));
        style.Triggers.Add(selected);

        tabControl.ItemContainerStyle = style;
    }

    private static Brush CreateBrush(string colorHex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
        brush.Freeze();
        return brush;
    }
}
