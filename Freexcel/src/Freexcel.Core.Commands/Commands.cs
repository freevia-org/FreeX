using Freexcel.Core.Model;
using Freexcel.Core.Formula;

namespace Freexcel.Core.Commands;

/// <summary>
/// Command to edit the value or formula of one or more cells.
/// Captures previous cell state for undo.
/// </summary>
public sealed class EditCellsCommand : IWorkbookCommand, IAffectedCellsCommand
{
    private readonly SheetId _sheetId;
    private readonly IReadOnlyList<(CellAddress Address, Cell NewCell)> _edits;
    private List<(CellAddress Address, Cell? OldCell)>? _snapshot;

    public string Label => _edits.Count == 1 ? "Edit Cell" : $"Edit {_edits.Count} Cells";

    public IReadOnlyList<CellAddress> AffectedCells => _edits.Select(edit => edit.Address).ToArray();

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
        if (sheet.IsProtected)
        {
            foreach (var (addr, _) in _edits)
            {
                if (!CommandGuards.CanEditCell(ctx.Workbook, sheet, addr))
                    return new CommandOutcome(false, "The sheet is protected.");
            }
        }

        _snapshot = [];

        var affected = new List<CellAddress>();

        foreach (var (addr, newCell) in _edits)
        {
            // Save old state for undo
            var oldCell = sheet.GetCell(addr)?.Clone();
            _snapshot.Add((addr, oldCell));

            // Apply new state while preserving the cell's existing formatting.
            var appliedCell = newCell.Clone();
            if (oldCell is not null)
                appliedCell.StyleId = oldCell.StyleId;
            sheet.SetCell(addr, appliedCell);
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
        if (CommandGuards.RejectIfWorkbookStructureProtected(ctx.Workbook) is { } protectedOutcome)
            return protectedOutcome;

        var validationError = ctx.Workbook.ValidateSheetName(_name);
        if (validationError is not null)
            return new CommandOutcome(false, validationError);

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
    private readonly Dictionary<CellAddress, string> _formulaSnapshot = [];

    public string Label => $"Rename Sheet to '{_newName}'";

    public RenameSheetCommand(SheetId sheetId, string newName)
    {
        _sheetId = sheetId;
        _newName = newName;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (CommandGuards.RejectIfWorkbookStructureProtected(ctx.Workbook) is { } protectedOutcome)
            return protectedOutcome;

        var sheet = ctx.GetSheet(_sheetId);
        var validationError = ctx.Workbook.ValidateSheetName(_newName, _sheetId);
        if (validationError is not null)
            return new CommandOutcome(false, validationError);

        _oldName = sheet.Name;
        sheet.Name = _newName;
        _formulaSnapshot.Clear();
        RowColumnShiftHelpers.RewriteAllFormulas(
            ctx.Workbook, new RenameSheetOp(_oldName, _newName), _formulaSnapshot);
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_oldName is not null)
        {
            var sheet = ctx.GetSheet(_sheetId);
            sheet.Name = _oldName;
            RowColumnShiftHelpers.RestoreFormulas(ctx.Workbook, _formulaSnapshot);
        }
    }
}

/// <summary>Command to delete a sheet from the workbook.</summary>
public sealed class RemoveSheetCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private Sheet? _removedSheet;
    private int _removedIndex;
    private Dictionary<string, NamedRangeSnapshot>? _namedRangeSnapshot;

    public string Label => "Delete Sheet";

    public RemoveSheetCommand(SheetId sheetId) => _sheetId = sheetId;

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (CommandGuards.RejectIfWorkbookStructureProtected(ctx.Workbook) is { } protectedOutcome)
            return protectedOutcome;

        if (ctx.Workbook.Sheets.Count <= 1)
            return new CommandOutcome(false, "Cannot delete the only sheet.");

        var sheet = ctx.GetSheet(_sheetId);
        _removedSheet = sheet;
        var sheets = ctx.Workbook.Sheets;
        for (int i = 0; i < sheets.Count; i++)
            if (sheets[i].Id == _sheetId) { _removedIndex = i; break; }
        _namedRangeSnapshot = RowColumnShiftHelpers.CaptureNamedRanges(ctx.Workbook);
        foreach (var (name, range) in ctx.Workbook.NamedRanges.ToList())
        {
            if (range.Start.Sheet == _sheetId)
                ctx.Workbook.RemoveNamedRange(name);
        }
        ctx.Workbook.RemoveSheet(_sheetId);
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_removedSheet is not null)
        {
            ctx.Workbook.InsertSheet(_removedIndex, _removedSheet);
            RowColumnShiftHelpers.RestoreNamedRanges(ctx.Workbook, _namedRangeSnapshot);
        }
    }
}

/// <summary>Command to move a sheet tab from one workbook position to another.</summary>
public sealed class MoveSheetCommand : IWorkbookCommand
{
    private readonly int _fromIndex;
    private readonly int _toIndex;
    private bool _applied;

    public string Label => "Move Sheet";

    public MoveSheetCommand(int fromIndex, int toIndex)
    {
        _fromIndex = fromIndex;
        _toIndex = toIndex;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (CommandGuards.RejectIfWorkbookStructureProtected(ctx.Workbook) is { } protectedOutcome)
            return protectedOutcome;

        if (!IsValidIndex(ctx.Workbook, _fromIndex) || !IsValidIndex(ctx.Workbook, _toIndex))
            return new CommandOutcome(false, "Sheet index is out of range.");

        if (_fromIndex == _toIndex)
            return new CommandOutcome(true);

        ctx.Workbook.MoveSheet(_fromIndex, _toIndex);
        _applied = true;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied || _fromIndex == _toIndex)
            return;

        ctx.Workbook.MoveSheet(_toIndex, _fromIndex);
        _applied = false;
    }

    private static bool IsValidIndex(Workbook workbook, int index) =>
        index >= 0 && index < workbook.Sheets.Count;
}

/// <summary>Command to hide or unhide a worksheet.</summary>
public sealed class SetSheetHiddenCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly bool _hidden;
    private bool? _previousHidden;

    public string Label => _hidden ? "Hide Sheet" : "Unhide Sheet";

    public SetSheetHiddenCommand(SheetId sheetId, bool hidden)
    {
        _sheetId = sheetId;
        _hidden = hidden;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (CommandGuards.RejectIfWorkbookStructureProtected(ctx.Workbook) is { } protectedOutcome)
            return protectedOutcome;

        var sheet = ctx.GetSheet(_sheetId);
        if (_hidden && !ctx.Workbook.Sheets.Any(s => s.Id != _sheetId && !s.IsHidden))
            return new CommandOutcome(false, "Cannot hide the only visible sheet.");

        _previousHidden = sheet.IsHidden;
        sheet.IsHidden = _hidden;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousHidden is null)
            return;

        ctx.GetSheet(_sheetId).IsHidden = _previousHidden.Value;
    }
}

/// <summary>Command to set or clear a worksheet tab color.</summary>
public sealed class SetSheetTabColorCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly CellColor? _color;
    private CellColor? _previousColor;
    private bool _hadPreviousColor;

    public string Label => "Set Sheet Tab Color";

    public SetSheetTabColorCommand(SheetId sheetId, CellColor? color)
    {
        _sheetId = sheetId;
        _color = color;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        if (CommandGuards.RejectIfWorkbookStructureProtected(ctx.Workbook) is { } protectedOutcome)
            return protectedOutcome;

        var sheet = ctx.GetSheet(_sheetId);
        _previousColor = sheet.TabColor;
        _hadPreviousColor = sheet.TabColor.HasValue;
        sheet.TabColor = _color;
        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        ctx.GetSheet(_sheetId).TabColor = _hadPreviousColor ? _previousColor : null;
    }
}
