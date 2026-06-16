using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Free.Shared.Ribbon.Icons;

namespace FreeX.App.Host;

public static partial class RibbonIconFactory
{
    private const double Artboard = RibbonIconGeometry.Artboard;

    public static int ResolveCommandIconPixelSizeForDpi(double logicalSize, double dpiScale)
    {
        if (double.IsNaN(logicalSize) || double.IsInfinity(logicalSize) || logicalSize <= 0 ||
            double.IsNaN(dpiScale) || double.IsInfinity(dpiScale) || dpiScale <= 0)
        {
            return 1;
        }

        return Math.Max(1, (int)Math.Round(logicalSize * dpiScale, MidpointRounding.AwayFromZero));
    }

    public static FrameworkElement CreateCommandIcon(
        string commandName,
        RibbonCommandIcon fallbackIcon,
        double size,
        Brush glyphBrush)
    {
        if (TryLoadCommandIcon(commandName, glyphBrush, size) is { } source)
        {
            var image = new Image
            {
                Source = source,
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                SnapsToDevicePixels = true,
                UseLayoutRounding = true
            };
            return image;
        }

        return CreateIcon(fallbackIcon, size, glyphBrush);
    }

    private static bool IsWhiteBrush(Brush brush)
    {
        return brush is SolidColorBrush solid &&
               solid.Color.R >= 245 &&
               solid.Color.G >= 245 &&
               solid.Color.B >= 245;
    }

    /// <summary>
    /// Renders the shared, platform-neutral baseline icon for the given kind. The geometry comes
    /// from <see cref="RibbonIconDefinitions"/> (the single cross-platform source of truth) so the
    /// WPF and Avalonia renderers draw the same shapes.
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

    /// <summary>
    /// Resolves the brush an icon should be drawn with. <see cref="RibbonCommandIconAccent.None"/>
    /// uses the caller-supplied glyph brush; any other accent maps to its neutral accent color —
    /// except when the caller wants a monochrome (e.g. white-on-dark) glyph, where the glyph brush wins.
    /// </summary>
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
}
