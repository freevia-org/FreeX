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

