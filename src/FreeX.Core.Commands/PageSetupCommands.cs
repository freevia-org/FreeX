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
            return new CommandOutcome(false, "Page orientation is not supported.");

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
            return new CommandOutcome(false, "Paper size is not supported.");

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
    private WorksheetPageMargins _previousMargins;

    public string Label => "Page Margins";

    public SetPageMarginsCommand(SheetId sheetId, WorksheetPageMargins margins)
    {
        _sheetId = sheetId;
        _margins = margins;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_margins.Left < 0 || _margins.Right < 0 || _margins.Top < 0 || _margins.Bottom < 0)
            return new CommandOutcome(false, "Page margins cannot be negative.");

        var sheet = ctx.GetSheet(_sheetId);
        _previousMargins = sheet.PageMargins;
        sheet.PageMargins = _margins;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        ctx.GetSheet(_sheetId).PageMargins = _previousMargins;
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
            return new CommandOutcome(false, "Scale percent must be between 10 and 400.");

        if (_scaleToFit.FitToPagesWide is < 1 || _scaleToFit.FitToPagesTall is < 1)
            return new CommandOutcome(false, "Fit-to-page dimensions must be at least 1.");

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
