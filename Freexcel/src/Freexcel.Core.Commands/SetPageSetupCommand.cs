using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public sealed class SetPageSetupCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly WorksheetPageOrientation _orientation;
    private readonly WorksheetPaperSize _paperSize;
    private readonly WorksheetPageMargins _margins;
    private readonly double _headerMargin;
    private readonly double _footerMargin;
    private readonly bool _printGridlines;
    private readonly bool _printHeadings;
    private readonly WorksheetScaleToFit _scaleToFit;
    private readonly WorksheetRepeatRange? _printTitleRows;
    private readonly WorksheetRepeatRange? _printTitleColumns;
    private readonly bool _centerHorizontally;
    private readonly bool _centerVertically;
    private readonly WorksheetPageOrder _pageOrder;
    private readonly int? _firstPageNumber;
    private readonly bool _printBlackAndWhite;
    private readonly bool _printDraftQuality;
    private readonly int? _printQualityDpi;
    private readonly WorksheetPrintErrorValue _printErrorValue;
    private readonly WorksheetPrintComments _printComments;

    private WorksheetPageOrientation _previousOrientation;
    private WorksheetPaperSize _previousPaperSize;
    private WorksheetPageMargins _previousMargins;
    private double _previousHeaderMargin;
    private double _previousFooterMargin;
    private bool _previousPrintGridlines;
    private bool _previousPrintHeadings;
    private WorksheetScaleToFit _previousScaleToFit;
    private WorksheetRepeatRange? _previousPrintTitleRows;
    private WorksheetRepeatRange? _previousPrintTitleColumns;
    private bool _previousCenterHorizontally;
    private bool _previousCenterVertically;
    private WorksheetPageOrder _previousPageOrder;
    private int? _previousFirstPageNumber;
    private bool _previousPrintBlackAndWhite;
    private bool _previousPrintDraftQuality;
    private int? _previousPrintQualityDpi;
    private WorksheetPrintErrorValue _previousPrintErrorValue;
    private WorksheetPrintComments _previousPrintComments;
    private bool _applied;

    public string Label => "Page Setup";

    public SetPageSetupCommand(
        SheetId sheetId,
        WorksheetPageOrientation orientation,
        WorksheetPaperSize paperSize,
        WorksheetPageMargins margins,
        bool printGridlines,
        bool printHeadings,
        WorksheetScaleToFit scaleToFit,
        WorksheetRepeatRange? printTitleRows,
        WorksheetRepeatRange? printTitleColumns,
        bool centerHorizontally = false,
        bool centerVertically = false,
        WorksheetPageOrder pageOrder = WorksheetPageOrder.DownThenOver,
        int? firstPageNumber = null,
        double headerMargin = 0.3,
        double footerMargin = 0.3,
        bool printBlackAndWhite = false,
        bool printDraftQuality = false,
        int? printQualityDpi = null,
        WorksheetPrintErrorValue printErrorValue = WorksheetPrintErrorValue.Displayed,
        WorksheetPrintComments printComments = WorksheetPrintComments.None)
    {
        _sheetId = sheetId;
        _orientation = orientation;
        _paperSize = paperSize;
        _margins = margins;
        _headerMargin = headerMargin;
        _footerMargin = footerMargin;
        _printGridlines = printGridlines;
        _printHeadings = printHeadings;
        _scaleToFit = scaleToFit;
        _printTitleRows = printTitleRows;
        _printTitleColumns = printTitleColumns;
        _centerHorizontally = centerHorizontally;
        _centerVertically = centerVertically;
        _pageOrder = pageOrder;
        _firstPageNumber = firstPageNumber;
        _printBlackAndWhite = printBlackAndWhite;
        _printDraftQuality = printDraftQuality;
        _printQualityDpi = printQualityDpi;
        _printErrorValue = printErrorValue;
        _printComments = printComments;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (!Enum.IsDefined(_orientation))
            return new CommandOutcome(false, "Page orientation is not supported.");
        if (!Enum.IsDefined(_paperSize))
            return new CommandOutcome(false, "Paper size is not supported.");
        if (!Enum.IsDefined(_pageOrder))
            return new CommandOutcome(false, "Page order is not supported.");
        if (!Enum.IsDefined(_printErrorValue))
            return new CommandOutcome(false, "Printed cell-error display option is not supported.");
        if (!Enum.IsDefined(_printComments))
            return new CommandOutcome(false, "Printed comments option is not supported.");
        if (_margins.Left < 0 || _margins.Right < 0 || _margins.Top < 0 || _margins.Bottom < 0)
            return new CommandOutcome(false, "Page margins cannot be negative.");
        if (_headerMargin < 0 || _footerMargin < 0)
            return new CommandOutcome(false, "Header and footer margins cannot be negative.");
        if (_scaleToFit.ScalePercent is < 10 or > 400)
            return new CommandOutcome(false, "Scale percent must be between 10 and 400.");
        if (_scaleToFit.FitToPagesWide is < 1 || _scaleToFit.FitToPagesTall is < 1)
            return new CommandOutcome(false, "Fit-to-page dimensions must be at least 1.");
        if (_printTitleRows is { Start: 0 } or { End: 0 } || _printTitleColumns is { Start: 0 } or { End: 0 })
            return new CommandOutcome(false, "Print title rows and columns must be 1-based.");
        if (_firstPageNumber is 0)
            return new CommandOutcome(false, "First page number cannot be zero.");
        if (_printQualityDpi is <= 0)
            return new CommandOutcome(false, "Print quality must be a positive DPI value.");

        var sheet = ctx.GetSheet(_sheetId);
        _previousOrientation = sheet.PageOrientation;
        _previousPaperSize = sheet.PaperSize;
        _previousMargins = sheet.PageMargins;
        _previousHeaderMargin = sheet.HeaderMargin;
        _previousFooterMargin = sheet.FooterMargin;
        _previousPrintGridlines = sheet.PrintGridlines;
        _previousPrintHeadings = sheet.PrintHeadings;
        _previousScaleToFit = sheet.ScaleToFit;
        _previousPrintTitleRows = sheet.PrintTitleRows;
        _previousPrintTitleColumns = sheet.PrintTitleColumns;
        _previousCenterHorizontally = sheet.CenterHorizontallyOnPage;
        _previousCenterVertically = sheet.CenterVerticallyOnPage;
        _previousPageOrder = sheet.PageOrder;
        _previousFirstPageNumber = sheet.FirstPageNumber;
        _previousPrintBlackAndWhite = sheet.PrintBlackAndWhite;
        _previousPrintDraftQuality = sheet.PrintDraftQuality;
        _previousPrintQualityDpi = sheet.PrintQualityDpi;
        _previousPrintErrorValue = sheet.PrintErrorValue;
        _previousPrintComments = sheet.PrintComments;

        sheet.PageOrientation = _orientation;
        sheet.PaperSize = _paperSize;
        sheet.PageMargins = _margins;
        sheet.HeaderMargin = _headerMargin;
        sheet.FooterMargin = _footerMargin;
        sheet.PrintGridlines = _printGridlines;
        sheet.PrintHeadings = _printHeadings;
        sheet.ScaleToFit = _scaleToFit;
        sheet.PrintTitleRows = _printTitleRows;
        sheet.PrintTitleColumns = _printTitleColumns;
        sheet.CenterHorizontallyOnPage = _centerHorizontally;
        sheet.CenterVerticallyOnPage = _centerVertically;
        sheet.PageOrder = _pageOrder;
        sheet.FirstPageNumber = _firstPageNumber;
        sheet.PrintBlackAndWhite = _printBlackAndWhite;
        sheet.PrintDraftQuality = _printDraftQuality;
        sheet.PrintQualityDpi = _printQualityDpi;
        sheet.PrintErrorValue = _printErrorValue;
        sheet.PrintComments = _printComments;
        _applied = true;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        var sheet = ctx.GetSheet(_sheetId);
        sheet.PageOrientation = _previousOrientation;
        sheet.PaperSize = _previousPaperSize;
        sheet.PageMargins = _previousMargins;
        sheet.HeaderMargin = _previousHeaderMargin;
        sheet.FooterMargin = _previousFooterMargin;
        sheet.PrintGridlines = _previousPrintGridlines;
        sheet.PrintHeadings = _previousPrintHeadings;
        sheet.ScaleToFit = _previousScaleToFit;
        sheet.PrintTitleRows = _previousPrintTitleRows;
        sheet.PrintTitleColumns = _previousPrintTitleColumns;
        sheet.CenterHorizontallyOnPage = _previousCenterHorizontally;
        sheet.CenterVerticallyOnPage = _previousCenterVertically;
        sheet.PageOrder = _previousPageOrder;
        sheet.FirstPageNumber = _previousFirstPageNumber;
        sheet.PrintBlackAndWhite = _previousPrintBlackAndWhite;
        sheet.PrintDraftQuality = _previousPrintDraftQuality;
        sheet.PrintQualityDpi = _previousPrintQualityDpi;
        sheet.PrintErrorValue = _previousPrintErrorValue;
        sheet.PrintComments = _previousPrintComments;
        _applied = false;
    }
}
