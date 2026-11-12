using FreeW.Core.Model;

namespace FreeW.App.Presentation.Backstage;

public enum TemplateThumbnailMarkKind
{
    /// <summary>A large title bar (Title-like styles).</summary>
    Title,

    /// <summary>A heading bar, drawn in the accent colour.</summary>
    Heading,

    /// <summary>One line of body text.</summary>
    Text,

    /// <summary>A table outline with <see cref="TemplateThumbnailMark.Rows"/> x <see cref="TemplateThumbnailMark.Columns"/> cells.</summary>
    Table,

    /// <summary>A horizontal rule (a paragraph bottom border).</summary>
    Rule,
}

/// <summary>
/// One shape of a template tile's miniature page. Coordinates are fractions of the page (0..1) so a
/// renderer scales them to whatever tile size it draws.
/// </summary>
public sealed record TemplateThumbnailMark(
    TemplateThumbnailMarkKind Kind,
    double X,
    double Y,
    double Width,
    double Height,
    int Rows = 0,
    int Columns = 0);

/// <summary>
/// Sketches page one of a document as a handful of bars and boxes: a renderer-neutral miniature for
/// the File &gt; New template tiles. It follows the document's real block order, styles, alignment
/// and tables, so each built-in template gets a recognisable tile without rasterising a page.
/// </summary>
public static class FreeWTemplateThumbnailPlanner
{
    private const double Left = 0.12;
    private const double Right = 0.88;
    private const double Top = 0.09;
    private const double Bottom = 0.93;
    private const double LineHeight = 0.011;
    private const double LinePitch = 0.026;
    private const int CharactersPerLine = 85;

    /// <summary>The placeholder miniature for a template whose content is not inspected (a file on disk).</summary>
    public static IReadOnlyList<TemplateThumbnailMark> Generic { get; } =
    [
        new(TemplateThumbnailMarkKind.Heading, Left, 0.12, 0.42, 0.032),
        new(TemplateThumbnailMarkKind.Text, Left, 0.21, Right - Left, LineHeight),
        new(TemplateThumbnailMarkKind.Text, Left, 0.21 + LinePitch, Right - Left, LineHeight),
        new(TemplateThumbnailMarkKind.Text, Left, 0.21 + 2 * LinePitch, 0.5, LineHeight),
        new(TemplateThumbnailMarkKind.Text, Left, 0.34, Right - Left, LineHeight),
        new(TemplateThumbnailMarkKind.Text, Left, 0.34 + LinePitch, Right - Left, LineHeight),
        new(TemplateThumbnailMarkKind.Text, Left, 0.34 + 2 * LinePitch, 0.36, LineHeight),
    ];

    public static IReadOnlyList<TemplateThumbnailMark> Build(TextDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var marks = new List<TemplateThumbnailMark>();
        var y = Top;
        var first = true;

        foreach (var block in document.Blocks)
        {
            if (y >= Bottom)
                break;

            if (block is Table table)
            {
                var rows = Math.Clamp(table.RowCount, 1, 6);
                var height = Math.Min(rows * 0.034, Bottom - y);
                marks.Add(new TemplateThumbnailMark(
                    TemplateThumbnailMarkKind.Table,
                    Left,
                    y,
                    Right - Left,
                    height,
                    rows,
                    Math.Clamp(table.ColumnCount, 1, 6)));
                y += height + 0.03;
                first = false;
                continue;
            }

            if (block is not Paragraph paragraph)
                continue;

            if (!first && paragraph.Formatting.PageBreakBefore)
                break;
            first = false;

            y += Math.Min(paragraph.Formatting.SpaceBeforePt / 792.0, 0.2);
            var text = paragraph.PlainText;
            if (text.Trim().Length > 0)
                y = AddParagraph(marks, paragraph, text, y);
            else
                y += 0.012;

            if (paragraph.Formatting.Border is { } border && (border.BottomOnly || border.Bottom) && y < Bottom)
            {
                marks.Add(new TemplateThumbnailMark(TemplateThumbnailMarkKind.Rule, Left, y - 0.008, Right - Left, 0.004));
                y += 0.012;
            }

            if (paragraph.Runs.Any(run => run.IsPageBreak))
                break;
        }

        return marks;
    }

    private static double AddParagraph(List<TemplateThumbnailMark> marks, Paragraph paragraph, string text, double y)
    {
        var styleId = paragraph.StyleId ?? string.Empty;
        var centered = paragraph.Formatting.Alignment == TextAlignment.Center;
        var rightAligned = paragraph.Formatting.Alignment == TextAlignment.Right;

        if (styleId.Contains("Title", StringComparison.OrdinalIgnoreCase)
            && !styleId.Contains("Subtitle", StringComparison.OrdinalIgnoreCase))
        {
            marks.Add(Bar(TemplateThumbnailMarkKind.Title, Fraction(text.Length, 26, 0.3, 0.7), y, 0.046, centered, rightAligned));
            return y + 0.07;
        }

        if (styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase)
            || string.Equals(styleId, TableOfContents.HeadingStyleId, StringComparison.Ordinal))
        {
            y += 0.012;
            marks.Add(Bar(TemplateThumbnailMarkKind.Heading, Fraction(text.Length, 40, 0.16, 0.6), y, 0.022, centered, rightAligned));
            return y + 0.04;
        }

        var indent = paragraph.Formatting.ListKind != ListKind.None ? 0.05 : 0;
        var lines = Math.Clamp((int)Math.Ceiling(text.Length / (double)CharactersPerLine), 1, 4);
        for (var line = 0; line < lines && y < Bottom; line++)
        {
            var isLast = line == lines - 1;
            var remaining = text.Length - line * CharactersPerLine;
            var width = isLast
                ? Fraction(remaining, CharactersPerLine, 0.1, 1) * (Right - Left - indent)
                : Right - Left - indent;
            var x = centered
                ? 0.5 - width / 2
                : rightAligned ? Right - width : Left + indent;
            marks.Add(new TemplateThumbnailMark(TemplateThumbnailMarkKind.Text, x, y, width, LineHeight));
            y += LinePitch;
        }

        return y + Math.Min(Math.Max(paragraph.Formatting.SpaceAfterPt, 0) / 792.0, 0.05);
    }

    private static TemplateThumbnailMark Bar(
        TemplateThumbnailMarkKind kind,
        double widthFraction,
        double y,
        double height,
        bool centered,
        bool rightAligned)
    {
        var width = widthFraction * (Right - Left);
        var x = centered ? 0.5 - width / 2 : rightAligned ? Right - width : Left;
        return new TemplateThumbnailMark(kind, x, y, width, height);
    }

    private static double Fraction(int length, int full, double min, double max) =>
        Math.Clamp(length / (double)full, min, max);
}
