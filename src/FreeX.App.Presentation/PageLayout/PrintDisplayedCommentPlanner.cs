using FreeX.App.Presentation.Comments;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.App.Presentation.Text;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

/// <summary>Shared geometry and paint policy for comments pinned open on a printed worksheet.</summary>
public static class PrintDisplayedCommentPlanner
{
    private static readonly PresentationRgb NoteIndicator = new(192, 0, 0);
    private static readonly PresentationRgb ThreadIndicator = new(124, 55, 158);
    private static readonly PresentationRgb BoxFill = new(255, 255, 225);
    private static readonly PresentationRgb BoxStroke = new(128, 128, 128);

    public static IReadOnlyList<WorksheetDisplayedComment> GetOverlays(
        Sheet sheet,
        IReadOnlyList<uint> pageRows,
        IReadOnlyList<uint> pageColumns)
    {
        if (sheet.PrintDraftQuality || sheet.PrintComments != WorksheetPrintComments.AsDisplayed ||
            sheet.ShownComments.Count == 0)
        {
            return [];
        }

        var overlays = WorksheetPageLayout.GetDisplayedCommentOverlays(
            sheet.Comments, sheet.ThreadedComments, pageRows, pageColumns, sheet.ShownComments);
        if (sheet.ThreadedComments.Count == 0)
            return overlays;

        return overlays.Select(overlay => sheet.ThreadedComments.TryGetValue(overlay.Address, out var thread)
            ? overlay with { Text = CommentNavigationPlanner.FormatThreadedComment(thread) }
            : overlay).ToArray();
    }

    /// <summary>Positions a comment using top-left layout coordinates and a common unit scale.</summary>
    public static PageDisplayedCommentBlock BuildBlock(
        WorksheetDisplayedComment overlay,
        LayoutRect cellBounds,
        LayoutRect pageBounds,
        double scale = 1)
    {
        var boxWidth = Math.Clamp(cellBounds.Width * 2.2, 80 * scale, 180 * scale);
        var boxHeight = 48 * scale;
        var pageInset = 8 * scale;
        var gap = 4 * scale;
        var indicatorSize = 7 * scale;
        var boxLeft = Math.Min(pageBounds.Right - boxWidth - pageInset, cellBounds.Right + gap);
        var boxTop = Math.Min(pageBounds.Bottom - boxHeight - pageInset, cellBounds.Top + gap);
        return new PageDisplayedCommentBlock(
            overlay.Kind,
            overlay.Text,
            [
                new LayoutPoint(cellBounds.Right - indicatorSize, cellBounds.Top),
                new LayoutPoint(cellBounds.Right, cellBounds.Top),
                new LayoutPoint(cellBounds.Right, cellBounds.Top + indicatorSize),
            ],
            new LayoutRect(
                Math.Max(pageBounds.Left + pageInset, boxLeft),
                Math.Max(pageBounds.Top + pageInset, boxTop),
                boxWidth,
                boxHeight));
    }

    /// <summary>Paints a comment indicator and its bounded text box in the block's layout units.</summary>
    public static IReadOnlyList<PrintPreviewPaintInstruction> BuildInstructions(
        PageDisplayedCommentBlock block,
        bool blackAndWhite,
        ITextMeasurer textMeasurer,
        double scale = 1)
    {
        var black = PrintPreviewInstructionBuilder.HeadingTextColor;
        var indicator = blackAndWhite ? black
            : block.Kind == CellCommentDisplayKind.Note ? NoteIndicator
            : ThreadIndicator;
        var fill = blackAndWhite ? PrintPreviewInstructionBuilder.PageBackground : BoxFill;
        var stroke = blackAndWhite ? black : BoxStroke;
        var font = PrintPreviewInstructionBuilder.BandFont with
        {
            FontSize = PageContentRenderModelBuilder.PrintFontSize * scale,
        };
        var padding = 4 * scale;
        var textWidth = block.Bounds.Width - padding * 2;
        TextSize Measure(string text) => textMeasurer.Measure(text, font.FontFamily, font.FontSize, font.Bold, font.Italic);
        var lines = PrintCommentSummaryPlanner.WrapOverlayText(block.Text, textWidth, text => Measure(text).Width);
        var lineHeight = Math.Max(scale, Measure("Ag").Height);
        var instructions = new List<PrintPreviewPaintInstruction>(lines.Count + 2)
        {
            PrintPreviewPaintInstruction.Polygon(block.IndicatorPoints, fill: indicator),
            PrintPreviewPaintInstruction.Rectangle(block.Bounds, fill, stroke, 0.75 * scale),
        };

        for (var index = 0; index < lines.Count; index++)
        {
            instructions.Add(PrintPreviewPaintInstruction.TextRun(
                new LayoutPoint(block.Bounds.Left + padding, block.Bounds.Top + padding + lineHeight * index),
                textWidth, lines[index], font, PageTextAlignment.Left));
        }

        return instructions;
    }
}
