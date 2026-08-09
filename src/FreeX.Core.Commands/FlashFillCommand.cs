using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// The non-mutating result of <see cref="FlashFillCommand.Preview"/>: the cells Flash Fill would
/// write and the values it would write into them, computed from the sheet's current contents
/// without committing anything. <paramref name="Cells"/> pairs each target address with its
/// inferred value, in the same order <see cref="FlashFillCommand.Apply"/> would write them.
/// </summary>
public readonly record struct FlashFillPreview(
    bool Success,
    string? Error,
    IReadOnlyList<(CellAddress Address, string Value)> Cells);

/// <summary>
/// Implements the Flash Fill (Ctrl+E) command.
/// Scans the fill column for user-provided examples, calls <see cref="FlashFillService"/>
/// to detect a transformation pattern, and writes the inferred values into the blank cells.
/// Fully undo-able via Revert.
/// </summary>
public sealed class FlashFillCommand : IWorkbookCommand, IEstimatesMemory
{
    // R125-commands-undo-byte-budget: _snapshot below captures a (Cell?) per written cell, the
    // same shape PasteCellsCommand/FillCellsCommand use 300 bytes/cell for. A Flash Fill run over
    // a large column should count proportionally, not the flat 200-byte default.
    private const int BytesPerCell = 300;

    private readonly SheetId _sheetId;
    private readonly uint _fillColIndex;
    private readonly uint _sourceColIndex;
    private readonly uint _startRow;
    private readonly uint _endRow;

    /// <summary>Snapshot of cells that were written during Apply, used to revert.</summary>
    private List<(CellAddress Address, Cell? OldCell)>? _snapshot;

    public string Label => "Flash Fill";

    public int EstimatedBytes => (int)Math.Min((long)(_snapshot?.Count ?? (int)(_endRow - _startRow + 1)) * BytesPerCell, int.MaxValue);

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
        var detection = DetectFill(ctx);
        if (!detection.Success)
            return new CommandOutcome(false, detection.Error);

        var sheet = ctx.GetSheet(_sheetId);
        var rowsToFill = detection.RowsToFill!;
        var filled = detection.FilledValues!;

        // Write filled values, capturing snapshot for undo
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

    /// <summary>
    /// Computes what <see cref="Apply"/> would fill without writing anything to the sheet. This is
    /// the pattern-detection step factored out so a live "as you type" Flash Fill preview can be
    /// rendered from the current (possibly still-being-edited) sheet contents — the caller is
    /// responsible for showing/hiding the preview UI; this only supplies the target cells and values.
    /// </summary>
    public FlashFillPreview Preview(ICommandContext ctx)
    {
        var detection = DetectFill(ctx);
        return detection.Success
            ? new FlashFillPreview(true, null, detection.RowsToFill!
                .Select((row, i) => (new CellAddress(_sheetId, row, _fillColIndex), detection.FilledValues![i]))
                .ToList())
            : new FlashFillPreview(false, detection.Error, []);
    }

    private readonly record struct FillDetection(
        bool Success,
        string? Error,
        IReadOnlyList<uint>? RowsToFill,
        IReadOnlyList<string>? FilledValues);

    /// <summary>
    /// Scans the fill column for user-provided examples, detects the transformation pattern, and
    /// computes the values that should be written into the blank rows — without mutating the sheet.
    /// Shared by <see cref="Apply"/> (which commits the result) and <see cref="Preview"/> (which does not).
    /// </summary>
    private FillDetection DetectFill(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);

        // 1. Scan fill column for non-blank rows (examples) and blank rows (rows to fill).
        var examplePairs = new List<(string Source, string Expected)>();
        var exampleRows = new List<uint>();
        var exampleOutputs = new List<string>();
        var rowsToFill = new List<uint>();
        var sourcesToFill = new List<string>();

        for (uint row = _startRow; row <= _endRow; row++)
        {
            var fillAddr = new CellAddress(_sheetId, row, _fillColIndex);
            var sourceAddr = new CellAddress(_sheetId, row, _sourceColIndex);

            var fillValue = sheet.GetValue(fillAddr);
            var sourceValue = sheet.GetValue(sourceAddr);

            var sourceStr = ScalarToString(sourceValue);

            if (!IsBlankForFlashFill(fillValue))
            {
                // This row has a user-typed example
                var expectedStr = ScalarToString(fillValue);
                if (expectedStr.Length > 0)
                {
                    exampleRows.Add(row);
                    exampleOutputs.Add(expectedStr);

                    if (sourceStr.Length > 0)
                        examplePairs.Add((sourceStr, expectedStr));
                }
            }
            else
            {
                // Blank fill cells with no source data are left blank instead of aborting the whole range.
                if (sourceStr.Length > 0)
                {
                    rowsToFill.Add(row);
                    sourcesToFill.Add(sourceStr);
                }
            }
        }

        if (exampleOutputs.Count == 0)
            return new FillDetection(false, "No examples found. Type at least one value in the fill column.", null, null);

        if (rowsToFill.Count == 0)
            return new FillDetection(true, null, [], []); // Nothing to fill — already complete

        if (rowsToFill.Any(row => !CommandGuards.CanEditCell(
                ctx.Workbook,
                sheet,
                new CellAddress(_sheetId, row, _fillColIndex))))
        {
            return new FillDetection(false, "The sheet is protected.", null, null);
        }

        // 2. Detect pattern and compute filled values
        var filled = TryFillFromImmediateLeftColumns(sheet, exampleRows, exampleOutputs, rowsToFill)
            ?? FlashFillService.Fill(examplePairs, sourcesToFill);
        if (filled is null)
            return new FillDetection(false, "Could not detect a pattern from the provided examples.", null, null);

        return new FillDetection(true, null, rowsToFill, filled);
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

    private static bool IsBlankForFlashFill(ScalarValue? value) =>
        value is null or BlankValue || value is TextValue { Value: "" };

    private IReadOnlyList<string>? TryFillFromImmediateLeftColumns(
        Sheet sheet,
        IReadOnlyList<uint> exampleRows,
        IReadOnlyList<string> exampleOutputs,
        IReadOnlyList<uint> rowsToFill)
    {
        if (_fillColIndex < 3 || _sourceColIndex != _fillColIndex - 1)
            return null;

        if (_fillColIndex >= 4)
        {
            var threeLeftCol0 = _fillColIndex - 3;
            var threeLeftCol1 = _fillColIndex - 2;
            var threeLeftCol2 = _fillColIndex - 1;
            var threeColumnResult = TryFillFromImmediateLeftColumns(
                sheet,
                exampleRows,
                exampleOutputs,
                rowsToFill,
                threeLeftCol0,
                threeLeftCol1,
                threeLeftCol2);
            if (threeColumnResult is not null)
                return threeColumnResult;
        }

        var leftCol0 = _fillColIndex - 2;
        var leftCol1 = _fillColIndex - 1;

        return TryFillFromImmediateLeftColumns(
            sheet,
            exampleRows,
            exampleOutputs,
            rowsToFill,
            leftCol0,
            leftCol1);
    }

    private static IReadOnlyList<string>? TryFillFromImmediateLeftColumns(
        Sheet sheet,
        IReadOnlyList<uint> exampleRows,
        IReadOnlyList<string> exampleOutputs,
        IReadOnlyList<uint> rowsToFill,
        uint leftCol0,
        uint leftCol1)
    {
        var exampleSources = new List<IReadOnlyList<string>>(exampleRows.Count);
        foreach (var row in exampleRows)
        {
            var sources = GetPopulatedLeftSources(sheet, row, leftCol0, leftCol1);
            if (sources is null)
                return null;

            exampleSources.Add(sources);
        }

        var remainingSources = new List<IReadOnlyList<string>>(rowsToFill.Count);
        foreach (var row in rowsToFill)
        {
            var sources = GetPopulatedLeftSources(sheet, row, leftCol0, leftCol1);
            if (sources is null)
                return null;

            remainingSources.Add(sources);
        }

        return FlashFillService.FillFromColumns(exampleSources, exampleOutputs, remainingSources);
    }

    private static IReadOnlyList<string>? TryFillFromImmediateLeftColumns(
        Sheet sheet,
        IReadOnlyList<uint> exampleRows,
        IReadOnlyList<string> exampleOutputs,
        IReadOnlyList<uint> rowsToFill,
        uint leftCol0,
        uint leftCol1,
        uint leftCol2)
    {
        var exampleSources = new List<IReadOnlyList<string>>(exampleRows.Count);
        foreach (var row in exampleRows)
        {
            var sources = GetPopulatedLeftSources(sheet, row, leftCol0, leftCol1, leftCol2);
            if (sources is null)
                return null;

            exampleSources.Add(sources);
        }

        var remainingSources = new List<IReadOnlyList<string>>(rowsToFill.Count);
        foreach (var row in rowsToFill)
        {
            var sources = GetPopulatedLeftSources(sheet, row, leftCol0, leftCol1, leftCol2);
            if (sources is null)
                return null;

            remainingSources.Add(sources);
        }

        return FlashFillService.FillFromColumns(exampleSources, exampleOutputs, remainingSources);
    }

    private static IReadOnlyList<string>? GetPopulatedLeftSources(Sheet sheet, uint row, uint leftCol0, uint leftCol1)
    {
        var first = ScalarToString(sheet.GetValue(row, leftCol0));
        var second = ScalarToString(sheet.GetValue(row, leftCol1));

        return first.Length > 0 && second.Length > 0
            ? [first, second]
            : null;
    }

    private static IReadOnlyList<string>? GetPopulatedLeftSources(
        Sheet sheet,
        uint row,
        uint leftCol0,
        uint leftCol1,
        uint leftCol2)
    {
        var first = ScalarToString(sheet.GetValue(row, leftCol0));
        var second = ScalarToString(sheet.GetValue(row, leftCol1));
        var third = ScalarToString(sheet.GetValue(row, leftCol2));

        return first.Length > 0 && second.Length > 0 && third.Length > 0
            ? [first, second, third]
            : null;
    }
}
