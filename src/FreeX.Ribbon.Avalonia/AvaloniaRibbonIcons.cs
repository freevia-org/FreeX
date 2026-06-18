using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Free.Shared.Ribbon.Icons;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;
using AvaloniaLine = Avalonia.Controls.Shapes.Line;
using AvaloniaRectangle = Avalonia.Controls.Shapes.Rectangle;
using AvaloniaEllipse = Avalonia.Controls.Shapes.Ellipse;

namespace FreeX.Ribbon.Avalonia;

/// <summary>
/// Avalonia ribbon icon renderer. Draws the SAME glyph as the WPF <c>RibbonIconFactory</c> for every
/// <see cref="RibbonCommandIconKind"/>.
/// </summary>
/// <remarks>
/// <para>
/// Both the WPF renderer and this Avalonia renderer consume the single platform-neutral source of
/// truth, <see cref="RibbonIconDefinitions"/>, which transcribes each WPF glyph 1:1 (the same SVG
/// path strings, the same line/rectangle/ellipse/text primitives, the same accent colours). This
/// renderer therefore reuses the WPF path-data verbatim (via <see cref="Geometry.Parse"/>, which
/// accepts the identical SVG path mini-language WPF's <c>Geometry.Parse</c> uses) and replicates the
/// WPF primitive composition shape-for-shape so the two platforms paint identical icons.
/// </para>
/// <para>
/// <b>Coordinate space / scaling.</b> Every element is authored on a
/// <see cref="RibbonIconGeometry.Artboard"/>-by-<see cref="RibbonIconGeometry.Artboard"/> (24×24)
/// design square — the same artboard the WPF factory draws on. The elements are laid out on a 24×24
/// <see cref="Canvas"/> and a <see cref="Viewbox"/> uniformly scales that artboard into the requested
/// pixel <c>size</c>, exactly mirroring WPF's <c>Viewbox</c> wrapper in <c>RibbonIconFactory.CreateIcon</c>.
/// </para>
/// <para>
/// <b>Translation fidelity.</b> Each native-shape mapping is matched to the corresponding WPF helper
/// in <c>RibbonIconFactory.Primitives.cs</c>: lines get round end caps; paths get round caps + round
/// joins; stroked rectangles use WPF's fixed 1.5 stroke; a filled shape path is stroked and faintly
/// filled with the whole element dimmed by its fill opacity.
/// </para>
/// <para>
/// <b>Text glyphs.</b> WPF letter glyphs (Bold "B", Italic "I", "$", "%", "fx", "π", "Ω", "¶", …) are
/// authored in "Segoe UI". Avalonia honours a comma-separated family fallback, so we request Segoe UI
/// first and then the closest cross-platform equivalents (so the same glyph renders on Linux/macOS
/// where Segoe UI is absent), keeping the letter shapes as faithful to Windows as the host fonts allow.
/// </para>
/// </remarks>
internal static class AvaloniaRibbonIcons
{
    private const double Artboard = RibbonIconGeometry.Artboard;

    /// <summary>
    /// Letter-glyph font. WPF authors these in "Segoe UI"; Avalonia resolves the first available
    /// family in this comma-separated chain, so Windows still uses Segoe UI while Linux/macOS fall
    /// back to the closest metrically/visually similar neutral sans, keeping the glyph faithful.
    /// </summary>
    private static readonly FontFamily GlyphFontFamily =
        new("Segoe UI, Selawik, Liberation Sans, DejaVu Sans, Arial, Helvetica, sans-serif");

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

    /// <summary>
    /// Resolves the brush an icon should be drawn with. Mirrors WPF
    /// <c>RibbonIconFactory.ResolveAccentBrush</c>: any accent other than
    /// <see cref="RibbonCommandIconAccent.None"/> maps to its neutral accent colour; otherwise the
    /// caller-supplied foreground (or black) wins.
    /// </summary>
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

    // Mirrors WPF RibbonIconFactory.AddPath: stroked with round caps + round joins; when a fill
    // opacity is present the path is also filled with the glyph brush and the whole element is dimmed
    // by that opacity (so both the stroke and the fill read as a faint, semi-transparent shape).
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
            path.Fill = brush;
            path.Opacity = element.FillOpacity;
        }
        else
        {
            path.Fill = Brushes.Transparent;
        }

        return path;
    }

    // Mirrors WPF RibbonIconFactory.AddLine: straight stroke with round end caps; optional 2,2 dash.
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

    // Mirrors WPF RibbonIconFactory.AddRectangle: a transparent-filled, stroked rounded rect. WPF
    // hard-codes the 1.5 stroke there, and every Rectangle element is authored with a 1.5 stroke, so
    // we use the same fixed 1.5 to stay pixel-faithful.
    private static Control BuildRectangle(RibbonIconElement element, IBrush brush)
    {
        var rect = new AvaloniaRectangle
        {
            Width = element.Width,
            Height = element.Height,
            RadiusX = element.Radius,
            RadiusY = element.Radius,
            Stroke = brush,
            StrokeThickness = 1.5,
            Fill = Brushes.Transparent,
        };
        Canvas.SetLeft(rect, element.X1);
        Canvas.SetTop(rect, element.Y1);
        return rect;
    }

    // Mirrors WPF RibbonIconFactory.AddFilledRectangle: a solid-filled rect placed at (X,Y).
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

    // Mirrors WPF RibbonIconFactory.DrawEllipse: a transparent-filled, stroked ellipse placed at (X,Y).
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

    // Mirrors WPF RibbonIconFactory.AddFilledCircle: a solid disc of the given diameter centred at
    // (cx,cy) — i.e. positioned at the centre minus half the diameter.
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

    // Mirrors WPF RibbonIconFactory.DrawText: a centred TextBlock filling the remaining artboard from
    // (X,Y), with the glyph's font size (carried in Width) and weight. The family is the Segoe-UI-first
    // fallback chain so the same letter shape renders cross-platform.
    private static Control BuildText(RibbonIconElement element, IBrush brush)
    {
        var block = new TextBlock
        {
            Text = element.Text,
            Foreground = brush,
            FontFamily = GlyphFontFamily,
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
