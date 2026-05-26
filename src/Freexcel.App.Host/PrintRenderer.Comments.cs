using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static partial class PrintRenderer
{
    private const double CommentSummaryHeaderHeight = 34.0;
    private const double CommentSummaryLineHeight = 24.0;

    private static void DrawDisplayedComments(
        DrawingContext dc,
        ICollection<PdfTextOverlay> textOverlays,
        IReadOnlyDictionary<CellAddress, string> comments,
        IReadOnlyDictionary<CellAddress, ThreadedComment> threadedComments,
        IReadOnlyList<uint> pageRows,
        IReadOnlyList<uint> pageColumns,
        double gridLeft,
        double gridTop,
        double colWidth,
        double rowHeight,
        double pageW,
        double pageH,
        bool blackAndWhite)
    {
        var overlays = WorksheetPageLayout.GetDisplayedCommentOverlays(
            comments,
            threadedComments,
            pageRows,
            pageColumns);
        if (overlays.Count == 0)
            return;

        var fill = blackAndWhite
            ? Brushes.White
            : new SolidColorBrush(Color.FromRgb(255, 255, 225));
        var border = new Pen(blackAndWhite ? Brushes.Black : new SolidColorBrush(Color.FromRgb(128, 128, 128)), 0.75);
        var indicator = blackAndWhite ? Brushes.Black : new SolidColorBrush(Color.FromRgb(192, 0, 0));
        var typeface = new Typeface("Segoe UI");

        foreach (var overlay in overlays)
        {
            var cellLeft = gridLeft + overlay.ColumnIndex * colWidth;
            var cellTop = gridTop + overlay.RowIndex * rowHeight;
            var triangle = new StreamGeometry();
            using (var ctx = triangle.Open())
            {
                ctx.BeginFigure(new Point(cellLeft + colWidth - 7, cellTop), true, true);
                ctx.LineTo(new Point(cellLeft + colWidth, cellTop), true, false);
                ctx.LineTo(new Point(cellLeft + colWidth, cellTop + 7), true, false);
            }
            triangle.Freeze();
            dc.DrawGeometry(indicator, null, triangle);

            var boxWidth = Math.Min(180, Math.Max(80, colWidth * 2.2));
            var boxHeight = 48.0;
            var boxLeft = Math.Min(pageW - boxWidth - 8, cellLeft + colWidth + 4);
            var boxTop = Math.Min(pageH - boxHeight - 8, cellTop + 4);
            var rect = new Rect(Math.Max(8, boxLeft), Math.Max(8, boxTop), boxWidth, boxHeight);
            dc.DrawRectangle(fill, border, rect);
            AddCommentTextOverlays(
                textOverlays,
                overlay.Text,
                rect.Left + 4,
                rect.Top + 4,
                typeface,
                PrintFontSize,
                FontWeights.Normal,
                rect.Width - 8);
            DrawCommentText(
                dc,
                overlay.Text,
                new Point(rect.Left + 4, rect.Top + 4),
                typeface,
                PrintFontSize,
                FontWeights.Normal,
                rect.Width - 8);
        }
    }

    private static (DrawingVisual Visual, IReadOnlyList<PdfTextOverlay> TextOverlays) RenderCommentSummaryPageVisual(
        double pageW,
        double pageH,
        double marginLeft,
        double marginTop,
        IReadOnlyList<KeyValuePair<CellAddress, string>> commentsForPage)
    {
        var visual = new DrawingVisual();
        using var dc = visual.RenderOpen();
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, pageW, pageH));

        var typeface = new Typeface("Segoe UI");
        var maxWidth = pageW - marginLeft * 2;
        var textOverlays = new List<PdfTextOverlay>();
        AddCommentTextOverlays(textOverlays, "Comments", marginLeft, marginTop, typeface, 14, FontWeights.SemiBold, maxWidth);
        DrawCommentText(dc, "Comments", new Point(marginLeft, marginTop), typeface, 14, FontWeights.SemiBold, maxWidth);

        var y = marginTop + CommentSummaryHeaderHeight;
        foreach (var (address, comment) in commentsForPage)
        {
            var line = $"{address.ToA1()}: {comment}";
            AddCommentTextOverlays(textOverlays, line, marginLeft, y, typeface, PrintFontSize, FontWeights.Normal, maxWidth);
            var height = DrawCommentText(dc, line, new Point(marginLeft, y), typeface, PrintFontSize, FontWeights.Normal, maxWidth);
            y += Math.Max(18, height + 6);
        }

        return (visual, textOverlays);
    }

    private static void AddCommentTextOverlays(
        ICollection<PdfTextOverlay> textOverlays,
        string text,
        double x,
        double y,
        Typeface typeface,
        double fontSize,
        FontWeight fontWeight,
        double maxWidth)
    {
        var lineHeight = MeasureCommentText("Ag", typeface, fontSize, fontWeight).Height;
        var lineIndex = 0;
        foreach (var line in WrapCommentSummaryOverlayText(text, typeface, fontSize, fontWeight, maxWidth))
        {
            textOverlays.Add(new PdfTextOverlay(
                line,
                x,
                y + lineHeight * lineIndex,
                fontSize,
                typeface.FontFamily.Source,
                fontWeight >= FontWeights.SemiBold,
                Italic: false,
                Colors.Black));
            lineIndex++;
        }
    }

    private static IReadOnlyList<string> WrapCommentSummaryOverlayText(
        string text,
        Typeface typeface,
        double fontSize,
        FontWeight fontWeight,
        double maxWidth)
    {
        const int maxLines = 3;
        var lines = new List<string>();
        var hardLines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        var truncated = false;
        for (var hardLineIndex = 0; hardLineIndex < hardLines.Length && lines.Count < maxLines && !truncated; hardLineIndex++)
        {
            truncated = AddWrappedCommentSummaryHardLine(
                lines,
                hardLines[hardLineIndex],
                typeface,
                fontSize,
                fontWeight,
                maxWidth,
                maxLines);
        }

        if (lines.Count > 0 &&
            !lines[^1].EndsWith("\u2026", StringComparison.Ordinal) &&
            (truncated || lines.Count == maxLines && ProducesMoreCommentOverlayLines(text, lines, typeface, fontSize, fontWeight, maxWidth, maxLines)))
        {
            lines[^1] = TrimCommentOverlayText(lines[^1], typeface, fontSize, fontWeight, maxWidth);
        }

        return lines;
    }

    private static bool AddWrappedCommentSummaryHardLine(
        ICollection<string> lines,
        string hardLine,
        Typeface typeface,
        double fontSize,
        FontWeight fontWeight,
        double maxWidth,
        int maxLines)
    {
        var words = hardLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            if (lines.Count < maxLines)
                lines.Add("");
            return false;
        }

        var index = 0;
        while (index < words.Length && lines.Count < maxLines)
        {
            var line = words[index++];
            while (index < words.Length && FitsCommentOverlayWidth($"{line} {words[index]}", typeface, fontSize, fontWeight, maxWidth))
                line = $"{line} {words[index++]}";

            if (!FitsCommentOverlayWidth(line, typeface, fontSize, fontWeight, maxWidth))
            {
                line = TrimCommentOverlayText(line, typeface, fontSize, fontWeight, maxWidth);
                lines.Add(line);
                return true;
            }

            lines.Add(line);
        }

        return index < words.Length;
    }

    private static bool ProducesMoreCommentOverlayLines(
        string originalText,
        IReadOnlyList<string> emittedLines,
        Typeface typeface,
        double fontSize,
        FontWeight fontWeight,
        double maxWidth,
        int maxLines)
    {
        var replay = new List<string>();
        var hardLines = originalText.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        foreach (var hardLine in hardLines)
        {
            AddWrappedCommentSummaryHardLine(replay, hardLine, typeface, fontSize, fontWeight, maxWidth, int.MaxValue);
            if (replay.Count > maxLines)
                return true;
        }

        return replay.Count > emittedLines.Count;
    }

    private static bool FitsCommentOverlayWidth(string text, Typeface typeface, double fontSize, FontWeight fontWeight, double maxWidth) =>
        MeasureCommentText(text, typeface, fontSize, fontWeight).WidthIncludingTrailingWhitespace <= Math.Max(1, maxWidth);

    private static string TrimCommentOverlayText(string text, Typeface typeface, double fontSize, FontWeight fontWeight, double maxWidth)
    {
        const string ellipsis = "\u2026";
        var candidate = text.TrimEnd();
        while (candidate.Length > 0 && !FitsCommentOverlayWidth(candidate + ellipsis, typeface, fontSize, fontWeight, maxWidth))
            candidate = candidate[..^1].TrimEnd();

        return candidate.Length == 0 ? ellipsis : candidate + ellipsis;
    }

    private static FormattedText MeasureCommentText(string text, Typeface typeface, double fontSize, FontWeight fontWeight)
    {
        var weightedTypeface = new Typeface(typeface.FontFamily, typeface.Style, fontWeight, typeface.Stretch);
        return new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            weightedTypeface,
            fontSize,
            Brushes.Black,
            1.0);
    }

    internal static IReadOnlyList<IReadOnlyList<KeyValuePair<CellAddress, string>>> BuildCommentSummaryPages(
        IReadOnlyDictionary<CellAddress, string> comments,
        IReadOnlyDictionary<CellAddress, ThreadedComment> threadedComments,
        double pageH,
        double marginTop)
    {
        var printableComments = GetPrintableComments(comments, threadedComments).ToList();
        if (printableComments.Count == 0)
            return [];

        var bodyHeight = Math.Max(
            CommentSummaryLineHeight,
            pageH - marginTop * 2 - CommentSummaryHeaderHeight);
        var commentsPerPage = Math.Max(1, (int)Math.Floor(bodyHeight / CommentSummaryLineHeight));

        return printableComments
            .Chunk(commentsPerPage)
            .Select(chunk => (IReadOnlyList<KeyValuePair<CellAddress, string>>)chunk.ToList())
            .ToList();
    }

    private static IEnumerable<KeyValuePair<CellAddress, string>> GetPrintableComments(
        IReadOnlyDictionary<CellAddress, string> comments,
        IReadOnlyDictionary<CellAddress, ThreadedComment> threadedComments)
    {
        return comments
            .Concat(threadedComments
                .Where(pair => !comments.ContainsKey(pair.Key))
                .Select(pair => new KeyValuePair<CellAddress, string>(
                    pair.Key,
                    CommentNavigationPlanner.FormatThreadedComment(pair.Value))))
            .OrderBy(pair => pair.Key.Row)
            .ThenBy(pair => pair.Key.Col);
    }

    private static double DrawCommentText(
        DrawingContext dc,
        string text,
        Point point,
        Typeface typeface,
        double fontSize,
        FontWeight fontWeight,
        double maxWidth)
    {
        var weightedTypeface = new Typeface(typeface.FontFamily, typeface.Style, fontWeight, typeface.Stretch);
        var ft = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            weightedTypeface,
            fontSize,
            Brushes.Black,
            1.0)
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            MaxLineCount = 3,
            Trimming = TextTrimming.CharacterEllipsis
        };

        dc.DrawText(ft, point);
        return ft.Height;
    }
}
