using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Icons;

namespace Free.Shared.Ribbon.Wpf;

/// <summary>
/// Draws ribbon icons from the shared, platform-neutral <see cref="RibbonIconDefinitions"/> geometry.
///
/// Ported from FreeX's <c>RibbonIconFactory</c>, but with FreeX's SVG/SharpVectors command-icon loader
/// removed: that path depended on FreeX-bundled <c>Resources/CommandIconsSvg/*.svg</c> files and the
/// SharpVectors NuGet package, neither of which is app-neutral. In its place, a command id is mapped to
/// a <see cref="RibbonCommandIconKind"/> through <see cref="CommandIconKindResolver"/> (set by the host
/// app) and the matching shared geometry is drawn. This keeps the shared library free of external
/// dependencies while every host app can still supply a glyph per command id.
/// </summary>
public static class RibbonIconFactory
{
    private const double Artboard = RibbonIconGeometry.Artboard;

    /// <summary>
    /// Optional host-supplied map from a ribbon command id (e.g. <c>"freew.bold"</c>) to the icon kind
    /// to draw. Returns <c>null</c> for an unmapped id (the caller then falls back to the control's own
    /// icon, or the generic glyph). FreeW installs a resolver for its <c>freew.*</c> command ids.
    /// </summary>
    public static Func<string, RibbonCommandIconKind?>? CommandIconKindResolver { get; set; }

    /// <summary>
    /// Optional host-supplied command artwork loader. Hosts that carry app-local SVG/raster assets can
    /// return a ready-to-render element here; returning <c>null</c> keeps the shared geometry fallback.
    /// </summary>
    public static Func<string, RibbonCommandIcon, double, Brush, FrameworkElement?>? CommandIconElementResolver { get; set; }

    /// <summary>
    /// Builds the icon for a command. Resolution order: an explicit kind carried by the control's own
    /// app-local asset wins; otherwise an explicit kind carried by the control's own icon wins; otherwise
    /// the host resolver maps the command id to a kind; otherwise the supplied fallback icon is used
    /// (generic when nothing else applies).
    /// </summary>
    public static FrameworkElement CreateCommandIcon(
        string commandName,
        RibbonCommandIcon fallbackIcon,
        double size,
        Brush glyphBrush)
    {
        if (!string.IsNullOrWhiteSpace(commandName) &&
            CommandIconElementResolver?.Invoke(commandName, fallbackIcon, size, glyphBrush) is { } appIcon)
        {
            return appIcon;
        }

        if (fallbackIcon.Kind == RibbonCommandIconKind.Generic &&
            !string.IsNullOrWhiteSpace(commandName) &&
            CommandIconKindResolver?.Invoke(commandName) is { } resolved)
        {
            return CreateIcon(new RibbonCommandIcon(resolved), size, glyphBrush);
        }

        return CreateIcon(fallbackIcon, size, glyphBrush);
    }

    private static bool IsWhiteBrush(Brush brush) =>
        brush is SolidColorBrush solid &&
        solid.Color.R >= 245 &&
        solid.Color.G >= 245 &&
        solid.Color.B >= 245;

    /// <summary>
    /// Renders the shared, platform-neutral baseline icon for the given kind. The geometry comes from
    /// <see cref="RibbonIconDefinitions"/> (the single cross-platform source of truth) so the icons
    /// match FreeX's WPF renderer exactly.
    /// </summary>
    public static FrameworkElement CreateIcon(RibbonCommandIcon icon, double size, Brush glyphBrush)
    {
        var canvas = CreateCanvas();
        var geometry = RibbonIconDefinitions.Resolve(icon.Kind);
        var brush = ResolveAccentBrush(icon.Accent, glyphBrush);

        foreach (var element in geometry.Elements)
            DrawElement(canvas, element, brush);

        return new Viewbox
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            Child = canvas,
            SnapsToDevicePixels = true
        };
    }

    private static Brush ResolveAccentBrush(RibbonCommandIconAccent accent, Brush glyphBrush)
    {
        if (IsWhiteBrush(glyphBrush))
            return glyphBrush;

        if (RibbonIconAccents.Resolve(accent) is { } color)
        {
            var solid = new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B));
            solid.Freeze();
            return solid;
        }

        return glyphBrush;
    }

    private static void DrawElement(Canvas canvas, RibbonIconElement element, Brush brush)
    {
        switch (element.Kind)
        {
            case RibbonIconElementKind.Path:
                if (element.FillOpacity > 0)
                    AddPath(canvas, element.PathData!, brush, element.StrokeThickness, brush, element.FillOpacity);
                else
                    AddPath(canvas, element.PathData!, brush, element.StrokeThickness);
                break;
            case RibbonIconElementKind.Line:
                AddLine(canvas, element.X1, element.Y1, element.X2, element.Y2, brush, element.StrokeThickness, element.Dashed);
                break;
            case RibbonIconElementKind.Rectangle:
                AddRectangle(canvas, element.X1, element.Y1, element.Width, element.Height, brush, element.Radius);
                break;
            case RibbonIconElementKind.FilledRectangle:
                AddFilledRectangle(canvas, element.X1, element.Y1, element.Width, element.Height, brush);
                break;
            case RibbonIconElementKind.Ellipse:
                DrawEllipse(canvas, element.X1, element.Y1, element.Width, element.Height, brush, element.StrokeThickness);
                break;
            case RibbonIconElementKind.FilledCircle:
                AddFilledCircle(canvas, element.X1, element.Y1, element.Width, brush);
                break;
            case RibbonIconElementKind.Text:
                DrawText(canvas, element.Text!, brush, element.Width, ToFontWeight(element.TextWeight), element.X1, element.Y1);
                break;
        }
    }

    private static FontWeight ToFontWeight(RibbonIconTextWeight weight) => weight switch
    {
        RibbonIconTextWeight.Bold => FontWeights.Bold,
        RibbonIconTextWeight.SemiBold => FontWeights.SemiBold,
        _ => FontWeights.Normal,
    };

    private static Canvas CreateCanvas() => new()
    {
        Width = Artboard,
        Height = Artboard,
        SnapsToDevicePixels = true
    };

    // ---- Primitive drawing helpers (ported from RibbonIconFactory.Primitives) ----

    private static void DrawText(Canvas canvas, string text, Brush brush, double fontSize, FontWeight weight, double x = 0, double y = 0)
    {
        var block = new TextBlock
        {
            Text = text,
            Foreground = brush,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = fontSize,
            FontWeight = weight,
            Width = Artboard - x,
            Height = Artboard - y,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        Canvas.SetLeft(block, x);
        Canvas.SetTop(block, y);
        canvas.Children.Add(block);
    }

    private static void AddRectangle(Canvas canvas, double x, double y, double width, double height, Brush brush, double radius = 0)
    {
        var rectangle = new System.Windows.Shapes.Rectangle
        {
            Width = width,
            Height = height,
            RadiusX = radius,
            RadiusY = radius,
            Stroke = brush,
            StrokeThickness = 1.5,
            Fill = Brushes.Transparent,
            SnapsToDevicePixels = true
        };
        Canvas.SetLeft(rectangle, x);
        Canvas.SetTop(rectangle, y);
        canvas.Children.Add(rectangle);
    }

    private static void AddFilledRectangle(Canvas canvas, double x, double y, double width, double height, Brush brush)
    {
        var rectangle = new System.Windows.Shapes.Rectangle
        {
            Width = width,
            Height = height,
            Fill = brush,
            SnapsToDevicePixels = true
        };
        Canvas.SetLeft(rectangle, x);
        Canvas.SetTop(rectangle, y);
        canvas.Children.Add(rectangle);
    }

    private static void DrawEllipse(Canvas canvas, double x, double y, double width, double height, Brush brush, double thickness)
    {
        var ellipse = new Ellipse
        {
            Width = width,
            Height = height,
            Stroke = brush,
            StrokeThickness = thickness,
            Fill = Brushes.Transparent
        };
        Canvas.SetLeft(ellipse, x);
        Canvas.SetTop(ellipse, y);
        canvas.Children.Add(ellipse);
    }

    private static void AddFilledCircle(Canvas canvas, double centerX, double centerY, double diameter, Brush brush)
    {
        var ellipse = new Ellipse
        {
            Width = diameter,
            Height = diameter,
            Fill = brush
        };
        Canvas.SetLeft(ellipse, centerX - diameter / 2);
        Canvas.SetTop(ellipse, centerY - diameter / 2);
        canvas.Children.Add(ellipse);
    }

    private static void AddLine(
        Canvas canvas,
        double x1,
        double y1,
        double x2,
        double y2,
        Brush brush,
        double thickness = 1.5,
        bool dash = false)
    {
        var line = new System.Windows.Shapes.Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = brush,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        if (dash)
            line.StrokeDashArray = new DoubleCollection { 2, 2 };
        canvas.Children.Add(line);
    }

    private static void AddPath(Canvas canvas, string data, Brush brush, double thickness, Brush? fill = null, double fillOpacity = 1)
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(data),
            Stroke = brush,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Fill = fill ?? Brushes.Transparent,
            Opacity = fill is null ? 1 : fillOpacity
        };
        canvas.Children.Add(path);
    }
}
