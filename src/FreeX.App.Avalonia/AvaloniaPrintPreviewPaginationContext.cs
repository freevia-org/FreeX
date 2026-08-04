using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Text;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// Avalonia's host-side print-preview scope adapter. Normal previews keep using the shared
/// multi-area context; WPF's selection override is projected locally because the shared context
/// intentionally models configured sheet print areas, not transient UI selections.
/// </summary>
internal sealed class AvaloniaPrintPreviewPaginationContext
{
    private readonly PrintPreviewPaginationContext? _sheetContext;
    private readonly Workbook? _workbook;
    private readonly Sheet? _sheet;
    private readonly PagePaginationResult? _selectionPlan;
    private readonly ITextMeasurer? _textMeasurer;
    private readonly string _workbookDirectory;

    private AvaloniaPrintPreviewPaginationContext(PrintPreviewPaginationContext sheetContext)
    {
        _sheetContext = sheetContext;
        _workbookDirectory = "";
    }

    private AvaloniaPrintPreviewPaginationContext(
        Workbook workbook,
        Sheet sheet,
        PagePaginationResult selectionPlan,
        ITextMeasurer textMeasurer,
        string workbookDirectory)
    {
        _workbook = workbook;
        _sheet = sheet;
        _selectionPlan = selectionPlan;
        _textMeasurer = textMeasurer;
        _workbookDirectory = workbookDirectory;
    }

    public int PageCount => _sheetContext?.PageCount ?? _selectionPlan!.PageCount;

    internal static AvaloniaPrintPreviewPaginationContext FromSheetContext(
        PrintPreviewPaginationContext sheetContext) =>
        new(sheetContext ?? throw new ArgumentNullException(nameof(sheetContext)));

    internal static bool TryCreate(
        Workbook workbook,
        Sheet sheet,
        ITextMeasurer textMeasurer,
        GridRange? printRangeOverride,
        out AvaloniaPrintPreviewPaginationContext context,
        string workbookDirectory = "",
        bool ignorePrintArea = false)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(textMeasurer);

        if (printRangeOverride is not { } selection)
        {
            if (!PrintPreviewPaginationContext.TryCreate(
                    workbook,
                    sheet,
                    textMeasurer,
                    out var sheetContext,
                    workbookDirectory,
                    ignorePrintArea))
            {
                context = null!;
                return false;
            }

            context = FromSheetContext(sheetContext);
            return true;
        }

        if (selection.Start.Sheet != sheet.Id || selection.End.Sheet != sheet.Id)
        {
            context = null!;
            return false;
        }

        var selectionPlan = PagePaginationPlanner.Paginate(
            selection,
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
            sheet.IsRowEffectivelyHidden,
            sheet.IsColEffectivelyHidden);

        if (selectionPlan.PageCount == 0)
        {
            context = null!;
            return false;
        }

        context = new AvaloniaPrintPreviewPaginationContext(
            workbook,
            sheet,
            selectionPlan,
            textMeasurer,
            workbookDirectory);
        return true;
    }

    public PageContentLayout? BuildPage(int pageIndex)
    {
        if (_sheetContext is not null)
            return _sheetContext.BuildPage(pageIndex);

        if (pageIndex < 0 || pageIndex >= _selectionPlan!.PageCount)
            return null;

        return PageContentRenderModelBuilder.Build(
            _workbook!,
            _sheet!,
            _selectionPlan,
            pageIndex,
            _textMeasurer!,
            workbookDirectory: _workbookDirectory,
            overridePageNumber: (_sheet!.FirstPageNumber ?? 1) + pageIndex,
            overrideTotalPages: _selectionPlan.PageCount);
    }
}
