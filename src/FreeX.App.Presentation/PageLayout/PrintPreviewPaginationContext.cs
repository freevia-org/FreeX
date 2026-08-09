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
        _textMeasurer = textMeasurer;
        _workbookDirectory = workbookDirectory;
    }

    public int PageCount => _renderPlan.GridPageCount;

    public static bool TryCreate(
        Workbook workbook,
        Sheet sheet,
        ITextMeasurer textMeasurer,
        out PrintPreviewPaginationContext context,
        string workbookDirectory = "",
        bool ignorePrintArea = false)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(textMeasurer);

        // Direct print/export intentionally emits one blank page. The interactive preview has
        // historically shown no page for a completely empty sheet without an explicit print area.
        if (sheet.GetUsedRange() is null && (ignorePrintArea || sheet.PrintAreas.Count == 0))
        {
            context = null!;
            return false;
        }

        if (!WorksheetPrintRenderPlanner.TryBuild(
                sheet,
                printRangeOverride: null,
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

    public PageContentLayout? BuildPage(int pageIndex) =>
        BuildContentPlan(pageIndex)?.PortableLayout;

    public PageContentLayout? BuildPage(
        int pageIndex,
        int? overridePageNumber,
        int? overrideTotalPages) =>
        BuildContentPlan(pageIndex, overridePageNumber, overrideTotalPages)?.PortableLayout;

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
