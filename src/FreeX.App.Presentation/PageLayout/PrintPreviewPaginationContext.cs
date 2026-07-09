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
    private readonly PagePaginationResult _plan;
    private readonly ITextMeasurer _textMeasurer;
    private readonly string _workbookDirectory;

    private PrintPreviewPaginationContext(
        Workbook workbook,
        Sheet sheet,
        PagePaginationResult plan,
        ITextMeasurer textMeasurer,
        string workbookDirectory)
    {
        _workbook = workbook;
        _sheet = sheet;
        _plan = plan;
        _textMeasurer = textMeasurer;
        _workbookDirectory = workbookDirectory;
    }

    public int PageCount => _plan.PageCount;

    /// <summary>
    /// Resolves the print range (explicit print area, else used range), paginates it, and returns a
    /// context when the sheet has at least one printable page. Returns false for an empty sheet.
    /// </summary>
    public static bool TryCreate(
        Workbook workbook,
        Sheet sheet,
        ITextMeasurer textMeasurer,
        out PrintPreviewPaginationContext context,
        string workbookDirectory = "")
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(textMeasurer);

        if (!PageBreakPreviewInstructionBuilder.TryResolvePrintRange(sheet, out var printRange))
        {
            context = null!;
            return false;
        }

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

        if (plan.PageCount <= 0)
        {
            context = null!;
            return false;
        }

        context = new PrintPreviewPaginationContext(workbook, sheet, plan, textMeasurer, workbookDirectory);
        return true;
    }

    public PageContentLayout? BuildPage(int pageIndex) =>
        PageContentRenderModelBuilder.Build(_workbook, _sheet, _plan, pageIndex, _textMeasurer, workbookDirectory: _workbookDirectory);
}
