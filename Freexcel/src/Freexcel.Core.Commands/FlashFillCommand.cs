using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

/// <summary>
/// Implements the Flash Fill (Ctrl+E) command.
/// Scans the fill column for user-provided examples, calls <see cref="FlashFillService"/>
/// to detect a transformation pattern, and writes the inferred values into the blank cells.
/// Fully undo-able via Revert.
/// </summary>
public sealed class FlashFillCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly uint _fillColIndex;
    private readonly uint _sourceColIndex;
    private readonly uint _startRow;
    private readonly uint _endRow;

    /// <summary>Snapshot of cells that were written during Apply, used to revert.</summary>
    private List<(CellAddress Address, Cell? OldCell)>? _snapshot;

    public string Label => "Flash Fill";

    /// <param name="sheetId">The sheet to operate on.</param>
    /// <param name="fillColIndex">Column the user typed examples into (1-based).</param>
    /// <param name="sourceColIndex">Adjacent source data column (1-based).</param>
    /// <param name="startRow">First row of the range to consider (1-based).</param>
    /// <param name="endRow">Last row of the range to consider (1-based, inclusive).</param>
    public FlashFillCommand(
        SheetId sheetId,
        uint fillColIndex,
        uint sourceColIndex,
        uint startRow,
        uint endRow)
    {
        _sheetId = sheetId;
        _fillColIndex = fillColIndex;
        _sourceColIndex = sourceColIndex;
        _startRow = startRow;
        _endRow = endRow;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);

        // 1. Scan fill column for non-blank rows (examples) and blank rows (rows to fill).
        var examplePairs = new List<(string Source, string Expected)>();
        var rowsToFill = new List<uint>();
        var sourcesToFill = new List<string>();

        for (uint row = _startRow; row <= _endRow; row++)
        {
            var fillAddr = new CellAddress(_sheetId, row, _fillColIndex);
            var sourceAddr = new CellAddress(_sheetId, row, _sourceColIndex);

            var fillValue = sheet.GetValue(fillAddr);
            var sourceValue = sheet.GetValue(sourceAddr);

            var sourceStr = ScalarToString(sourceValue);

            if (fillValue is not BlankValue and not null)
            {
                // This row has a user-typed example
                var expectedStr = ScalarToString(fillValue);
                if (sourceStr.Length > 0 || expectedStr.Length > 0)
                    examplePairs.Add((sourceStr, expectedStr));
            }
            else
            {
                // Blank fill cell — candidate for filling
                rowsToFill.Add(row);
                sourcesToFill.Add(sourceStr);
            }
        }

        if (examplePairs.Count == 0)
            return new CommandOutcome(false, "No examples found. Type at least one value in the fill column.");

        if (rowsToFill.Count == 0)
            return new CommandOutcome(true); // Nothing to fill — already complete

        // 2. Detect pattern and compute filled values
        var filled = FlashFillService.Fill(examplePairs, sourcesToFill);
        if (filled is null)
            return new CommandOutcome(false, "Could not detect a pattern from the provided examples.");

        // 3. Write filled values, capturing snapshot for undo
        _snapshot = [];
        var affected = new List<CellAddress>();

        for (int i = 0; i < rowsToFill.Count; i++)
        {
            var addr = new CellAddress(_sheetId, rowsToFill[i], _fillColIndex);
            _snapshot.Add((addr, sheet.GetCell(addr)?.Clone()));

            var newCell = Cell.FromValue(new TextValue(filled[i]));
            sheet.SetCell(addr, newCell);
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

    private static string ScalarToString(ScalarValue value) => value switch
    {
        TextValue t => t.Value,
        NumberValue n => n.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
        BoolValue b => b.Value ? "TRUE" : "FALSE",
        _ => string.Empty
    };
}
