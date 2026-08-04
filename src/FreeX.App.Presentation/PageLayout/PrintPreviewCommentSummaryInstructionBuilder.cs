using FreeX.App.Presentation.Text;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

/// <summary>Neutral geometry for one "Comments: At end of sheet" preview appendix page.</summary>
public sealed record PrintPreviewCommentSummaryPage(
    int PageNumber,
    LayoutRect PageBounds,
    double MarginLeft,
    double MarginTop,
    IReadOnlyList<PrintCommentSummaryEntry> Entries);

/// <summary>
/// Flattens comment-summary appendix pages into the same renderer-neutral instructions used by
/// worksheet pages. The wrapping and vertical spacing mirror WPF's comment-summary renderer.
/// </summary>
public static class PrintPreviewCommentSummaryInstructionBuilder
{
    public static PrintPreviewPagePainting Build(
        PrintPreviewCommentSummaryPage page,
        ITextMeasurer textMeasurer)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(textMeasurer);

        var instructions = new List<PrintPreviewPaintInstruction>
        {
            PrintPreviewPaintInstruction.Rectangle(page.PageBounds, PrintPreviewInstructionBuilder.PageBackground),
        };
        var maxWidth = Math.Max(1, page.PageBounds.Width - page.MarginLeft * 2);
        var headerFont = new PageTextFont(
            PageContentRenderModelBuilder.PrintFontFamily,
            14,
            Bold: true,
            Italic: false,
            Color: PrintPreviewInstructionBuilder.HeadingTextColor);
        var bodyFont = PrintPreviewInstructionBuilder.BandFont;

        instructions.Add(PrintPreviewPaintInstruction.TextRun(
            new LayoutPoint(page.MarginLeft, page.MarginTop),
            maxWidth,
            "Comments",
            headerFont,
            PageTextAlignment.Left));

        var y = page.MarginTop + PrintCommentSummaryPlanner.HeaderHeight;
        foreach (var entry in page.Entries)
        {
            var text = $"{entry.Address.ToA1()}: {entry.Text}";
            var lines = PrintCommentSummaryPlanner.WrapOverlayText(
                text,
                maxWidth,
                candidate => textMeasurer.Measure(
                    candidate,
                    bodyFont.FontFamily,
                    bodyFont.FontSize,
                    bodyFont.Bold,
                    bodyFont.Italic).Width);
            var lineHeight = Math.Max(
                1,
                textMeasurer.Measure(
                    "Ag",
                    bodyFont.FontFamily,
                    bodyFont.FontSize,
                    bodyFont.Bold,
                    bodyFont.Italic).Height);

            for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
            {
                instructions.Add(PrintPreviewPaintInstruction.TextRun(
                    new LayoutPoint(page.MarginLeft, y + lineHeight * lineIndex),
                    maxWidth,
                    lines[lineIndex],
                    bodyFont,
                    PageTextAlignment.Left));
            }

            var renderedHeight = Math.Max(lineHeight, lineHeight * Math.Max(1, lines.Count));
            y += Math.Max(18, renderedHeight + 6);
        }

        return new PrintPreviewPagePainting(page.PageNumber, page.PageBounds, instructions);
    }
}
