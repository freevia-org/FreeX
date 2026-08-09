using System.Globalization;
using System.Windows;
using System.Windows.Media;
using FreeX.App.Presentation.PageLayout;
using FreeX.Core.Model;

namespace FreeX.App.Host;

public static partial class PrintRenderer
{
    private static void DrawDisplayedComments(
        DrawingContext dc,
        ICollection<PdfTextOverlay> textOverlays,
        IReadOnlyList<PageDisplayedCommentBlock> comments,
        bool blackAndWhite)
    {
        if (comments.Count == 0)
            return;

        var fill = blackAndWhite
            ? Brushes.White
            : new SolidColorBrush(Color.FromRgb(255, 255, 225));
        var border = new Pen(blackAndWhite ? Brushes.Black : new SolidColorBrush(Color.FromRgb(128, 128, 128)), 0.75);
        var noteIndicator = blackAndWhite ? Brushes.Black : new SolidColorBrush(Color.FromRgb(192, 0, 0));
        var threadedIndicator = blackAndWhite ? Brushes.Black : new SolidColorBrush(Color.FromRgb(124, 55, 158));
        var typeface = new Typeface("Segoe UI");

        foreach (var comment in comments)
        {
            var triangle = new StreamGeometry();
            using (var ctx = triangle.Open())
            {
                var points = comment.IndicatorPoints;
                ctx.BeginFigure(new Point(points[0].X, points[0].Y), true, true);
                for (var index = 1; index < points.Count; index++)
                    ctx.LineTo(new Point(points[index].X, points[index].Y), true, false);
            }
            triangle.Freeze();
            // Note (legacy) prints red; ThreadedComment/Mixed print the same purple #7C379E used
            // on-screen by GridView.Rendering.CommentIndicatorBrush, so the printed page matches
            // what the user actually saw on the sheet.
            var indicator = comment.Kind == CellCommentDisplayKind.Note ? noteIndicator : threadedIndicator;
            dc.DrawGeometry(indicator, null, triangle);

            var rect = new Rect(comment.Bounds.Left, comment.Bounds.Top, comment.Bounds.Width, comment.Bounds.Height);
            dc.DrawRectangle(fill, border, rect);
            AddCommentTextOverlays(
                textOverlays,
                comment.Text,
                rect.Left + 4,
                rect.Top + 4,
                typeface,
                PrintFontSize,
                FontWeights.Normal,
                rect.Width - 8);
            DrawCommentText(
                dc,
                comment.Text,
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
        IReadOnlyList<PrintCommentSummaryEntry> commentsForPage)
    {
        var visual = new DrawingVisual();
        using var dc = visual.RenderOpen();
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, pageW, pageH));

        var typeface = new Typeface("Segoe UI");
        var maxWidth = pageW - marginLeft * 2;
        var textOverlays = new List<PdfTextOverlay>();
        AddCommentTextOverlays(textOverlays, "Comments", marginLeft, marginTop, typeface, 14, FontWeights.SemiBold, maxWidth);
        DrawCommentText(dc, "Comments", new Point(marginLeft, marginTop), typeface, 14, FontWeights.SemiBold, maxWidth);

        var y = marginTop + PrintCommentSummaryPlanner.HeaderHeight;
        foreach (var entry in commentsForPage)
        {
            var line = $"{entry.Address.ToA1()}: {entry.Text}";
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
        foreach (var line in PrintCommentSummaryPlanner.WrapOverlayText(
            text,
            maxWidth,
            candidate => MeasureCommentText(candidate, typeface, fontSize, fontWeight).WidthIncludingTrailingWhitespace))
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
