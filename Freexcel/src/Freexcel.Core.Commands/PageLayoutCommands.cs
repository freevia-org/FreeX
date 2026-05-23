using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>Sets the worksheet print area with undo support.</summary>
public sealed class SetPrintAreaCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _printArea;
    private GridRange? _previousPrintArea;

    public string Label => "Set Print Area";

    public SetPrintAreaCommand(SheetId sheetId, GridRange printArea)
    {
        _sheetId = sheetId;
        _printArea = printArea;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_printArea.Start.Sheet != _sheetId || _printArea.End.Sheet != _sheetId)
            return new CommandOutcome(false, "Print area must be on the target sheet.");

        var sheet = ctx.GetSheet(_sheetId);
        _previousPrintArea = sheet.PrintArea;
        sheet.PrintArea = _printArea;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        ctx.GetSheet(_sheetId).PrintArea = _previousPrintArea;
    }
}

/// <summary>Clears the worksheet print area with undo support.</summary>
public sealed class ClearPrintAreaCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private GridRange? _previousPrintArea;

    public string Label => "Clear Print Area";

    public ClearPrintAreaCommand(SheetId sheetId)
    {
        _sheetId = sheetId;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        _previousPrintArea = sheet.PrintArea;
        sheet.PrintArea = null;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        ctx.GetSheet(_sheetId).PrintArea = _previousPrintArea;
    }
}

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

/// <summary>Sets rows/columns repeated on each printed page.</summary>
public sealed class SetPrintTitlesCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly WorksheetRepeatRange? _rows;
    private readonly WorksheetRepeatRange? _columns;
    private WorksheetRepeatRange? _previousRows;
    private WorksheetRepeatRange? _previousColumns;

    public string Label => "Print Titles";

    public SetPrintTitlesCommand(SheetId sheetId, WorksheetRepeatRange? rows, WorksheetRepeatRange? columns)
    {
        _sheetId = sheetId;
        _rows = rows;
        _columns = columns;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_rows is { Start: 0 } or { End: 0 } || _columns is { Start: 0 } or { End: 0 })
            return new CommandOutcome(false, "Print title rows and columns must be 1-based.");

        var sheet = ctx.GetSheet(_sheetId);
        _previousRows = sheet.PrintTitleRows;
        _previousColumns = sheet.PrintTitleColumns;
        sheet.PrintTitleRows = _rows;
        sheet.PrintTitleColumns = _columns;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        sheet.PrintTitleRows = _previousRows;
        sheet.PrintTitleColumns = _previousColumns;
    }
}

/// <summary>Replaces worksheet manual page breaks with undo support.</summary>
public sealed class SetPageBreaksCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly IReadOnlyCollection<uint> _rowBreaks;
    private readonly IReadOnlyCollection<uint> _columnBreaks;
    private List<uint>? _previousRowBreaks;
    private List<uint>? _previousColumnBreaks;

    public string Label => "Page Breaks";

    public SetPageBreaksCommand(SheetId sheetId, IReadOnlyCollection<uint> rowBreaks, IReadOnlyCollection<uint> columnBreaks)
    {
        _sheetId = sheetId;
        _rowBreaks = rowBreaks;
        _columnBreaks = columnBreaks;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_rowBreaks.Any(b => b < 2 || b > CellAddress.MaxRow) ||
            _columnBreaks.Any(b => b < 2 || b > CellAddress.MaxCol))
        {
            return new CommandOutcome(false, "Page breaks must be inside the worksheet and after the first row/column.");
        }

        var sheet = ctx.GetSheet(_sheetId);
        _previousRowBreaks = sheet.RowPageBreaks.ToList();
        _previousColumnBreaks = sheet.ColumnPageBreaks.ToList();
        sheet.RowPageBreaks.Clear();
        sheet.ColumnPageBreaks.Clear();
        foreach (var rowBreak in _rowBreaks.Order())
            sheet.RowPageBreaks.Add(rowBreak);
        foreach (var columnBreak in _columnBreaks.Order())
            sheet.ColumnPageBreaks.Add(columnBreak);
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        sheet.RowPageBreaks.Clear();
        sheet.ColumnPageBreaks.Clear();
        if (_previousRowBreaks is not null)
            foreach (var rowBreak in _previousRowBreaks)
                sheet.RowPageBreaks.Add(rowBreak);
        if (_previousColumnBreaks is not null)
            foreach (var columnBreak in _previousColumnBreaks)
                sheet.ColumnPageBreaks.Add(columnBreak);
    }
}

public sealed class SetWorksheetBackgroundCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly WorksheetBackgroundImage _background;
    private WorksheetBackgroundImage? _previousBackground;
    private bool _applied;

    public string Label => "Sheet Background";

    public SetWorksheetBackgroundCommand(SheetId sheetId, WorksheetBackgroundImage background)
    {
        _sheetId = sheetId;
        _background = background;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (_background.ImageBytes.Length == 0)
            return new CommandOutcome(false, "Background image cannot be empty.");

        var sheet = ctx.GetSheet(_sheetId);
        _previousBackground = sheet.BackgroundImage;
        sheet.BackgroundImage = _background;
        _applied = true;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        ctx.GetSheet(_sheetId).BackgroundImage = _previousBackground;
        _applied = false;
    }
}

public sealed class ClearWorksheetBackgroundCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private WorksheetBackgroundImage? _previousBackground;
    private bool _applied;

    public string Label => "Clear Sheet Background";

    public ClearWorksheetBackgroundCommand(SheetId sheetId)
    {
        _sheetId = sheetId;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        _previousBackground = sheet.BackgroundImage;
        sheet.BackgroundImage = null;
        _applied = true;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied) return;
        ctx.GetSheet(_sheetId).BackgroundImage = _previousBackground;
        _applied = false;
    }
}

