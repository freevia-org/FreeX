using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Sets the worksheet page orientation with undo support.</summary>
public sealed class SetPageOrientationCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly WorksheetPageOrientation _orientation;
    private WorksheetPageOrientation _previousOrientation;

    public string Label => "Page Orientation";

    public SetPageOrientationCommand(SheetId sheetId, WorksheetPageOrientation orientation)
    {
        _sheetId = sheetId;
        _orientation = orientation;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (!Enum.IsDefined(_orientation))
            return PageSetupCommandGuards.PageOrientationNotSupported();

        var sheet = ctx.GetSheet(_sheetId);
        _previousOrientation = sheet.PageOrientation;
        sheet.PageOrientation = _orientation;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        ctx.GetSheet(_sheetId).PageOrientation = _previousOrientation;
    }
}

/// <summary>Sets the worksheet paper size with undo support.</summary>
public sealed class SetPaperSizeCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly WorksheetPaperSize _paperSize;
    private WorksheetPaperSize _previousPaperSize;

    public string Label => "Paper Size";

    public SetPaperSizeCommand(SheetId sheetId, WorksheetPaperSize paperSize)
    {
        _sheetId = sheetId;
        _paperSize = paperSize;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (!Enum.IsDefined(_paperSize))
            return PageSetupCommandGuards.PaperSizeNotSupported();

        var sheet = ctx.GetSheet(_sheetId);
        _previousPaperSize = sheet.PaperSize;
        sheet.PaperSize = _paperSize;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        ctx.GetSheet(_sheetId).PaperSize = _previousPaperSize;
    }
}

/// <summary>Sets worksheet page margins with undo support.</summary>
public sealed class SetPageMarginsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly WorksheetPageMargins _margins;
    private readonly double? _headerMargin;
    private readonly double? _footerMargin;
    private WorksheetPageMargins _previousMargins;
    private double _previousHeaderMargin;
    private double _previousFooterMargin;

    public string Label => "Page Margins";

    public SetPageMarginsCommand(
        SheetId sheetId,
        WorksheetPageMargins margins,
        double? headerMargin = null,
        double? footerMargin = null)
    {
        _sheetId = sheetId;
        _margins = margins;
        _headerMargin = headerMargin;
        _footerMargin = footerMargin;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_margins.Left < 0 || _margins.Right < 0 || _margins.Top < 0 || _margins.Bottom < 0)
            return PageSetupCommandGuards.PageMarginsCannotBeNegative();
        if (_headerMargin is < 0 || _footerMargin is < 0)
            return PageSetupCommandGuards.PageMarginsCannotBeNegative();

        var sheet = ctx.GetSheet(_sheetId);
        _previousMargins = sheet.PageMargins;
        _previousHeaderMargin = sheet.HeaderMargin;
        _previousFooterMargin = sheet.FooterMargin;
        sheet.PageMargins = _margins;
        if (_headerMargin is { } headerMargin)
            sheet.HeaderMargin = headerMargin;
        if (_footerMargin is { } footerMargin)
            sheet.FooterMargin = footerMargin;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        sheet.PageMargins = _previousMargins;
        if (_headerMargin is not null)
            sheet.HeaderMargin = _previousHeaderMargin;
        if (_footerMargin is not null)
            sheet.FooterMargin = _previousFooterMargin;
    }
}

/// <summary>Sets worksheet print gridline/headings options with undo support.</summary>
public sealed class SetPrintOptionsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly bool _printGridlines;
    private readonly bool _printHeadings;
    private bool _previousPrintGridlines;
    private bool _previousPrintHeadings;

    public string Label => "Print Options";

    public SetPrintOptionsCommand(SheetId sheetId, bool printGridlines, bool printHeadings)
    {
        _sheetId = sheetId;
        _printGridlines = printGridlines;
        _printHeadings = printHeadings;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        _previousPrintGridlines = sheet.PrintGridlines;
        _previousPrintHeadings = sheet.PrintHeadings;
        sheet.PrintGridlines = _printGridlines;
        sheet.PrintHeadings = _printHeadings;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        sheet.PrintGridlines = _previousPrintGridlines;
        sheet.PrintHeadings = _previousPrintHeadings;
    }
}

/// <summary>Sets worksheet scale-to-fit print options with undo support.</summary>
public sealed class SetScaleToFitCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly WorksheetScaleToFit _scaleToFit;
    private WorksheetScaleToFit _previousScaleToFit;

    public string Label => "Scale To Fit";

    public SetScaleToFitCommand(SheetId sheetId, WorksheetScaleToFit scaleToFit)
    {
        _sheetId = sheetId;
        _scaleToFit = scaleToFit;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_scaleToFit.ScalePercent is < 10 or > 400)
            return PageSetupCommandGuards.ScalePercentOutOfRange();

        if (_scaleToFit.FitToPagesWide is < 1 || _scaleToFit.FitToPagesTall is < 1)
            return PageSetupCommandGuards.FitToPageDimensionsTooSmall();

        var sheet = ctx.GetSheet(_sheetId);
        _previousScaleToFit = sheet.ScaleToFit;
        sheet.ScaleToFit = _scaleToFit;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        ctx.GetSheet(_sheetId).ScaleToFit = _previousScaleToFit;
    }
}

internal static class PageSetupCommandGuards
{
    private const string PageOrientationNotSupportedMessage = "Page orientation is not supported.";
    private const string PaperSizeNotSupportedMessage = "Paper size is not supported.";
    private const string PageMarginsCannotBeNegativeMessage = "Page margins cannot be negative.";
    private const string ScalePercentOutOfRangeMessage = "Scale percent must be between 10 and 400.";
    private const string FitToPageDimensionsTooSmallMessage = "Fit-to-page dimensions must be at least 1.";
    private const string PrintTitlesMustBeOneBasedMessage = "Print title rows and columns must be 1-based.";

    public static CommandOutcome PageOrientationNotSupported() =>
        new(false, PageOrientationNotSupportedMessage);

    public static CommandOutcome PaperSizeNotSupported() =>
        new(false, PaperSizeNotSupportedMessage);

    public static CommandOutcome PageMarginsCannotBeNegative() =>
        new(false, PageMarginsCannotBeNegativeMessage);

    public static CommandOutcome ScalePercentOutOfRange() =>
        new(false, ScalePercentOutOfRangeMessage);

    public static CommandOutcome FitToPageDimensionsTooSmall() =>
        new(false, FitToPageDimensionsTooSmallMessage);

    public static CommandOutcome PrintTitlesMustBeOneBased() =>
        new(false, PrintTitlesMustBeOneBasedMessage);
}
