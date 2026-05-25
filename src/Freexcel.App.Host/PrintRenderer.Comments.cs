using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Freexcel.Core.Model;

namespace Freexcel.App.Host;

public static partial class PrintRenderer
{
    private static void DrawDisplayedComments(
        DrawingContext dc,
        IReadOnlyDictionary<CellAddress, string> comments,
        IReadOnlyDictionary<CellAddress, ThreadedComment> threadedComments,
        IReadOnlyList<uint> pageRows,
        IReadOnlyList<uint> pageColumns,
        double gridLeft,
        double gridTop,
        double colWidth,
        double rowHeight,
        double pageW,
        double pageH)
    {
        var overlays = WorksheetPageLayout.GetDisplayedCommentOverlays(
            comments,
            threadedComments,
            pageRows,
            pageColumns);
        if (overlays.Count == 0)
            return;

        var fill = new SolidColorBrush(Color.FromRgb(255, 255, 225));
        var border = new Pen(new SolidColorBrush(Color.FromRgb(128, 128, 128)), 0.75);
        var indicator = new SolidColorBrush(Color.FromRgb(192, 0, 0));
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

    private static DrawingVisual RenderCommentSummaryPageVisual(
        double pageW,
        double pageH,
        double marginLeft,
        double marginTop,
        IReadOnlyDictionary<CellAddress, string> comments,
        IReadOnlyDictionary<CellAddress, ThreadedComment> threadedComments)
    {
        var visual = new DrawingVisual();
        using var dc = visual.RenderOpen();
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, pageW, pageH));

        var typeface = new Typeface("Segoe UI");
        DrawCommentText(dc, "Comments", new Point(marginLeft, marginTop), typeface, 14, FontWeights.SemiBold, pageW - marginLeft * 2);

        var y = marginTop + 34;
        foreach (var (address, comment) in GetPrintableComments(comments, threadedComments))
        {
            if (y > pageH - marginTop - 24)
                break;

            var line = $"{address.ToA1()}: {comment}";
            var height = DrawCommentText(dc, line, new Point(marginLeft, y), typeface, PrintFontSize, FontWeights.Normal, pageW - marginLeft * 2);
            y += Math.Max(18, height + 6);
        }

        return visual;
    }

    private static IEnumerable<KeyValuePair<CellAddress, string>> GetPrintableComments(
        IReadOnlyDictionary<CellAddress, string> comments,
        IReadOnlyDictionary<CellAddress, ThreadedComment> threadedComments)
    {
        return comments
            .Concat(threadedComments
                .Where(pair => !comments.ContainsKey(pair.Key))
                .Select(pair => new KeyValuePair<CellAddress, string>(pair.Key, pair.Value.Text)))
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
