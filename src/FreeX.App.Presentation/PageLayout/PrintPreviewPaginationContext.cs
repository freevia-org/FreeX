using FreeX.App.Presentation.Text;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

/// <summary>
/// Lazily materializes the shared worksheet print-content plan for the page currently shown by a
/// preview host. Print range, pagination, content, and renderer-profile policy all remain UI-free.
/// </summary>
public sealed class PrintPreviewPaginationContext
{
    private readonly Workbook _workbook;
    private readonly Sheet _sheet;
    private readonly WorksheetPrintRenderPlan _renderPlan;
    private readonly IReadOnlyList<PrintCommentSummaryPagePlan> _commentPages;
    private readonly ITextMeasurer _textMeasurer;
    private readonly string _workbookDirectory;

    private PrintPreviewPaginationContext(
        Workbook workbook,
        Sheet sheet,
        WorksheetPrintRenderPlan renderPlan,
        ITextMeasurer textMeasurer,
        string workbookDirectory)
    {
        _workbook = workbook;
        _sheet = sheet;
        _renderPlan = renderPlan;
        _commentPages = WorksheetPrintPageContentPlanner.BuildCommentSummaryPages(sheet, renderPlan);
        _textMeasurer = textMeasurer;
        _workbookDirectory = workbookDirectory;
    }

    public int PageCount => GridPageCount + _commentPages.Count;

    internal int GridPageCount => _renderPlan.GridPageCount;

    public static bool TryCreate(
        Workbook workbook,
        Sheet sheet,
        ITextMeasurer textMeasurer,
        out PrintPreviewPaginationContext context,
        string workbookDirectory = "",
        bool ignorePrintArea = false) =>
        TryCreate(workbook, sheet, textMeasurer, out context, workbookDirectory, ignorePrintArea, printRangeOverride: null);

    public static bool TryCreate(
        Workbook workbook,
        Sheet sheet,
        ITextMeasurer textMeasurer,
        out PrintPreviewPaginationContext context,
        string workbookDirectory,
        bool ignorePrintArea,
        GridRange? printRangeOverride)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(textMeasurer);

        if (printRangeOverride is { } selection && (selection.Start.Sheet != sheet.Id || selection.End.Sheet != sheet.Id))
        {
            context = null!;
            return false;
        }

        if (!WorksheetPrintRenderPlanner.TryBuild(
                sheet,
                printRangeOverride,
                ignorePrintArea,
                out var renderPlan))
        {
            context = null!;
            return false;
        }

        context = new PrintPreviewPaginationContext(
            workbook,
            sheet,
            renderPlan,
            textMeasurer,
            workbookDirectory);
        return true;
    }

    /// <summary>Gets the worksheet layout for a grid page. Use <see cref="BuildPainting"/> for appendix pages.</summary>
    public PageContentLayout? BuildPage(int pageIndex) =>
        BuildContentPlan(pageIndex)?.PortableLayout;

    public PageContentLayout? BuildPage(
        int pageIndex,
        int? overridePageNumber,
        int? overrideTotalPages) =>
        BuildContentPlan(pageIndex, overridePageNumber, overrideTotalPages)?.PortableLayout;

    public PrintPreviewPagePainting? BuildPainting(
        int pageIndex,
        int? overridePageNumber = null,
        int? overrideTotalPages = null)
    {
        if (pageIndex < 0 || pageIndex >= PageCount)
            return null;

        if (pageIndex < GridPageCount)
        {
            var content = BuildContentPlan(pageIndex, overridePageNumber, overrideTotalPages);
            return content is null ? null : PrintPreviewInstructionBuilder.Build(content);
        }

        var metrics = _renderPlan.Metrics;
        return PrintPreviewCommentSummaryInstructionBuilder.Build(
            new PrintPreviewCommentSummaryPage(
                overridePageNumber ?? (_renderPlan.FirstPageNumber + pageIndex),
                new LayoutRect(0, 0, metrics.PageWidth, metrics.PageHeight),
                metrics.MarginLeft,
                metrics.MarginTop,
                _commentPages[pageIndex - GridPageCount].Entries),
            _textMeasurer);
    }

    public WorksheetPrintPageContentPlan? BuildContentPlan(
        int pageIndex,
        int? overridePageNumber = null,
        int? overrideTotalPages = null)
    {
        if (pageIndex < 0 || pageIndex >= _renderPlan.Pages.Count)
            return null;

        var page = _renderPlan.Pages[pageIndex];
        var pageNumberOffset = overridePageNumber is { } number ? number - page.PageNumber : 0;
        return WorksheetPrintPageContentPlanner.Build(
            _workbook,
            _sheet,
            _renderPlan,
            page,
            _textMeasurer,
            WorksheetPrintMaterializationProfile.AvaloniaPreview,
            workbookDirectory: _workbookDirectory,
            pageNumberOffset: pageNumberOffset,
            totalPageCountOverride: overrideTotalPages ?? PageCount);
    }
}
