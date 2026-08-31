using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace Free.Shared.Ribbon.Wpf;

/// <summary>
/// Shared SharpVectors-backed command-icon engine. Loads <c>Resources/{resourceFolder}/{slug}.svg</c>
/// vector artwork, caches it per size/brush, optionally recolors it to a monochrome glyph brush, and
/// wraps it in a view-box-normalized drawing whose stroke widths are scaled to the requested pixel size.
///
/// Both FreeX and FreeW hosts independently grew byte-identical copies of this loading/caching/recolor/
/// view-box machinery. The host-specific bits — how a command name maps to a file slug, which alias slugs
/// to try, and how coarse the size cache key is — are supplied as delegates so each host keeps its exact
/// behavior while sharing the engine.
/// </summary>
public sealed class SvgCommandIconLoader
{
    private readonly object _cacheGate = new();
    private readonly Dictionary<string, ImageSource> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _missing = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _resourceFolder;
    private readonly Func<string, string> _slugFromCommandName;
    private readonly Func<string, IEnumerable<string>> _slugCandidates;
    private readonly Func<double, string> _sizeKeySelector;

    private static readonly WpfDrawingSettings SvgDrawingSettings = new()
    {
        IncludeRuntime = false,
        OptimizePath = true,
        TextAsGeometry = true
    };

    /// <param name="resourceFolder">
    /// Folder under <c>AppContext.BaseDirectory/Resources</c> that holds the <c>{slug}.svg</c> files
    /// (e.g. <c>"CommandIconsSvg"</c>).
    /// </param>
    /// <param name="slugFromCommandName">
    /// Converts a raw ribbon command name to its base file slug (trimming any host-specific prefix /
    /// handler suffix and normalizing to lowercase dash form). Returns an empty string when there is no
    /// resolvable slug.
    /// </param>
    /// <param name="slugCandidates">
    /// Expands the base slug to the ordered set of file slugs to try (e.g. the slug itself plus any alias
    /// the host maps it to).
    /// </param>
    /// <param name="sizeKeySelector">
    /// Produces the cache-key component for a requested pixel size. Hosts that re-wrap the vector per exact
    /// size return the rounded pixel size; hosts that bucket into small/large return a coarse token.
    /// </param>
    public SvgCommandIconLoader(
        string resourceFolder,
        Func<string, string> slugFromCommandName,
        Func<string, IEnumerable<string>> slugCandidates,
        Func<double, string> sizeKeySelector)
    {
        _resourceFolder = resourceFolder ?? throw new ArgumentNullException(nameof(resourceFolder));
        _slugFromCommandName = slugFromCommandName ?? throw new ArgumentNullException(nameof(slugFromCommandName));
        _slugCandidates = slugCandidates ?? throw new ArgumentNullException(nameof(slugCandidates));
        _sizeKeySelector = sizeKeySelector ?? throw new ArgumentNullException(nameof(sizeKeySelector));
    }

    /// <summary>
    /// Loads (or returns a cached) frozen <see cref="ImageSource"/> for <paramref name="commandName"/> at
    /// the requested <paramref name="size"/>, or <c>null</c> when no matching artwork exists. When
    /// <paramref name="glyphBrush"/> is a near-white (monochrome) brush the artwork is recolored to it.
    /// </summary>
    public ImageSource? TryLoad(string commandName, Brush glyphBrush, double size)
    {
        var slug = _slugFromCommandName(commandName);
        if (slug.Length == 0)
            return null;

        foreach (var candidateSlug in _slugCandidates(slug))
        {
            var monochromeBrush = IsWhiteBrush(glyphBrush) ? glyphBrush : null;
            var sizeKey = _sizeKeySelector(size);
            foreach (var fileSlug in GetSizeSpecificSlugCandidates(candidateSlug, size))
            {
                var cacheKey = monochromeBrush is null
                    ? $"{fileSlug}|{sizeKey}"
                    : $"{fileSlug}|{sizeKey}|mono|{BrushCacheKey(monochromeBrush)}";
                lock (_cacheGate)
                {
                    if (_cache.TryGetValue(cacheKey, out var cached))
                        return cached;
                    if (_missing.Contains(cacheKey))
                        continue;
                }

                var filePath = Path.Combine(
                    AppContext.BaseDirectory,
                    "Resources",
                    _resourceFolder,
                    fileSlug + ".svg");
                if (!File.Exists(filePath))
                {
                    lock (_cacheGate)
                        _missing.Add(cacheKey);
                    continue;
                }

                using var reader = new FileSvgReader(SvgDrawingSettings);
                var drawing = reader.Read(filePath);
                if (drawing is null)
                {
                    lock (_cacheGate)
                        _missing.Add(cacheKey);
                    continue;
                }

                if (monochromeBrush is not null)
                    RecolorDrawing(drawing, monochromeBrush);

                var vectorImage = new DrawingImage(WrapDrawingInSvgViewBox(drawing, filePath, size));
                vectorImage.Freeze();

                lock (_cacheGate)
                    _cache[cacheKey] = vectorImage;
                return vectorImage;
            }
        }

        return null;
    }

    internal static IEnumerable<string> GetSizeSpecificSlugCandidates(string slug, double size)
    {
        if (size <= 22)
            yield return slug + "-small";
        else
            yield return slug + "-large";

        yield return slug;
    }

    private static bool IsWhiteBrush(Brush brush) =>
        brush is SolidColorBrush solid &&
        solid.Color.R >= 245 &&
        solid.Color.G >= 245 &&
        solid.Color.B >= 245;

    private static string BrushCacheKey(Brush brush) =>
        brush is SolidColorBrush solid
            ? solid.Color.ToString(CultureInfo.InvariantCulture)
            : brush.ToString() ?? "brush";

    private static Drawing WrapDrawingInSvgViewBox(Drawing drawing, string filePath, double targetSize)
    {
        var bounds = TryReadSvgViewBox(filePath) ?? drawing.Bounds;
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
            return drawing;

        var designSize = bounds.Width;
        var scale = targetSize / designSize;

        var mutableDrawing = drawing.IsFrozen ? (Drawing)drawing.Clone() : drawing;
        ScalePenThicknesses(mutableDrawing, 1.0 / scale);

        var normalGroup = new DrawingGroup();
        normalGroup.Children.Add(new GeometryDrawing(
            Brushes.Transparent,
            null,
            new RectangleGeometry(bounds)));
        normalGroup.Children.Add(mutableDrawing);
        normalGroup.Freeze();
        return normalGroup;
    }

    private static void ScalePenThicknesses(Drawing drawing, double factor)
    {
        if (drawing is DrawingGroup group)
        {
            foreach (var child in group.Children)
                ScalePenThicknesses(child, factor);
        }
        else if (drawing is GeometryDrawing geometry && geometry.Pen is { } pen)
        {
            geometry.Pen = new Pen(pen.Brush, pen.Thickness * factor)
            {
                DashCap = pen.DashCap,
                EndLineCap = pen.EndLineCap,
                LineJoin = pen.LineJoin,
                MiterLimit = pen.MiterLimit,
                StartLineCap = pen.StartLineCap,
                DashStyle = pen.DashStyle
            };
        }
    }

    private static void RecolorDrawing(Drawing drawing, Brush brush)
    {
        switch (drawing)
        {
            case DrawingGroup group:
                foreach (var child in group.Children)
                    RecolorDrawing(child, brush);
                break;
            case GeometryDrawing geometry:
                if (geometry.Brush is not null)
                    geometry.Brush = brush;
                if (geometry.Pen is not null)
                    geometry.Pen = new Pen(brush, geometry.Pen.Thickness)
                    {
                        DashCap = geometry.Pen.DashCap,
                        EndLineCap = geometry.Pen.EndLineCap,
                        LineJoin = geometry.Pen.LineJoin,
                        MiterLimit = geometry.Pen.MiterLimit,
                        StartLineCap = geometry.Pen.StartLineCap,
                        DashStyle = geometry.Pen.DashStyle
                    };
                break;
            case GlyphRunDrawing glyph:
                glyph.ForegroundBrush = brush;
                break;
        }
    }

    private static Rect? TryReadSvgViewBox(string filePath)
    {
        try
        {
            var root = XDocument.Load(filePath).Root;
            if (root is null)
                return null;

            var viewBox = root.Attribute("viewBox")?.Value;
            if (!string.IsNullOrWhiteSpace(viewBox))
            {
                var parts = viewBox
                    .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => double.Parse(part, CultureInfo.InvariantCulture))
                    .ToArray();
                if (parts.Length == 4)
                    return new Rect(parts[0], parts[1], parts[2], parts[3]);
            }

            var width = TryParseSvgLength(root.Attribute("width")?.Value);
            var height = TryParseSvgLength(root.Attribute("height")?.Value);
            return width is > 0 && height is > 0
                ? new Rect(0, 0, width.Value, height.Value)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static double? TryParseSvgLength(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var numeric = new string(value
            .Trim()
            .TakeWhile(ch => char.IsDigit(ch) || ch is '.' or '-')
            .ToArray());
        return double.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }
}
