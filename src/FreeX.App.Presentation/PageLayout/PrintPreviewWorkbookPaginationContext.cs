using FreeX.App.Presentation.Text;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

public enum PrintPreviewWorkbookPageKind
{
    Worksheet,
    CommentSummary,
}

/// <summary>One page in the ordered Entire Workbook print-preview stream.</summary>
public sealed record PrintPreviewWorkbookPageInfo(
    int PageIndex,
    SheetId SheetId,
    string SheetName,
    PrintPreviewWorkbookPageKind Kind,
    int SheetPageIndex,
    int PrintedPageNumber);

/// <summary>
/// Shared workbook-level print-preview pagination. The order and numbering mirror WPF's
/// <c>PrintRenderer.RenderWorkbook</c>: visible sheets only, each sheet's grid pages followed by its
/// optional comments-at-end appendix, with a running page offset and one grand total. Rendering of
/// worksheet pages remains delegated to <see cref="PrintPreviewPaginationContext"/> so Selection and
/// Active Sheets keep their established path.
/// </summary>
public sealed class PrintPreviewWorkbookPaginationContext
{
    private sealed record PageEntry(
        PrintPreviewWorkbookPageInfo Info,
        PrintPreviewPaginationContext? SheetContext,
        PrintCommentSummaryPagePlan? CommentPage,
        WorksheetPrintRenderMetrics? Metrics);

    private readonly IReadOnlyList<PageEntry> _entries;
    private readonly ITextMeasurer _textMeasurer;

    private PrintPreviewWorkbookPaginationContext(
        IReadOnlyList<PageEntry> entries,
        ITextMeasurer textMeasurer)
    {
        _entries = entries;
        _textMeasurer = textMeasurer;
    }

    public int PageCount => _entries.Count;

    public IReadOnlyList<PrintPreviewWorkbookPageInfo> Pages =>
        _entries.Select(entry => entry.Info).ToArray();

    public static bool TryCreate(
        Workbook workbook,
        ITextMeasurer textMeasurer,
        out PrintPreviewWorkbookPaginationContext context,
        string workbookDirectory = "",
        bool ignorePrintArea = false)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(textMeasurer);

        var entries = new List<PageEntry>();
        var pageNumberOffset = 0;

        foreach (var sheet in workbook.Sheets)
        {
            if (sheet.IsHidden || sheet.IsVeryHidden)
                continue;

            // Use the same neutral worksheet plan that WPF uses to derive both printable grid pages
            // and the comment appendix count. An empty sheet contributes no pages and is skipped.
            if (!WorksheetPrintRenderPlanner.TryBuild(sheet, printRangeOverride: null, ignorePrintArea, out var printPlan))
                continue;

            if (!PrintPreviewPaginationContext.TryCreate(
                    workbook,
                    sheet,
                    textMeasurer,
                    out var sheetContext,
                    workbookDirectory,
                    ignorePrintArea))
            {
                continue;
            }

            var printedComments = PrintCommentSummaryPlanner.FilterToPrintedCells(sheet, printPlan);
            var commentPages = sheet.PrintComments == WorksheetPrintComments.AtEnd
                ? PrintCommentSummaryPlanner.BuildPages(
                    printedComments.Comments,
                    printedComments.ThreadedComments,
                    printPlan.Metrics.PageHeight,
                    printPlan.Metrics.MarginTop)
                : (IReadOnlyList<PrintCommentSummaryPagePlan>)[];

            var sheetPageCount = sheetContext.PageCount + commentPages.Count;
            var firstPageNumber = sheet.FirstPageNumber ?? 1;

            for (var localPageIndex = 0; localPageIndex < sheetContext.PageCount; localPageIndex++)
            {
                entries.Add(new PageEntry(
                    new PrintPreviewWorkbookPageInfo(
                        entries.Count,
                        sheet.Id,
                        sheet.Name,
                        PrintPreviewWorkbookPageKind.Worksheet,
                        localPageIndex,
                        firstPageNumber + pageNumberOffset + localPageIndex),
                    sheetContext,
                    CommentPage: null,
                    Metrics: null));
            }

            for (var commentPageIndex = 0; commentPageIndex < commentPages.Count; commentPageIndex++)
            {
                entries.Add(new PageEntry(
                    new PrintPreviewWorkbookPageInfo(
                        entries.Count,
                        sheet.Id,
                        sheet.Name,
                        PrintPreviewWorkbookPageKind.CommentSummary,
                        commentPageIndex,
                        firstPageNumber + pageNumberOffset + sheetContext.PageCount + commentPageIndex),
                    SheetContext: null,
                    commentPages[commentPageIndex],
                    printPlan.Metrics));
            }

            pageNumberOffset += sheetPageCount;
        }

        if (entries.Count == 0)
        {
            context = null!;
            return false;
        }

        context = new PrintPreviewWorkbookPaginationContext(entries, textMeasurer);
        return true;
    }

    public PageContentLayout? BuildPage(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= _entries.Count)
            return null;

        var entry = _entries[pageIndex];
        if (entry.Info.Kind != PrintPreviewWorkbookPageKind.Worksheet)
            return null;

        return entry.SheetContext?.BuildPage(
            entry.Info.SheetPageIndex,
            entry.Info.PrintedPageNumber,
            PageCount);
    }

    public PrintPreviewPagePainting? BuildPainting(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= _entries.Count)
            return null;

        var entry = _entries[pageIndex];
        if (entry.Info.Kind == PrintPreviewWorkbookPageKind.Worksheet)
        {
            var layout = BuildPage(pageIndex);
            return layout is null ? null : PrintPreviewInstructionBuilder.Build(layout);
        }

        var metrics = entry.Metrics ?? throw new InvalidOperationException("Comment page metrics are missing.");
        var commentPage = entry.CommentPage ?? throw new InvalidOperationException("Comment page data is missing.");
        return PrintPreviewCommentSummaryInstructionBuilder.Build(
            new PrintPreviewCommentSummaryPage(
                entry.Info.PrintedPageNumber,
                new LayoutRect(0, 0, metrics.PageWidth, metrics.PageHeight),
                metrics.MarginLeft,
                metrics.MarginTop,
                commentPage.Entries),
            _textMeasurer);
    }
}
