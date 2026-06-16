using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using FreeX.Ribbon.Icons;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;
using AvaloniaLine = Avalonia.Controls.Shapes.Line;
using AvaloniaRectangle = Avalonia.Controls.Shapes.Rectangle;
using AvaloniaEllipse = Avalonia.Controls.Shapes.Ellipse;

namespace FreeX.Ribbon.Avalonia;

/// <summary>
/// Avalonia ribbon icon renderer. Draws the SAME shapes as the WPF renderer by consuming the
/// shared, platform-neutral <see cref="RibbonIconDefinitions"/> (the cross-platform source of truth)
/// and translating each <see cref="RibbonIconElement"/> into native Avalonia shapes laid out on a
/// 24×24 <see cref="Canvas"/>, scaled to the requested size by a <see cref="Viewbox"/>.
/// </summary>
internal static class AvaloniaRibbonIcons
{
    private const double Artboard = RibbonIconGeometry.Artboard;

    /// <summary>Builds an icon control for the given kind at the requested pixel size.</summary>
    public static Control Build(RibbonCommandIconKind kind, double size) =>
        Build(new RibbonCommandIcon(kind), size, foreground: null);

    /// <summary>
    /// Builds an icon control for the given command icon (kind + accent) at the requested pixel size.
    /// <paramref name="foreground"/> overrides the glyph color; when null a default is used.
    /// </summary>
    public static Control Build(RibbonCommandIcon icon, double size, IBrush? foreground)
    {
        var geometry = RibbonIconDefinitions.Resolve(icon.Kind);
        var brush = ResolveAccentBrush(icon.Accent, foreground);

        var canvas = new Canvas
        {
            Width = Artboard,
            Height = Artboard,
        };

        foreach (var element in geometry.Elements)
        {
            if (CreateElement(element, brush) is { } control)
                canvas.Children.Add(control);
        }

        return new Viewbox
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            Child = canvas,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private static IBrush ResolveAccentBrush(RibbonCommandIconAccent accent, IBrush? foreground)
    {
        if (RibbonIconAccents.Resolve(accent) is { } color)
            return new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));

        return foreground ?? Brushes.Black;
    }

    private static Control? CreateElement(RibbonIconElement element, IBrush brush) => element.Kind switch
    {
        RibbonIconElementKind.Path => BuildPath(element, brush),
        RibbonIconElementKind.Line => BuildLine(element, brush),
        RibbonIconElementKind.Rectangle => BuildRectangle(element, brush),
        RibbonIconElementKind.FilledRectangle => BuildFilledRectangle(element, brush),
        RibbonIconElementKind.Ellipse => BuildEllipse(element, brush),
        RibbonIconElementKind.FilledCircle => BuildFilledCircle(element, brush),
        RibbonIconElementKind.Text => BuildText(element, brush),
        _ => null,
    };

    private static Control BuildPath(RibbonIconElement element, IBrush brush)
    {
        var path = new AvaloniaPath
        {
            Data = Geometry.Parse(element.PathData!),
            Stroke = brush,
            StrokeThickness = element.StrokeThickness,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
        };

        if (element.FillOpacity > 0)
        {
            // Mirror the WPF behaviour: a filled shape path is stroked + faintly filled, with the
            // whole element dimmed by the fill opacity.
            path.Fill = brush;
            path.Opacity = element.FillOpacity;
        }

        return path;
    }

    private static Control BuildLine(RibbonIconElement element, IBrush brush)
    {
        var line = new AvaloniaLine
        {
            StartPoint = new Point(element.X1, element.Y1),
            EndPoint = new Point(element.X2, element.Y2),
            Stroke = brush,
            StrokeThickness = element.StrokeThickness,
            StrokeLineCap = PenLineCap.Round,
        };
        if (element.Dashed)
            line.StrokeDashArray = new AvaloniaList<double> { 2, 2 };
        return line;
    }

    private static Control BuildRectangle(RibbonIconElement element, IBrush brush)
    {
        var rect = new AvaloniaRectangle
        {
            Width = element.Width,
            Height = element.Height,
            RadiusX = element.Radius,
            RadiusY = element.Radius,
            Stroke = brush,
            StrokeThickness = element.StrokeThickness,
            Fill = Brushes.Transparent,
        };
        Canvas.SetLeft(rect, element.X1);
        Canvas.SetTop(rect, element.Y1);
        return rect;
    }

    private static Control BuildFilledRectangle(RibbonIconElement element, IBrush brush)
    {
        var rect = new AvaloniaRectangle
        {
            Width = element.Width,
            Height = element.Height,
            Fill = brush,
        };
        Canvas.SetLeft(rect, element.X1);
        Canvas.SetTop(rect, element.Y1);
        return rect;
    }

    private static Control BuildEllipse(RibbonIconElement element, IBrush brush)
    {
        var ellipse = new AvaloniaEllipse
        {
            Width = element.Width,
            Height = element.Height,
            Stroke = brush,
            StrokeThickness = element.StrokeThickness,
            Fill = Brushes.Transparent,
        };
        Canvas.SetLeft(ellipse, element.X1);
        Canvas.SetTop(ellipse, element.Y1);
        return ellipse;
    }

    private static Control BuildFilledCircle(RibbonIconElement element, IBrush brush)
    {
        var diameter = element.Width;
        var ellipse = new AvaloniaEllipse
        {
            Width = diameter,
            Height = diameter,
            Fill = brush,
        };
        Canvas.SetLeft(ellipse, element.X1 - diameter / 2);
        Canvas.SetTop(ellipse, element.Y1 - diameter / 2);
        return ellipse;
    }

    private static Control BuildText(RibbonIconElement element, IBrush brush)
    {
        var block = new TextBlock
        {
            Text = element.Text,
            Foreground = brush,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = element.Width,
            FontWeight = ToFontWeight(element.TextWeight),
            Width = Artboard - element.X1,
            Height = Artboard - element.Y1,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Canvas.SetLeft(block, element.X1);
        Canvas.SetTop(block, element.Y1);
        return block;
    }

    private static FontWeight ToFontWeight(RibbonIconTextWeight weight) => weight switch
    {
        RibbonIconTextWeight.Bold => FontWeight.Bold,
        RibbonIconTextWeight.SemiBold => FontWeight.SemiBold,
        _ => FontWeight.Normal,
    };
}
