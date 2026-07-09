using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>Sets the worksheet print area with undo support.</summary>
public sealed class SetPrintAreaCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _printArea;
    private List<GridRange>? _previousPrintAreas;

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
        _previousPrintAreas = sheet.PrintAreas.ToList();
        sheet.PrintArea = _printArea;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        ctx.GetSheet(_sheetId).SetPrintAreas(_previousPrintAreas ?? []);
    }
}

/// <summary>
/// Sets all of the worksheet's print areas (single- or multi-region) with undo support. Unlike
/// <see cref="SetPrintAreaCommand"/>, which always collapses to one region, this preserves every
/// region passed in (mirroring Excel's comma-separated <c>_xlnm.Print_Area</c> defined name).
/// </summary>
public sealed class SetPrintAreasCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly IReadOnlyList<GridRange> _printAreas;
    private List<GridRange>? _previousPrintAreas;

    public string Label => "Set Print Area";

    public SetPrintAreasCommand(SheetId sheetId, IReadOnlyList<GridRange> printAreas)
    {
        _sheetId = sheetId;
        _printAreas = printAreas;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        foreach (var area in _printAreas)
        {
            if (area.Start.Sheet != _sheetId || area.End.Sheet != _sheetId)
                return new CommandOutcome(false, "Print area must be on the target sheet.");
        }

        var sheet = ctx.GetSheet(_sheetId);
        _previousPrintAreas = sheet.PrintAreas.ToList();
        sheet.SetPrintAreas(_printAreas);
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        ctx.GetSheet(_sheetId).SetPrintAreas(_previousPrintAreas ?? []);
    }
}

/// <summary>Clears the worksheet print area with undo support.</summary>
public sealed class ClearPrintAreaCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private List<GridRange>? _previousPrintAreas;

    public string Label => "Clear Print Area";

    public ClearPrintAreaCommand(SheetId sheetId)
    {
        _sheetId = sheetId;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        _previousPrintAreas = sheet.PrintAreas.ToList();
        sheet.PrintArea = null;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        ctx.GetSheet(_sheetId).SetPrintAreas(_previousPrintAreas ?? []);
    }
}

