using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.PageLayout;

public sealed record PageSetupHeaderFooterRequest
{
    public WorksheetHeaderFooter Header { get; init; } = new("", "", "");
    public WorksheetHeaderFooter Footer { get; init; } = new("", "", "");
    public WorksheetHeaderFooter FirstPageHeader { get; init; } = new("", "", "");
    public WorksheetHeaderFooter FirstPageFooter { get; init; } = new("", "", "");
    public WorksheetHeaderFooter EvenPageHeader { get; init; } = new("", "", "");
    public WorksheetHeaderFooter EvenPageFooter { get; init; } = new("", "", "");
    public bool DifferentFirstPage { get; init; }
    public bool DifferentOddEvenPages { get; init; }
    public bool ScaleHeaderFooterWithDocument { get; init; } = true;
    public bool AlignHeaderFooterWithMargins { get; init; } = true;
    public WorksheetHeaderFooterPictureSet HeaderPictures { get; init; } = WorksheetHeaderFooterPictureSet.Empty;
    public WorksheetHeaderFooterPictureSet FooterPictures { get; init; } = WorksheetHeaderFooterPictureSet.Empty;
    public WorksheetHeaderFooterPictureSet FirstPageHeaderPictures { get; init; } = WorksheetHeaderFooterPictureSet.Empty;
    public WorksheetHeaderFooterPictureSet FirstPageFooterPictures { get; init; } = WorksheetHeaderFooterPictureSet.Empty;
    public WorksheetHeaderFooterPictureSet EvenPageHeaderPictures { get; init; } = WorksheetHeaderFooterPictureSet.Empty;
    public WorksheetHeaderFooterPictureSet EvenPageFooterPictures { get; init; } = WorksheetHeaderFooterPictureSet.Empty;
}

public sealed record PageSetupCommandRequest
{
    public GridRange? PrintArea { get; init; }
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
    public PageSetupHeaderFooterRequest HeaderFooter { get; init; } = new();
}

public sealed record PageSetupCommandPlan(
    GridRange? PrintArea,
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
        return new PageSetupCommandPlan(
            RemapPrintAreaToSheet(request.PrintArea, targetSheetId),
            BuildPrintAreaCommand(targetSheetId, request.PrintArea),
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
            new SetHeaderFooterCommand(
                targetSheetId,
                headerFooter.Header,
                headerFooter.Footer,
                headerFooter.FirstPageHeader,
                headerFooter.FirstPageFooter,
                headerFooter.EvenPageHeader,
                headerFooter.EvenPageFooter,
                headerFooter.DifferentFirstPage,
                headerFooter.DifferentOddEvenPages,
                headerFooter.ScaleHeaderFooterWithDocument,
                headerFooter.AlignHeaderFooterWithMargins,
                headerFooter.HeaderPictures,
                headerFooter.FooterPictures,
                headerFooter.FirstPageHeaderPictures,
                headerFooter.FirstPageFooterPictures,
                headerFooter.EvenPageHeaderPictures,
                headerFooter.EvenPageFooterPictures));
    }

    public static IWorkbookCommand BuildPrintAreaCommand(SheetId targetSheetId, GridRange? printArea) =>
        RemapPrintAreaToSheet(printArea, targetSheetId) is { } range
            ? new SetPrintAreaCommand(targetSheetId, range)
            : new ClearPrintAreaCommand(targetSheetId);

    public static GridRange? RemapPrintAreaToSheet(GridRange? printArea, SheetId targetSheetId) =>
        printArea is { } range
            ? new GridRange(
                new CellAddress(targetSheetId, range.Start.Row, range.Start.Col),
                new CellAddress(targetSheetId, range.End.Row, range.End.Col))
            : null;
}
