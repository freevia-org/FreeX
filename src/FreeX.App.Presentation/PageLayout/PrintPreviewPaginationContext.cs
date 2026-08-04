using FreeX.App.Presentation.Text;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

/// <summary>
/// Bundles the inputs needed to lazily build any preview page for one sheet: the workbook, the sheet,
/// and the pagination plan. Page builds are deferred so preview shells only materialize the page
/// currently on screen while sharing the same print-range and pagination policy.
/// </summary>
public sealed class PrintPreviewPaginationContext
{
    private readonly Workbook _workbook;
    private readonly Sheet _sheet;
    private readonly IReadOnlyList<PagePaginationResult> _plans;
    private readonly ITextMeasurer _textMeasurer;
    private readonly string _workbookDirectory;

    private PrintPreviewPaginationContext(
        Workbook workbook,
        Sheet sheet,
        IReadOnlyList<PagePaginationResult> plans,
        ITextMeasurer textMeasurer,
        string workbookDirectory)
    {
        _workbook = workbook;
        _sheet = sheet;
        _plans = plans;
        _textMeasurer = textMeasurer;
        _workbookDirectory = workbookDirectory;
    }

    public int PageCount
    {
        get
        {
            var total = 0;
            foreach (var plan in _plans)
                total += plan.PageCount;
            return total;
        }
    }

    /// <summary>
    /// Resolves every print range for the sheet (all configured print areas, else the used range; see
    /// <see cref="PageBreakPreviewInstructionBuilder.TryResolvePrintRanges"/>), paginates each
    /// independently, and returns a context when at least one range has a printable page. Page numbers
    /// continue across ranges in the order given, mirroring <c>WorkbookExportPrintPlanner</c> and the
    /// multi-area page-break-preview overlay so the interactive Print Preview never omits a configured
    /// print-area region that the real print/PDF export includes. Returns false for an empty sheet.
    /// </summary>
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

        IReadOnlyList<GridRange> printRanges;
        if (ignorePrintArea)
        {
            // Mirrors WorksheetPrintRenderPlanner.ResolvePrintRanges's ignorePrintArea branch: fall
            // back straight to the used range instead of the sheet's configured print area(s), the
            // same "Ignore print area" semantic the real print/PDF export honors
            // (WorksheetPrintRenderPlanner.TryBuild). An empty sheet still means "nothing to
            // preview" here (unlike the export renderer's blank-page fallback), matching this
            // context's existing convention below.
            if (sheet.GetUsedRange() is not { } usedRange)
            {
                context = null!;
                return false;
            }

            printRanges = [usedRange];
        }
        else if (!PageBreakPreviewInstructionBuilder.TryResolvePrintRanges(sheet, out printRanges))
        {
            context = null!;
            return false;
        }

        var plans = new List<PagePaginationResult>(printRanges.Count);
        foreach (var printRange in printRanges)
        {
            var plan = PagePaginationPlanner.Paginate(
                printRange,
                sheet.ScaleToFit,
                sheet.PrintTitleRows,
                sheet.PrintTitleColumns,
                sheet.PaperSize,
                sheet.PageOrientation,
                sheet.PageMargins,
                sheet.RowHeights,
                sheet.DefaultRowHeight,
                sheet.ColumnWidths,
                sheet.DefaultColumnWidth,
                sheet.HeaderMargin,
                sheet.FooterMargin,
                sheet.RowPageBreaks,
                sheet.ColumnPageBreaks,
                // Match the actual print/PDF job (WorkbookExportPrintPlanner), which excludes
                // hidden/filtered rows and columns via these same predicates — otherwise the preview
                // shows rows/columns and a page count that the real output never produces.
                sheet.IsRowEffectivelyHidden,
                sheet.IsColEffectivelyHidden);

            if (plan.PageCount > 0)
                plans.Add(plan);
        }

        if (plans.Count == 0)
        {
            context = null!;
            return false;
        }

        context = new PrintPreviewPaginationContext(workbook, sheet, plans, textMeasurer, workbookDirectory);
        return true;
    }

    public PageContentLayout? BuildPage(int pageIndex) =>
        BuildPage(pageIndex, overridePageNumber: null, overrideTotalPages: null);

    /// <summary>
    /// Builds a page while allowing a workbook-level preview to supply the running page number and
    /// grand total. The default overload above preserves the existing single-sheet/multi-area
    /// semantics; workbook callers use these overrides exactly like WPF's RenderWorkbook path.
    /// </summary>
    public PageContentLayout? BuildPage(
        int pageIndex,
        int? overridePageNumber,
        int? overrideTotalPages)
    {
        if (pageIndex < 0)
            return null;

        // The &P/&N header/footer numbers must run continuously across every print area of this
        // sheet (seeded from FirstPageNumber, aggregate total), matching the real print/PDF
        // export's WorkbookPdfContentBuilder.ResolveEffectiveSheetPageNumber/-TotalPages -- NOT
        // reset per area the way the local `remaining` index below (which only resolves which
        // plan/page to render) would otherwise imply.
        var globalPageNumber = overridePageNumber ?? ((_sheet.FirstPageNumber ?? 1) + pageIndex);
        var totalPages = overrideTotalPages ?? PageCount;

        var remaining = pageIndex;
        foreach (var plan in _plans)
        {
            if (remaining < plan.PageCount)
            {
                return PageContentRenderModelBuilder.Build(
                    _workbook, _sheet, plan, remaining, _textMeasurer, workbookDirectory: _workbookDirectory,
                    overridePageNumber: globalPageNumber, overrideTotalPages: totalPages);
            }

            remaining -= plan.PageCount;
        }

        return null;
    }
}
