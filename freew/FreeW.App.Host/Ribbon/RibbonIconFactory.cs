using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Xml.Linq;
using Free.Shared.Ribbon.Icons;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace FreeW.App.Host;

internal static class RibbonIconFactory
{
    private const double Artboard = RibbonIconGeometry.Artboard;
    private static readonly object CommandIconCacheGate = new();
    private static readonly Dictionary<string, ImageSource> CommandIconCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> MissingCommandIcons = new(StringComparer.OrdinalIgnoreCase);
    private static readonly WpfDrawingSettings SvgDrawingSettings = new()
    {
        IncludeRuntime = false,
        OptimizePath = true,
        TextAsGeometry = true
    };

    public static FrameworkElement CreateCommandIcon(
        string commandName,
        RibbonCommandIcon fallbackIcon,
        double size,
        Brush glyphBrush)
    {
        if (TryLoadCommandIcon(commandName, glyphBrush, size) is { } source)
        {
            return new Image
            {
                Source = source,
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                SnapsToDevicePixels = true,
                UseLayoutRounding = true
            };
        }

        return CreateIcon(fallbackIcon, size, glyphBrush);
    }

    private static ImageSource? TryLoadCommandIcon(string commandName, Brush glyphBrush, double size)
    {
        var slug = ToCommandIconSlug(commandName);
        if (slug.Length == 0)
            return null;

        foreach (var candidateSlug in GetCommandIconSlugCandidates(slug))
        {
            var monochromeBrush = IsWhiteBrush(glyphBrush) ? glyphBrush : null;
            var sizeKey = size <= 22 ? "s" : "l";
            foreach (var fileSlug in GetSizeSpecificSlugCandidates(candidateSlug, size))
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

    private static IEnumerable<string> GetSizeSpecificSlugCandidates(string slug, double size)
    {
        if (size <= 22)
            yield return slug + "-small";
        else
            yield return slug + "-large";

        yield return slug;
    }

    private static IEnumerable<string> GetCommandIconSlugCandidates(string slug)
    {
        yield return slug;

        var alias = slug switch
        {
            "align-center" => "center",
            "orientation" => "page-orientation",
            "size" => "paper-size",
            "paper-size" => "paper-size",
            "style-normal" => "normal",
            "normal-style" => "normal",
            "style-heading1" => "heading-1",
            "heading-1-style" => "heading-1",
            "style-heading2" => "heading-2",
            "heading-2-style" => "heading-2",
            "style-title" => "title",
            "title-style" => "title",
            "bullet-list" => "bullets",
            "bulleted-list" => "bullets",
            "numbered-list" => "numbering",
            "page-break-insert" => "page-break",
            "blank-page-insert" => "blank-page",
            "cover-page-insert" => "cover-page",
            "pictures" => "picture",
            "table-insert" => "table",
            "table-of-contents-gallery" => "table-of-contents",
            "footnote-insert" => "footnote",
            "endnote-insert" => "endnote",
            "citation-insert" => "citation",
            "bibliography-gallery" => "bibliography",
            "caption-insert" => "caption",
            "index-insert" => "index",
            "mail-merge" => "mail-merge",
            "track-changes" => "track-changes",
            "accept-change" => "accept-change",
            "reject-change" => "reject-change",
            "word-count" => "word-count",
            _ => ""
        };

        if (alias.Length > 0 && !string.Equals(alias, slug, StringComparison.Ordinal))
            yield return alias;
    }

    private static string ToCommandIconSlug(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var lower = text
            .Trim()
            .ToLowerInvariant()
            .Replace("&amp;", "and", StringComparison.Ordinal)
            .Replace("&", "and", StringComparison.Ordinal);
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

    private static Drawing WrapDrawingInSvgViewBox(Drawing drawing, string filePath, double targetSize)
    {
        var bounds = TryReadSvgViewBox(filePath) ?? drawing.Bounds;
        if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
            return drawing;

        var scale = targetSize / bounds.Width;
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
                {
                    geometry.Pen = new Pen(brush, geometry.Pen.Thickness)
                    {
                        DashCap = geometry.Pen.DashCap,
                        EndLineCap = geometry.Pen.EndLineCap,
                        LineJoin = geometry.Pen.LineJoin,
                        MiterLimit = geometry.Pen.MiterLimit,
                        StartLineCap = geometry.Pen.StartLineCap,
                        DashStyle = geometry.Pen.DashStyle
                    };
                }
                break;
            case GlyphRunDrawing glyph:
                glyph.ForegroundBrush = brush;
                break;
        }
    }

    private static FrameworkElement CreateIcon(RibbonCommandIcon icon, double size, Brush glyphBrush)
    {
        var canvas = new Canvas
        {
            Width = Artboard,
            Height = Artboard,
            SnapsToDevicePixels = true
        };
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

    private static bool IsWhiteBrush(Brush brush) =>
        brush is SolidColorBrush solid &&
        solid.Color.R >= 245 &&
        solid.Color.G >= 245 &&
        solid.Color.B >= 245;

    private static string BrushCacheKey(Brush brush) =>
        brush is SolidColorBrush solid ? solid.Color.ToString(CultureInfo.InvariantCulture) : brush.ToString() ?? "brush";

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
