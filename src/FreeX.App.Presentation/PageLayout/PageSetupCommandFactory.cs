using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

public sealed record PageSetupCommandRequest
{
    /// <summary>
    /// All configured print areas (Excel supports multiple comma-separated regions on the
    /// <c>_xlnm.Print_Area</c> defined name). Empty means clear (print the used range).
    /// </summary>
    public IReadOnlyList<GridRange> PrintAreas { get; init; } = [];
    public WorksheetPageOrientation Orientation { get; init; } = WorksheetPageOrientation.Portrait;
    public WorksheetPaperSize PaperSize { get; init; } = WorksheetPaperSize.A4;
    public WorksheetPageMargins Margins { get; init; } = WorksheetPageMargins.Normal;
    public bool PrintGridlines { get; init; }
    public bool PrintHeadings { get; init; }
    public WorksheetScaleToFit ScaleToFit { get; init; } = WorksheetScaleToFit.Default;
    public WorksheetRepeatRange? PrintTitleRows { get; init; }
    public WorksheetRepeatRange? PrintTitleColumns { get; init; }
    public bool CenterHorizontally { get; init; }
    public bool CenterVertically { get; init; }
    public WorksheetPageOrder PageOrder { get; init; } = WorksheetPageOrder.DownThenOver;
    public int? FirstPageNumber { get; init; }
    public double HeaderMargin { get; init; } = 0.3;
    public double FooterMargin { get; init; } = 0.3;
    public bool PrintBlackAndWhite { get; init; }
    public bool PrintDraftQuality { get; init; }
    public int? PrintQualityDpi { get; init; }
    public WorksheetPrintErrorValue PrintErrorValue { get; init; } = WorksheetPrintErrorValue.Displayed;
    public WorksheetPrintComments PrintComments { get; init; } = WorksheetPrintComments.None;
    public HeaderFooterEditorState HeaderFooter { get; init; } = HeaderFooterEditorState.Empty;
}

public sealed record PageSetupCommandPlan(
    GridRange? PrintArea,
    IReadOnlyList<GridRange> PrintAreas,
    IWorkbookCommand PrintAreaCommand,
    SetPageSetupCommand PageSetupCommand,
    SetHeaderFooterCommand HeaderFooterCommand)
{
    public IWorkbookCommand ToComposite(string label = "Page Setup") =>
        new CompositeWorkbookCommand(label, [PrintAreaCommand, PageSetupCommand, HeaderFooterCommand]);
}

public static class PageSetupCommandFactory
{
    public static PageSetupCommandPlan Build(SheetId targetSheetId, PageSetupCommandRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var headerFooter = request.HeaderFooter;
        var remappedPrintAreas = RemapPrintAreasToSheet(request.PrintAreas, targetSheetId);
        return new PageSetupCommandPlan(
            remappedPrintAreas.Count > 0 ? remappedPrintAreas[0] : null,
            remappedPrintAreas,
            BuildPrintAreasCommand(targetSheetId, request.PrintAreas),
            new SetPageSetupCommand(
                targetSheetId,
                request.Orientation,
                request.PaperSize,
                request.Margins,
                request.PrintGridlines,
                request.PrintHeadings,
                request.ScaleToFit,
                request.PrintTitleRows,
                request.PrintTitleColumns,
                request.CenterHorizontally,
                request.CenterVertically,
                request.PageOrder,
                request.FirstPageNumber,
                request.HeaderMargin,
                request.FooterMargin,
                request.PrintBlackAndWhite,
                request.PrintDraftQuality,
                request.PrintQualityDpi,
                request.PrintErrorValue,
                request.PrintComments),
            BuildHeaderFooterCommand(targetSheetId, headerFooter));
    }

    public static SetHeaderFooterCommand BuildHeaderFooterCommand(
        SheetId targetSheetId,
        HeaderFooterEditorState headerFooter)
    {
        ArgumentNullException.ThrowIfNull(headerFooter);

        return new SetHeaderFooterCommand(
            targetSheetId,
            headerFooter.Header,
            headerFooter.Footer,
            headerFooter.FirstPageHeader,
            headerFooter.FirstPageFooter,
            headerFooter.EvenPageHeader,
            headerFooter.EvenPageFooter,
            headerFooter.DifferentFirstPage,
            headerFooter.DifferentOddEvenPages,
            headerFooter.ScaleWithDocument,
            headerFooter.AlignWithMargins,
            headerFooter.HeaderPictures,
            headerFooter.FooterPictures,
            headerFooter.FirstPageHeaderPictures,
            headerFooter.FirstPageFooterPictures,
            headerFooter.EvenPageHeaderPictures,
            headerFooter.EvenPageFooterPictures);
    }

    public static IWorkbookCommand BuildPrintAreaCommand(SheetId targetSheetId, GridRange? printArea) =>
        RemapPrintAreaToSheet(printArea, targetSheetId) is { } range
            ? new SetPrintAreaCommand(targetSheetId, range)
            : new ClearPrintAreaCommand(targetSheetId);

    /// <summary>
    /// Builds the command for the full (possibly multi-region) print-area set. Issues a
    /// <see cref="SetPrintAreasCommand"/> so every region survives, instead of collapsing to one
    /// via <see cref="SetPrintAreaCommand"/>. Distinct name (not an overload of
    /// <see cref="BuildPrintAreaCommand(SheetId, GridRange?)"/>) so existing <c>null</c>-literal
    /// call sites for the single-region overload stay unambiguous.
    /// </summary>
    public static IWorkbookCommand BuildPrintAreasCommand(SheetId targetSheetId, IReadOnlyList<GridRange> printAreas)
    {
        var remapped = RemapPrintAreasToSheet(printAreas, targetSheetId);
        return remapped.Count > 0
            ? new SetPrintAreasCommand(targetSheetId, remapped)
            : new ClearPrintAreaCommand(targetSheetId);
    }

    public static GridRange? RemapPrintAreaToSheet(GridRange? printArea, SheetId targetSheetId) =>
        printArea is { } range
            ? new GridRange(
                new CellAddress(targetSheetId, range.Start.Row, range.Start.Col),
                new CellAddress(targetSheetId, range.End.Row, range.End.Col))
            : null;

    public static IReadOnlyList<GridRange> RemapPrintAreasToSheet(IReadOnlyList<GridRange> printAreas, SheetId targetSheetId) =>
        printAreas.Select(range => RemapPrintAreaToSheet(range, targetSheetId)!.Value).ToList();
}
