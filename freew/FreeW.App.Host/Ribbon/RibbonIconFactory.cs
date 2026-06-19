using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Free.Shared.Ribbon.Icons;
using Free.Shared.Ribbon.Wpf;

namespace FreeW.App.Host;

internal static class RibbonIconFactory
{
    private const double Artboard = RibbonIconGeometry.Artboard;
    private static readonly SvgCommandIconLoader CommandIconLoader = new(
        resourceFolder: "CommandIconsSvg",
        slugFromCommandName: ToCommandIconSlug,
        slugCandidates: GetCommandIconSlugCandidates,
        sizeKeySelector: size => size <= 22 ? "s" : "l");

    public static FrameworkElement CreateCommandIcon(
        string commandName,
        RibbonCommandIcon fallbackIcon,
        double size,
        Brush glyphBrush)
    {
        return TryCreateCommandIcon(commandName, fallbackIcon, size, glyphBrush)
            ?? CreateIcon(fallbackIcon, size, glyphBrush);
    }

    public static FrameworkElement? TryCreateCommandIcon(
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

        return null;
    }

    private static ImageSource? TryLoadCommandIcon(string commandName, Brush glyphBrush, double size) =>
        CommandIconLoader.TryLoad(commandName, glyphBrush, size);

    private static IEnumerable<string> GetCommandIconSlugCandidates(string slug)
    {
        // Prefer the alias FIRST: where FreeW maps a command to an existing FreeX/Word icon, we want that
        // icon to win even when a same-named (but different-meaning) file is also present — e.g. "size"
        // (page size) must resolve to paper-size, not FreeX's cell-size "size". The bare slug is the
        // fallback, so direct FreeX names (bold, paste, …) still resolve straight through.
        if (CommandIconAliases.TryGetValue(slug, out var alias) &&
            !string.Equals(alias, slug, StringComparison.Ordinal))
            yield return alias;

        yield return slug;
    }

    /// <summary>
    /// Maps a FreeW ribbon command slug to the slug of an existing FreeX/Word icon, so commands that have
    /// a FreeX equivalent render FreeX's actual artwork (linked into the build) rather than a redrawn copy.
    /// Word-specific commands with no FreeX equivalent are absent here and resolve to their own bundled SVG.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> CommandIconAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Page setup concepts that collide with a different-meaning FreeX icon (alias must win).
            ["orientation"] = "page-orientation",
            ["size"] = "paper-size",
            // Font
            ["font-family"] = "fonts",
            ["font-size"] = "fonts",
            ["highlight"] = "highlighter",
            ["clear-formatting"] = "clear",
            // Paragraph
            ["align-center"] = "center",
            ["align-justify"] = "distributed-justify",
            ["indent-increase"] = "increase-indent",
            ["indent-decrease"] = "decrease-indent",
            ["multilevel-list"] = "numbering",
            ["merge-cells"] = "merge-center",
            ["para-border"] = "borders",
            ["para-shading"] = "fill-color",
            ["page-valign"] = "middle-align",
            // Styles
            ["style"] = "styles",
            ["manage-styles"] = "styles",
            ["style-normal"] = "normal",
            ["style-heading1"] = "heading-1",
            ["style-heading2"] = "heading-2",
            ["style-title"] = "title",
            // Insert
            ["horizontal-rule"] = "line",
            ["cell-shading"] = "fill-color",
            ["shape-ellipse"] = "ellipse",
            ["shape-rectangle"] = "rectangle",
            ["shape-rounded"] = "rectangle",
            ["shape-textbox"] = "text-box",
            ["chart"] = "column-chart",
            ["datetime"] = "date-time",
            ["index-mark"] = "index",
            ["toc-refresh"] = "refresh-all",
            ["tof-refresh"] = "refresh-all",
            ["citation-style"] = "citation",
            ["insert-file"] = "insert",
            ["insert-quickpart"] = "insert",
            ["save-quickpart"] = "save",
            ["field"] = "insert-function",
            ["object"] = "insert",
            ["image-align-left"] = "align-left",
            ["image-align-center"] = "center",
            ["image-align-right"] = "align-right",
            ["image-alt-text"] = "alt-text",
            ["image-size"] = "size",
            ["hyperlink-tooltip"] = "comment-note",
            ["edit-hyperlink"] = "hyperlink",
            ["remove-hyperlink"] = "hyperlink",
            ["link-bookmark"] = "hyperlink",
            // Layout
            ["print-layout"] = "page-layout",
            ["print-preview"] = "print",
            ["page-border"] = "borders",
            ["restrict-editing"] = "protect-sheet",
            // Mailings
            ["merge-data"] = "mail-merge",
            ["merge-field"] = "mail-merge",
            ["merge-finish"] = "mail-merge",
            ["merge-preview"] = "mail-merge",
            // Design / View / Review
            ["theme"] = "themes",
            ["spellcheck-toggle"] = "spelling",
            ["accept-all"] = "accept-change",
            ["reject-all"] = "reject-change",
            // Clipboard
            ["paste-plain"] = "paste-special",
            ["paste-merge"] = "paste-special",
            // Alternate id spellings carried over from the original map.
            ["bullet-list"] = "bullets",
            ["bulleted-list"] = "bullets",
            ["numbered-list"] = "numbering",
            ["pictures"] = "picture",
            ["table-insert"] = "table",
            ["table-of-contents-gallery"] = "table-of-contents",
        };

    private static string ToCommandIconSlug(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var trimmed = text.Trim();
        if (trimmed.StartsWith("freew.", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed["freew.".Length..];

        var lower = trimmed
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
