using FreeX.App.Presentation.Text;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

public enum PrintPreviewWorkbookPageKind
{
    Worksheet,
    CommentSummary,
}

/// <summary>One page in the ordered print-preview stream for a workbook or sheet group.</summary>
public sealed record PrintPreviewWorkbookPageInfo(
    int PageIndex,
    SheetId SheetId,
    string SheetName,
    PrintPreviewWorkbookPageKind Kind,
    int SheetPageIndex,
    int PrintedPageNumber);

/// <summary>
/// Shared workbook-level print-preview pagination. Visible sheets are visited in workbook order,
/// with a running page offset and one grand total. Each sheet context supplies its grid pages
/// followed by its comments-at-end appendix.
/// </summary>
public sealed class PrintPreviewWorkbookPaginationContext
{
    private sealed record PageEntry(
        PrintPreviewWorkbookPageInfo Info,
        PrintPreviewPaginationContext SheetContext,
        int LocalPageIndex);

    private readonly IReadOnlyList<PageEntry> _entries;

    private PrintPreviewWorkbookPaginationContext(IReadOnlyList<PageEntry> entries)
    {
        _entries = entries;
    }

    public int PageCount => _entries.Count;

    public IReadOnlyList<PrintPreviewWorkbookPageInfo> Pages =>
        _entries.Select(entry => entry.Info).ToArray();

    public static bool TryCreate(
        Workbook workbook,
        ITextMeasurer textMeasurer,
        out PrintPreviewWorkbookPaginationContext context,
        string workbookDirectory = "",
        bool ignorePrintArea = false) =>
        TryCreate(workbook, textMeasurer, out context, workbookDirectory, ignorePrintArea, sheetFilter: null);

    public static bool TryCreate(
        Workbook workbook,
        ITextMeasurer textMeasurer,
        out PrintPreviewWorkbookPaginationContext context,
        string workbookDirectory,
        bool ignorePrintArea,
        IReadOnlySet<SheetId>? sheetFilter)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(textMeasurer);

        var entries = new List<PageEntry>();
        var pageNumberOffset = 0;

        foreach (var sheet in workbook.Sheets)
        {
            if (sheet.IsHidden || sheet.IsVeryHidden || (sheetFilter is not null && !sheetFilter.Contains(sheet.Id)))
                continue;

            if (!PrintPreviewPaginationContext.TryCreate(
                    workbook,
                    sheet,
                    textMeasurer,
                    out var sheetContext,
                    workbookDirectory,
                    ignorePrintArea,
                    printRangeOverride: null,
                    includeEmptySheet: sheetFilter is not null))
            {
                continue;
            }

            var firstPageNumber = sheet.FirstPageNumber ?? 1;
            for (var localPageIndex = 0; localPageIndex < sheetContext.PageCount; localPageIndex++)
            {
                var isGridPage = localPageIndex < sheetContext.GridPageCount;
                entries.Add(new PageEntry(
                    new PrintPreviewWorkbookPageInfo(
                        entries.Count,
                        sheet.Id,
                        sheet.Name,
                        isGridPage ? PrintPreviewWorkbookPageKind.Worksheet : PrintPreviewWorkbookPageKind.CommentSummary,
                        isGridPage ? localPageIndex : localPageIndex - sheetContext.GridPageCount,
                        firstPageNumber + pageNumberOffset + localPageIndex),
                    sheetContext,
                    localPageIndex));
            }

            pageNumberOffset += sheetContext.PageCount;
        }

        if (entries.Count == 0)
        {
            context = null!;
            return false;
        }

        context = new PrintPreviewWorkbookPaginationContext(entries);
        return true;
    }

    public PageContentLayout? BuildPage(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= _entries.Count)
            return null;

        var entry = _entries[pageIndex];
        return entry.SheetContext.BuildPage(entry.LocalPageIndex, entry.Info.PrintedPageNumber, PageCount);
    }

    public PrintPreviewPagePainting? BuildPainting(int pageIndex)
    {
        if (pageIndex < 0 || pageIndex >= _entries.Count)
            return null;

        var entry = _entries[pageIndex];
        return entry.SheetContext.BuildPainting(entry.LocalPageIndex, entry.Info.PrintedPageNumber, PageCount);
    }
}
