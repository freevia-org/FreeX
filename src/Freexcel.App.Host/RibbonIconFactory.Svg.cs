using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace Freexcel.App.Host;

public static partial class RibbonIconFactory
{
    private static readonly object CommandIconCacheGate = new();
    private static readonly Dictionary<string, ImageSource> CommandIconCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> MissingCommandIcons = new(StringComparer.OrdinalIgnoreCase);
    private static readonly WpfDrawingSettings SvgDrawingSettings = new()
    {
        IncludeRuntime = false,
        OptimizePath = true,
        TextAsGeometry = true
    };

    private static ImageSource? TryLoadCommandIcon(string commandName, Brush glyphBrush, double size)
    {
        var slug = ToCommandIconSlug(commandName);
        if (slug.Length == 0)
            return null;

        foreach (var candidateSlug in GetCommandIconSlugCandidates(slug))
        {
            var monochromeBrush = IsWhiteBrush(glyphBrush) ? glyphBrush : null;
            var sizeKey = size <= 22 ? "s" : "l";
            foreach (var fileSlug in GetSizeSpecificSlugCandidates(candidateSlug, size, monochromeBrush is not null))
            {
                var cacheKey = monochromeBrush is null
                    ? $"{fileSlug}|{sizeKey}"
                    : $"{fileSlug}|{sizeKey}|mono|{BrushCacheKey(monochromeBrush)}";
                lock (CommandIconCacheGate)
                {
                    if (CommandIconCache.TryGetValue(cacheKey, out var cached))
                        return cached;
                    if (MissingCommandIcons.Contains(cacheKey))
                        continue;
                }

                var filePath = System.IO.Path.Combine(
                    AppContext.BaseDirectory,
                    "Resources",
                    "CommandIconsSvg",
                    fileSlug + ".svg");
                if (!File.Exists(filePath))
                {
                    lock (CommandIconCacheGate)
                        MissingCommandIcons.Add(cacheKey);
                    continue;
                }

                using var reader = new FileSvgReader(SvgDrawingSettings);
                var drawing = reader.Read(filePath);
                if (drawing is null)
                {
                    lock (CommandIconCacheGate)
                        MissingCommandIcons.Add(cacheKey);
                    continue;
                }

                if (monochromeBrush is not null)
                    RecolorDrawing(drawing, monochromeBrush);

                var vectorImage = new DrawingImage(WrapDrawingInSvgViewBox(drawing, filePath, size));
                vectorImage.Freeze();

                lock (CommandIconCacheGate)
                    CommandIconCache[cacheKey] = vectorImage;
                return vectorImage;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetSizeSpecificSlugCandidates(string slug, double size, bool monochrome)
    {
        if (size <= 22)
            yield return slug + "-small";
        else
            yield return slug + "-large";

        yield return slug;
    }

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
                    .Select(part => double.Parse(part, System.Globalization.CultureInfo.InvariantCulture))
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
        return double.TryParse(numeric, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private static string BrushCacheKey(Brush brush) =>
        brush is SolidColorBrush solid
            ? solid.Color.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : brush.ToString() ?? "brush";

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

    private static IEnumerable<string> GetCommandIconSlugCandidates(string slug)
    {
        yield return slug;

        var alias = slug switch
        {
            "increase-font-size" => "grow-font",
            "decrease-font-size" => "shrink-font",
            "accounting-number-format" => "accounting-currency",
            "increase-decimal-places" => "increase-decimal",
            "decrease-decimal-places" => "decrease-decimal",
            "merge-and-center" => "merge-center",
            "sort-and-filter" => "sort",
            "find-and-select" => "find",
            "percent-style" => "percent-style",
            "object-fill" => "fill",
            "object-outline" => "outline-color",
            "object-size" => "size",
            "object-rotate" => "rotate",
            "shape-gradient" => "gradient",
            "object-effects" => "effects",
            "math" => "math-trig",
            "date" => "date-time",
            "lookup" => "lookup-reference",
            "formula-auditing" => "evaluate-formula",
            "calculation" => "calculate-now",
            "workbook-stats" => "statistics",
            "workbook-statistics" => "statistics",
            "accessibility" => "accessibility-checker",
            "refresh-pivot" => "refresh-all",
            "show-details" => "show-detail",
            "links-and-objects" => "hyperlink",
            "help-online" => "help",
            "about-freexcel" => "about",
            _ => ""
        };

        if (alias.Length > 0 && !string.Equals(alias, slug, StringComparison.Ordinal))
            yield return alias;
    }

    private static string ToCommandIconSlug(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var lower = text.Trim().ToLowerInvariant().Replace("&amp;", "and", StringComparison.Ordinal);
        var builder = new System.Text.StringBuilder(lower.Length);
        var pendingDash = false;

        foreach (var ch in lower)
        {
            if (ch is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                if (pendingDash && builder.Length > 0)
                    builder.Append('-');
                builder.Append(ch);
                pendingDash = false;
            }
            else
            {
                pendingDash = builder.Length > 0;
            }
        }

        return builder.ToString().Trim('-');
    }

}
