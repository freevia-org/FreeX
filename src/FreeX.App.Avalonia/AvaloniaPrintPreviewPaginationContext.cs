using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Text;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

/// <summary>
/// Avalonia's host-side adapter for the shared print-preview scopes.
/// </summary>
internal sealed class AvaloniaPrintPreviewPaginationContext
{
    private readonly PrintPreviewPaginationContext? _sheetContext;
    private readonly PrintPreviewWorkbookPaginationContext? _workbookContext;

    private AvaloniaPrintPreviewPaginationContext(
        PrintPreviewPaginationContext? sheetContext = null,
        PrintPreviewWorkbookPaginationContext? workbookContext = null)
    {
        _sheetContext = sheetContext;
        _workbookContext = workbookContext;
    }

    public int PageCount => _workbookContext?.PageCount ?? _sheetContext?.PageCount ?? 0;

    internal static AvaloniaPrintPreviewPaginationContext FromSheetContext(
        PrintPreviewPaginationContext sheetContext) =>
        new(sheetContext ?? throw new ArgumentNullException(nameof(sheetContext)));

    internal static AvaloniaPrintPreviewPaginationContext FromWorkbookContext(
        PrintPreviewWorkbookPaginationContext workbookContext) =>
        new(workbookContext: workbookContext ?? throw new ArgumentNullException(nameof(workbookContext)));

    internal static AvaloniaPrintPreviewPaginationContext Empty() => new();

    internal static bool TryCreate(
        Workbook workbook,
        Sheet sheet,
        ITextMeasurer textMeasurer,
        GridRange? printRangeOverride,
        out AvaloniaPrintPreviewPaginationContext context,
        string workbookDirectory = "",
        bool ignorePrintArea = false)
    {
        if (!PrintPreviewPaginationContext.TryCreate(
                workbook,
                sheet,
                textMeasurer,
                out var sheetContext,
                workbookDirectory,
                ignorePrintArea,
                printRangeOverride))
        {
            context = null!;
            return false;
        }

        context = FromSheetContext(sheetContext);
        return true;
    }

    internal static bool TryCreateWorkbook(
        Workbook workbook,
        ITextMeasurer textMeasurer,
        out AvaloniaPrintPreviewPaginationContext context,
        string workbookDirectory = "",
        bool ignorePrintArea = false,
        IReadOnlySet<SheetId>? sheetFilter = null)
    {
        if (!PrintPreviewWorkbookPaginationContext.TryCreate(
                workbook,
                textMeasurer,
                out var workbookContext,
                workbookDirectory,
                ignorePrintArea,
                sheetFilter))
        {
            context = null!;
            return false;
        }

        context = FromWorkbookContext(workbookContext);
        return true;
    }

    public PageContentLayout? BuildPage(int pageIndex) =>
        _workbookContext is not null ? _workbookContext.BuildPage(pageIndex) : _sheetContext?.BuildPage(pageIndex);

    public PrintPreviewPagePainting? BuildPainting(int pageIndex) =>
        _workbookContext is not null ? _workbookContext.BuildPainting(pageIndex) : _sheetContext?.BuildPainting(pageIndex);
}
