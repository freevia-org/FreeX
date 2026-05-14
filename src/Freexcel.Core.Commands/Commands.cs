using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>
/// Command to edit the value or formula of one or more cells.
/// Captures previous cell state for undo.
/// </summary>
public sealed class EditCellsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly IReadOnlyList<(CellAddress Address, Cell NewCell)> _edits;
    private List<(CellAddress Address, Cell? OldCell)>? _snapshot;

    public string Label => _edits.Count == 1 ? "Edit Cell" : $"Edit {_edits.Count} Cells";

    public EditCellsCommand(SheetId sheetId, IReadOnlyList<(CellAddress Address, Cell NewCell)> edits)
    {
        _sheetId = sheetId;
        _edits = edits;
    }

    /// <summary>Convenience constructor for editing a single cell value.</summary>
    public EditCellsCommand(SheetId sheetId, CellAddress address, ScalarValue value)
        : this(sheetId, [(address, Cell.FromValue(value))])
    {
    }

    /// <summary>Convenience factory for editing a single cell value.</summary>
    public static EditCellsCommand ForValue(SheetId sheetId, CellAddress address, ScalarValue value)
        => new(sheetId, address, value);

    /// <summary>Convenience constructor for setting a single cell formula.</summary>
    public static EditCellsCommand ForFormula(SheetId sheetId, CellAddress address, string formulaText)
    {
        return new EditCellsCommand(sheetId, [(address, Cell.FromFormula(formulaText))]);
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        _snapshot = [];

        var affected = new List<CellAddress>();

        foreach (var (addr, newCell) in _edits)
        {
            // Save old state for undo
            var oldCell = sheet.GetCell(addr)?.Clone();
            _snapshot.Add((addr, oldCell));

            // Apply new state
            sheet.SetCell(addr, newCell.Clone());
            affected.Add(addr);
        }

        return new CommandOutcome(true, AffectedCells: affected);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null) return;

        var sheet = ctx.GetSheet(_sheetId);

        foreach (var (addr, oldCell) in _snapshot)
        {
            if (oldCell is null)
                sheet.ClearCell(addr);
            else
                sheet.SetCell(addr, oldCell.Clone());
        }
    }
}

/// <summary>Command to add a new sheet to the workbook.</summary>
public sealed class AddSheetCommand : IWorkbookCommand
{
    private readonly string _name;
    private SheetId? _addedSheetId;

    public string Label => $"Add Sheet '{_name}'";

    public AddSheetCommand(string name) => _name = name;

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.Workbook.AddSheet(_name);
        _addedSheetId = sheet.Id;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_addedSheetId.HasValue)
            ctx.Workbook.RemoveSheet(_addedSheetId.Value);
    }
}

/// <summary>Command to rename a sheet.</summary>
public sealed class RenameSheetCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly string _newName;
    private string? _oldName;

    public string Label => $"Rename Sheet to '{_newName}'";

    public RenameSheetCommand(SheetId sheetId, string newName)
    {
        _sheetId = sheetId;
        _newName = newName;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        _oldName = sheet.Name;
        sheet.Name = _newName;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_oldName is not null)
        {
            var sheet = ctx.GetSheet(_sheetId);
            sheet.Name = _oldName;
        }
    }
}

/// <summary>Command to delete a sheet from the workbook.</summary>
public sealed class RemoveSheetCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private string? _removedName;
    private int _removedIndex;

    public string Label => "Delete Sheet";

    public RemoveSheetCommand(SheetId sheetId) => _sheetId = sheetId;

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        _removedName = sheet.Name;
        var sheets = ctx.Workbook.Sheets;
        for (int i = 0; i < sheets.Count; i++)
            if (sheets[i].Id == _sheetId) { _removedIndex = i; break; }
        ctx.Workbook.RemoveSheet(_sheetId);
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_removedName is not null)
            ctx.Workbook.InsertSheet(_removedIndex, _removedName);
    }
}
