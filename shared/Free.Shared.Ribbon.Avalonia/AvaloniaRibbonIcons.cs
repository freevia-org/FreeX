using System.Collections.Generic;
using System.IO;
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
using AvaloniaImage = Avalonia.Controls.Image;

namespace Free.Shared.Ribbon.Avalonia;

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
public static class AvaloniaRibbonIcons
{
    private const double Artboard = RibbonIconGeometry.Artboard;

    /// <summary>
    /// Letter-glyph font. WPF authors these in "Segoe UI"; Avalonia resolves the first available
    /// family in this comma-separated chain, so Windows still uses Segoe UI while Linux/macOS fall
    /// back to the closest metrically/visually similar neutral sans, keeping the glyph faithful.
    /// </summary>
    private static readonly FontFamily GlyphFontFamily =
        new("Segoe UI, Selawik, Liberation Sans, DejaVu Sans, Arial, Helvetica, sans-serif");

    /// <summary>
    /// Per-command SVG glyphs live next to the app under <c>Resources/CommandIconsSvg/&lt;slug&gt;.svg</c>
    /// — the SAME copy the WPF host loads (shared via the FreeX.Ribbon.Definitions project). Parsed
    /// <see cref="DrawingImage"/>s are cached by file slug because SVG parse is not free.
    /// </summary>
    private static readonly string CommandIconsDirectory =
        Path.Combine(AppContext.BaseDirectory, "Resources", "CommandIconsSvg");

    private static readonly object CommandIconCacheGate = new();
    private static readonly Dictionary<string, DrawingImage?> CommandIconCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, DrawingImage?> CommandIconMonochromeCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Builds an icon control for the given kind at the requested pixel size.</summary>
    public static Control Build(RibbonCommandIconKind kind, double size) =>
        Build(kind, size, commandName: null);

    /// <summary>
    /// Builds an icon for the given kind at the requested pixel size, preferring the per-command SVG
    /// glyph resolved from <paramref name="commandName"/> (matching the WPF host's
    /// <c>RibbonIconFactory.CreateCommandIcon</c>). When no command name is supplied, or no SVG file
    /// resolves for it, falls back to the platform-neutral kind glyph — identical to WPF's fallback.
    /// The SVGs are full-colour Excel-style glyphs, so they render as-authored (no tinting), exactly as
    /// WPF renders them on the white ribbon surface.
    /// </summary>
    public static Control Build(RibbonCommandIconKind kind, double size, string? commandName)
    {
        if (TryBuildCommandSvg(commandName, size, monochromeForeground: null) is { } svg)
            return svg;

        return Build(new RibbonCommandIcon(kind), size, foreground: null);
    }

    /// <summary>Builds an icon with explicit kind/accent metadata and optional per-command artwork.</summary>
    public static Control Build(RibbonCommandIcon icon, double size, string? commandName)
    {
        if (TryBuildCommandSvg(commandName, size, monochromeForeground: null) is { } svg)
            return svg;

        return Build(icon, size, foreground: null);
    }

    /// <summary>
    /// Builds a command icon from the shared SVG asset and recolours its visible paint to
    /// <paramref name="foreground"/>. This is used by dark chrome such as the Office backstage rail,
    /// matching the WPF SVG loader's white glyph path while still reusing the exact same artwork.
    /// </summary>
    public static Control BuildMonochrome(RibbonCommandIconKind kind, double size, string? commandName, IBrush foreground)
    {
        if (TryBuildCommandSvg(commandName, size, foreground) is { } svg)
            return svg;

        return Build(new RibbonCommandIcon(kind), size, foreground);
    }

    /// <summary>
    /// Resolves and renders the per-command SVG for <paramref name="commandName"/> at the requested
    /// size, or returns <see langword="null"/> when the command has no matching SVG file (so the caller
    /// falls back to the kind glyph). Slug derivation and the candidate order (size-specific
    /// <c>-small</c>/<c>-large</c> variants for each shared policy candidate) mirror the WPF host in
    /// <c>RibbonIconFactory.Svg.cs</c> so the filenames resolve identically.
    /// </summary>
    private static Control? TryBuildCommandSvg(string? commandName, double size, IBrush? monochromeForeground)
    {
        if (string.IsNullOrWhiteSpace(commandName))
            return null;

        var slug = RibbonCommandIconPolicy.ToCommandIconSlug(
            RibbonCommandIconPolicy.NormalizeCommandIconName(commandName));
        if (slug.Length == 0)
            return null;

        foreach (var candidateSlug in RibbonCommandIconPolicy.GetCommandIconSlugCandidates(slug))
        {
            foreach (var fileSlug in GetSizeSpecificSlugCandidates(candidateSlug, size))
            {
                if (LoadCommandSvg(fileSlug, monochromeForeground) is { } image)
                {
                    return new AvaloniaImage
                    {
                        Source = image,
                        Width = size,
                        Height = size,
                        Stretch = Stretch.Uniform,
                    };
                }
            }
        }

        return null;
    }

    // Parses (and caches by file slug) the per-command SVG into a native Avalonia DrawingImage via the
    // in-house SvgIconParser — no external SVG library, so it renders through Avalonia's own software-safe
    // pipeline. A missing file or a parse failure caches null so the caller falls back to the kind glyph.
    private static DrawingImage? LoadCommandSvg(string fileSlug, IBrush? monochromeForeground)
    {
        if (monochromeForeground is { } foreground)
            return LoadMonochromeCommandSvg(fileSlug, foreground);

        lock (CommandIconCacheGate)
        {
            if (CommandIconCache.TryGetValue(fileSlug, out var cached))
                return cached;
        }

        var filePath = Path.Combine(CommandIconsDirectory, fileSlug + ".svg");
        DrawingImage? image = null;
        if (File.Exists(filePath))
            image = SvgIconParser.TryParseFile(filePath);

        lock (CommandIconCacheGate)
            CommandIconCache[fileSlug] = image;
        return image;
    }

    private static DrawingImage? LoadMonochromeCommandSvg(string fileSlug, IBrush foreground)
    {
        var cacheKey = fileSlug + "|" + GetBrushCacheKey(foreground);
        lock (CommandIconCacheGate)
        {
            if (CommandIconMonochromeCache.TryGetValue(cacheKey, out var cached))
                return cached;
        }

        var filePath = Path.Combine(CommandIconsDirectory, fileSlug + ".svg");
        DrawingImage? image = null;
        if (File.Exists(filePath))
            image = SvgIconParser.TryParseFile(filePath, foreground);

        lock (CommandIconCacheGate)
            CommandIconMonochromeCache[cacheKey] = image;
        return image;
    }

    private static string GetBrushCacheKey(IBrush brush) =>
        brush is ISolidColorBrush solid
            ? $"#{solid.Color.A:X2}{solid.Color.R:X2}{solid.Color.G:X2}{solid.Color.B:X2}"
            : brush.GetType().FullName ?? "brush";

    // Mirrors RibbonIconFactory.Svg.cs GetSizeSpecificSlugCandidates: prefer a size-specific variant
    // (small ≤ 22px, otherwise large), then fall back to the bare slug.
    private static IEnumerable<string> GetSizeSpecificSlugCandidates(string slug, double size)
    {
        yield return size <= 22 ? slug + "-small" : slug + "-large";
        yield return slug;
    }

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
