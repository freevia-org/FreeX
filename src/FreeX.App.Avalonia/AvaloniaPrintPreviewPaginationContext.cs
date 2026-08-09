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
    private readonly bool _isEmpty;
    private readonly PrintPreviewPaginationContext? _sheetContext;
    private readonly PrintPreviewWorkbookPaginationContext? _workbookContext;
    private readonly Workbook? _workbook;
    private readonly Sheet? _sheet;
    private readonly WorksheetPrintRenderPlan? _selectionPlan;
    private readonly ITextMeasurer? _textMeasurer;
    private readonly string _workbookDirectory;

    private AvaloniaPrintPreviewPaginationContext(PrintPreviewPaginationContext sheetContext)
    {
        _sheetContext = sheetContext;
        _workbookDirectory = "";
    }

    private AvaloniaPrintPreviewPaginationContext(PrintPreviewWorkbookPaginationContext workbookContext)
    {
        _workbookContext = workbookContext;
        _workbookDirectory = "";
    }

    private AvaloniaPrintPreviewPaginationContext(bool isEmpty)
    {
        _isEmpty = isEmpty;
        _workbookDirectory = "";
    }

    private AvaloniaPrintPreviewPaginationContext(
        Workbook workbook,
        Sheet sheet,
        WorksheetPrintRenderPlan selectionPlan,
        ITextMeasurer textMeasurer,
        string workbookDirectory)
    {
        _workbook = workbook;
        _sheet = sheet;
        _selectionPlan = selectionPlan;
        _textMeasurer = textMeasurer;
        _workbookDirectory = workbookDirectory;
    }

    public int PageCount =>
        _isEmpty
            ? 0
            : _workbookContext?.PageCount ?? _sheetContext?.PageCount ?? _selectionPlan!.GridPageCount;

    internal static AvaloniaPrintPreviewPaginationContext FromSheetContext(
        PrintPreviewPaginationContext sheetContext) =>
        new(sheetContext ?? throw new ArgumentNullException(nameof(sheetContext)));

    internal static AvaloniaPrintPreviewPaginationContext FromWorkbookContext(
        PrintPreviewWorkbookPaginationContext workbookContext) =>
        new(workbookContext ?? throw new ArgumentNullException(nameof(workbookContext)));

    internal static AvaloniaPrintPreviewPaginationContext Empty() => new(isEmpty: true);

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

        if (!WorksheetPrintRenderPlanner.TryBuild(
                sheet,
                selection,
                ignorePrintArea,
                out var selectionPlan))
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

    internal static bool TryCreateWorkbook(
        Workbook workbook,
        ITextMeasurer textMeasurer,
        out AvaloniaPrintPreviewPaginationContext context,
        string workbookDirectory = "",
        bool ignorePrintArea = false)
    {
        if (!PrintPreviewWorkbookPaginationContext.TryCreate(
                workbook,
                textMeasurer,
                out var workbookContext,
                workbookDirectory,
                ignorePrintArea))
        {
            context = null!;
            return false;
        }

        context = FromWorkbookContext(workbookContext);
        return true;
    }

    public PageContentLayout? BuildPage(int pageIndex)
    {
        if (_isEmpty)
            return null;

        if (_workbookContext is not null)
            return _workbookContext.BuildPage(pageIndex);

        if (_sheetContext is not null)
            return _sheetContext.BuildPage(pageIndex);

        if (pageIndex < 0 || pageIndex >= _selectionPlan!.GridPageCount)
            return null;

        var page = _selectionPlan.Pages[pageIndex];
        return WorksheetPrintPageContentPlanner.Build(
            _workbook!,
            _sheet!,
            _selectionPlan,
            page,
            _textMeasurer!,
            WorksheetPrintMaterializationProfile.AvaloniaPreview,
            workbookDirectory: _workbookDirectory,
            totalPageCountOverride: _selectionPlan.GridPageCount)?.PortableLayout;
    }

    public PrintPreviewPagePainting? BuildPainting(int pageIndex)
    {
        if (_isEmpty)
            return null;

        if (_workbookContext is not null)
            return _workbookContext.BuildPainting(pageIndex);

        if (_sheetContext is not null)
        {
            var plan = _sheetContext.BuildContentPlan(pageIndex);
            return plan is null ? null : PrintPreviewInstructionBuilder.Build(plan);
        }

        if (pageIndex < 0 || pageIndex >= _selectionPlan!.GridPageCount)
            return null;

        var page = _selectionPlan.Pages[pageIndex];
        var selectionPlan = WorksheetPrintPageContentPlanner.Build(
            _workbook!,
            _sheet!,
            _selectionPlan,
            page,
            _textMeasurer!,
            WorksheetPrintMaterializationProfile.AvaloniaPreview,
            workbookDirectory: _workbookDirectory,
            totalPageCountOverride: _selectionPlan.GridPageCount);
        return selectionPlan is null ? null : PrintPreviewInstructionBuilder.Build(selectionPlan);
    }
}
