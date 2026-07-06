using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

/// <summary>
/// Command to edit the value or formula of one or more cells.
/// Captures previous cell state for undo.
/// </summary>
public sealed class EditCellsCommand : IWorkbookCommand, IAffectedCellsCommand
{
    private readonly SheetId _sheetId;
    private readonly IReadOnlyList<(CellAddress Address, Cell NewCell)> _edits;
    private readonly IReadOnlyList<CellAddress> _affectedCells;
    private List<(CellAddress Address, Cell? OldCell, StyleId? OldStyleOnly, bool HadRichTextRuns, IReadOnlyList<CellTextRun>? OldRichTextRuns, bool HadHyperlink, string? OldHyperlink, bool HadHyperlinkMetadata, HyperlinkMetadata? OldHyperlinkMetadata)>? _snapshot;

    // N33/N34: sub-commands run in the same undo transaction as the edit itself — table
    // auto-expand (growing a table when the edit lands one row/column past its current range)
    // and calculated-column formula propagation (spreading a newly typed formula to the rest of
    // the calculated column's data-body rows). Both are populated by StructuredTableEditEffects.Apply
    // and reverted (in reverse order) before the base edit is rolled back.
    private readonly List<IWorkbookCommand> _appliedTableEffects = [];

    public string Label => _edits.Count == 1 ? "Edit Cell" : $"Edit {_edits.Count} Cells";

    public IReadOnlyList<CellAddress> AffectedCells => _affectedCells;

    public EditCellsCommand(SheetId sheetId, IReadOnlyList<(CellAddress Address, Cell NewCell)> edits)
    {
        _sheetId = sheetId;
        _edits = edits;
        var affectedCells = new CellAddress[edits.Count];
        for (var i = 0; i < edits.Count; i++)
            affectedCells[i] = edits[i].Address;
        _affectedCells = affectedCells;
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
                    return CommandGuards.RejectSheetProtected();
            }
        }

        if (CommandGuards.RejectIfSplitsArray(sheet, _affectedCells) is { } splitsArrayRejection)
            return splitsArrayRejection;

        _snapshot = [];

        foreach (var (addr, newCell) in _edits)
        {
            // Save old state for undo
            var oldCell = sheet.GetCell(addr)?.Clone();
            var hadRichTextRuns = sheet.RichTextRuns.TryGetValue(addr, out var oldRuns);
            var hadHyperlink = sheet.Hyperlinks.TryGetValue(addr, out var oldHyperlink);
            var hadHyperlinkMetadata = sheet.HyperlinkMetadata.TryGetValue(addr, out var oldHyperlinkMetadata);
            _snapshot.Add((
                addr,
                oldCell,
                sheet.GetStyleOnly(addr.Row, addr.Col),
                hadRichTextRuns,
                oldRuns,
                hadHyperlink,
                oldHyperlink,
                hadHyperlinkMetadata,
                oldHyperlinkMetadata));

            // Apply new state while preserving the cell's existing formatting.
            var appliedCell = newCell.Clone();
            if (oldCell is not null)
                appliedCell.StyleId = oldCell.StyleId;
            sheet.SetCell(addr, appliedCell);

            // The cell's content is being replaced, so any rich-text runs and hyperlink that
            // belonged to the old content are stale and must not carry over to the new content
            // (matching ClearContentsCommand/FillCellsCommand's handling of the same dictionaries).
            sheet.RichTextRuns.Remove(addr);
            sheet.Hyperlinks.Remove(addr);
            sheet.HyperlinkMetadata.Remove(addr);
        }

        var extraAffectedCells = StructuredTableEditEffects.Apply(ctx, _edits, _appliedTableEffects);
        if (extraAffectedCells.Count == 0)
            return new CommandOutcome(true, AffectedCells: _affectedCells);

        var allAffectedCells = new List<CellAddress>(_affectedCells.Count + extraAffectedCells.Count);
        allAffectedCells.AddRange(_affectedCells);
        allAffectedCells.AddRange(extraAffectedCells);
        return new CommandOutcome(true, AffectedCells: allAffectedCells);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_snapshot is null) return;

        StructuredTableEditEffects.Revert(ctx, _appliedTableEffects);

        var sheet = ctx.GetSheet(_sheetId);

        foreach (var (addr, oldCell, oldStyleOnly, hadRichTextRuns, oldRichTextRuns, hadHyperlink, oldHyperlink, hadHyperlinkMetadata, oldHyperlinkMetadata) in _snapshot)
        {
            if (oldCell is null)
            {
                sheet.ClearCell(addr);
                RestoreStyleOnly(sheet, addr, oldStyleOnly);
            }
            else
            {
                sheet.SetCell(addr, oldCell.Clone());
            }

            if (hadRichTextRuns && oldRichTextRuns is not null)
                sheet.RichTextRuns[addr] = oldRichTextRuns;
            else
                sheet.RichTextRuns.Remove(addr);

            if (hadHyperlink && oldHyperlink is not null)
                sheet.Hyperlinks[addr] = oldHyperlink;
            else
                sheet.Hyperlinks.Remove(addr);

            if (hadHyperlinkMetadata && oldHyperlinkMetadata is not null)
                sheet.HyperlinkMetadata[addr] = oldHyperlinkMetadata;
            else
                sheet.HyperlinkMetadata.Remove(addr);
        }
    }

    private static void RestoreStyleOnly(Sheet sheet, CellAddress address, StyleId? styleId)
    {
        if (styleId.HasValue)
            sheet.SetStyleOnly(address.Row, address.Col, styleId.Value);
        else
            sheet.ClearStyleOnly(address.Row, address.Col);
    }
}

/// <summary>
/// N33 (table auto-expand) and N34 (calculated-column formula propagation): the follow-up effects
/// <see cref="EditCellsCommand"/> runs, in the same undo transaction as the edit itself, after a
/// cell edit lands inside or just past a structured table. Kept as a driver over small
/// <see cref="IWorkbookCommand"/> sub-commands so <see cref="EditCellsCommand"/> can apply/revert
/// them the same way <see cref="CompositeWorkbookCommand"/> does for its own sub-commands.
/// </summary>
internal static class StructuredTableEditEffects
{
    /// <summary>
    /// Runs auto-expand (N33) and calculated-column propagation (N34) for every edited cell,
    /// appending each successfully-applied sub-command to <paramref name="applied"/> so
    /// <see cref="Revert"/> can unwind them in reverse order. Both effects are best-effort: a
    /// failing sub-command (e.g. a guard rejection) is simply skipped rather than failing the
    /// whole edit, since the base cell edit has already been committed by the time this runs.
    /// Returns every extra cell address the sub-commands wrote (e.g. calculated-column rows
    /// filled by N34) so the caller can fold them into its own <see cref="CommandOutcome.AffectedCells"/>
    /// and get them recalculated — these cells are additional to the direct edits and would
    /// otherwise be invisible to the recalc engine.
    /// </summary>
    public static IReadOnlyList<CellAddress> Apply(
        ICommandContext ctx,
        IReadOnlyList<(CellAddress Address, Cell NewCell)> edits,
        List<IWorkbookCommand> applied)
    {
        List<CellAddress>? extraAffectedCells = null;

        foreach (var (address, newCell) in edits)
        {
            var sheet = ctx.GetSheet(address.Sheet);

            // A table can auto-expand from this edit; resolve the (possibly new) table state
            // fresh from the sheet afterward since ResizeStructuredTableCommand may have just
            // replaced it.
            var table = FindContainingOrAdjacentTable(sheet, address);
            if (table is null)
                continue;

            var tableId = table.Id;
            if (StructuredTableDesignCommandHelpers.TryGetAutoExpandRange(sheet, table, address) is { } expandRange)
            {
                var previousRange = table.Range;
                var resizeCommand = new ResizeStructuredTableCommand(address.Sheet, tableId, expandRange);
                var resizeOutcome = resizeCommand.Apply(ctx);
                if (resizeOutcome.Success)
                {
                    applied.Add(resizeCommand);

                    // ResizeStructuredTableCommand's own outcome only reports the table's anchor
                    // cell as affected, not every cell FillGrownCalculatedColumns silently wrote
                    // into the newly grown rows — recompute the grown cells directly from the
                    // range delta so they still get recalculated.
                    (extraAffectedCells ??= []).AddRange(GetGrownCells(previousRange, expandRange));
                }

                // Whether or not the resize applied, re-resolve the table before considering N34
                // below: the range (and therefore the data-body bounds) may have just changed.
                if (!CommandGuards.TryFindStructuredTable(sheet, tableId, out table))
                    continue;
            }

            if (!newCell.HasFormula || newCell.FormulaText is null)
                continue;

            var propagateCommand = TryCreateCalculatedColumnPropagation(sheet, table, address, newCell.FormulaText);
            if (propagateCommand is null)
                continue;

            var propagateOutcome = propagateCommand.Apply(ctx);
            if (propagateOutcome.Success)
            {
                applied.Add(propagateCommand);
                if (propagateOutcome.AffectedCells is { Count: > 0 } propagateAffected)
                    (extraAffectedCells ??= []).AddRange(propagateAffected);
            }
        }

        return extraAffectedCells is null ? [] : extraAffectedCells;
    }

    /// <summary>Reverts every applied sub-command in reverse order, then clears the list.</summary>
    public static void Revert(ICommandContext ctx, List<IWorkbookCommand> applied)
    {
        for (var i = applied.Count - 1; i >= 0; i--)
            applied[i].Revert(ctx);
        applied.Clear();
    }

    /// <summary>
    /// Every cell address in <paramref name="grownRange"/> that wasn't already part of
    /// <paramref name="previousRange"/> — the rows/column a table auto-expand just brought into
    /// the table (a single new row OR a single new column, per <see cref="StructuredTableDesignCommandHelpers.TryGetAutoExpandRange"/>).
    /// </summary>
    private static IEnumerable<CellAddress> GetGrownCells(GridRange previousRange, GridRange grownRange)
    {
        foreach (var address in grownRange.AllCells())
        {
            if (!previousRange.Contains(address))
                yield return address;
        }
    }

    /// <summary>
    /// Finds the table that either already contains <paramref name="address"/>, or that
    /// <paramref name="address"/> is an N33 auto-expand gesture for (one row below / one column
    /// right of the table's current range). Excel only ever auto-expands or auto-fills a
    /// calculated column for a single table per edited cell, so the first match is authoritative
    /// (tables never overlap, per <see cref="ResizeStructuredTableCommand"/>'s own guard).
    /// </summary>
    private static StructuredTableModel? FindContainingOrAdjacentTable(Sheet sheet, CellAddress address)
    {
        foreach (var table in sheet.StructuredTables)
        {
            if (table.Range.Contains(address))
                return table;
        }

        foreach (var table in sheet.StructuredTables)
        {
            if (StructuredTableDesignCommandHelpers.TryGetAutoExpandRange(sheet, table, address) is not null)
                return table;
        }

        return null;
    }

    /// <summary>
    /// N34: when <paramref name="address"/> lands in a data-body row of one of
    /// <paramref name="table"/>'s columns and every OTHER data-body row in that column already
    /// shares a single common formula shape (so the edited cell is completing/continuing a
    /// calculated column rather than diverging from a column of independent formulas), builds a
    /// command that persists the formula on the column (so future auto-expands via
    /// <see cref="ResizeStructuredTableCommand.FillGrownCalculatedColumns"/> keep filling it) and
    /// propagates the row-shifted formula into that column's other data-body rows. Returns null
    /// when the edited cell isn't in a data-body row, or the column doesn't (or no longer)
    /// qualifies as a calculated column.
    /// </summary>
    private static IWorkbookCommand? TryCreateCalculatedColumnPropagation(
        Sheet sheet, StructuredTableModel table, CellAddress address, string formulaText)
    {
        if (!table.Range.Contains(address))
            return null;

        var (firstDataRow, lastDataRow) = GetDataBodyRowBounds(table);
        if (address.Row < firstDataRow || address.Row > lastDataRow)
            return null;
        if (address.Col < table.Range.Start.Col || address.Col > table.Range.End.Col)
            return null;

        var columnIndex = (int)(address.Col - table.Range.Start.Col);
        if (columnIndex >= table.Columns.Count)
            return null;
        var column = table.Columns[columnIndex];

        var otherDataRows = new List<uint>();
        for (var row = firstDataRow; row <= lastDataRow; row++)
        {
            if (row != address.Row)
                otherDataRows.Add(row);
        }

        // A lone-row table (the edit's row is the only data row) has no sibling rows to
        // propagate into or check for a consistent shape — nothing to do until it grows.
        if (otherDataRows.Count == 0)
            return null;

        // Only treat this as "typing a calculated column" when every other data-body row in the
        // column is either blank or already carries the same row-shifted formula shape as the
        // edited cell — otherwise this is an ordinary column of independent per-row formulas
        // (or mixed values) and Excel would not silently overwrite it.
        foreach (var otherRow in otherDataRows)
        {
            var otherCell = sheet.GetCell(new CellAddress(address.Sheet, otherRow, address.Col));
            if (otherCell is null || (!otherCell.HasFormula && otherCell.Value is BlankValue))
                continue;

            if (!otherCell.HasFormula || otherCell.FormulaText is null)
                return null;

            var expectedFormula = ShiftFormulaRows(formulaText, address.Row, otherRow, sheet.Name);
            if (!string.Equals(otherCell.FormulaText, expectedFormula, StringComparison.Ordinal))
                return null;
        }

        return new PropagateCalculatedColumnCommand(address.Sheet, table.Id, column.Id, address.Row, formulaText, otherDataRows);
    }

    /// <summary>
    /// Computes the first/last data-body rows of <paramref name="table"/> — the range excluding
    /// the header row(s) (when present) and the totals row (when shown), matching the same
    /// header/totals accounting used by <see cref="ResizeStructuredTableCommand.FillGrownCalculatedColumns"/>
    /// and the sibling structured-table commands.
    /// </summary>
    private static (uint FirstDataRow, uint LastDataRow) GetDataBodyRowBounds(StructuredTableModel table)
    {
        var hasHeaderRow = table.HeaderRowCount is null or > 0;
        var firstDataRow = table.Range.Start.Row + (hasHeaderRow ? 1u : 0u);
        var lastDataRow = table.TotalsRowShown && table.Range.End.Row > table.Range.Start.Row
            ? table.Range.End.Row - 1
            : table.Range.End.Row;

        return (firstDataRow, Math.Max(firstDataRow, lastDataRow));
    }

    /// <summary>Row-shifts a formula from <paramref name="fromRow"/> to <paramref name="toRow"/> via a plain paste-offset rewrite (relative refs move; absolute/structured refs don't).</summary>
    internal static string ShiftFormulaRows(string formulaText, uint fromRow, uint toRow, string hostSheetName)
    {
        var rowDelta = (int)toRow - (int)fromRow;
        if (rowDelta == 0)
            return formulaText;

        return FormulaRewriter.Rewrite(formulaText, new PasteOffsetOp(rowDelta, 0), hostSheetName) ?? formulaText;
    }
}

/// <summary>
/// N34 sub-command: persists <paramref name="formulaText"/> (row-shifted per target row) as
/// <paramref name="tableId"/>'s calculated-column formula on <paramref name="columnId"/>, and
/// writes it into every row in <paramref name="targetRows"/> (the column's other data-body rows).
/// Snapshots the previous per-row cells and the column's previous
/// <see cref="StructuredTableColumnModel.CalculatedColumnFormula"/>/<see cref="StructuredTableColumnModel.IsCalculatedColumnFormulaArray"/>
/// so <see cref="Revert"/> restores both exactly.
/// </summary>
internal sealed class PropagateCalculatedColumnCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly int _tableId;
    private readonly int _columnId;
    private readonly uint _sourceRow;
    private readonly string _sourceFormulaText;
    private readonly IReadOnlyList<uint> _targetRows;
    private List<(CellAddress Address, Cell? OldCell)>? _snapshot;
    private StructuredTableColumnModel? _previousColumn;
    private bool _applied;

    public string Label => "Fill Calculated Column";

    public PropagateCalculatedColumnCommand(
        SheetId sheetId,
        int tableId,
        int columnId,
        uint sourceRow,
        string sourceFormulaText,
        IReadOnlyList<uint> targetRows)
    {
        _sheetId = sheetId;
        _tableId = tableId;
        _columnId = columnId;
        _sourceRow = sourceRow;
        _sourceFormulaText = sourceFormulaText;
        _targetRows = targetRows;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        _snapshot = null;
        _previousColumn = null;
        _applied = false;

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        if (!CommandGuards.TryFindStructuredTable(sheet, _tableId, out var table))
            return CommandGuards.RejectStructuredTableNotFound();

        var columnIndex = table.Columns.FindIndex(c => c.Id == _columnId);
        if (columnIndex < 0)
            return CommandGuards.RejectStructuredTableNotFound();

        _previousColumn = table.Columns[columnIndex];
        var col = table.Range.Start.Col + (uint)columnIndex;

        _snapshot = [];
        foreach (var row in _targetRows)
        {
            var address = new CellAddress(_sheetId, row, col);
            _snapshot.Add((address, sheet.GetCell(address)?.Clone()));

            var shiftedFormula = StructuredTableEditEffects.ShiftFormulaRows(_sourceFormulaText, _sourceRow, row, sheet.Name);
            sheet.SetCell(address, Cell.FromFormula(shiftedFormula));
        }

        table.SetCalculatedColumnFormula(_columnId, _sourceFormulaText);
        _applied = true;

        return new CommandOutcome(true, AffectedCells: _snapshot.ConvertAll(s => s.Address));
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied)
            return;

        var sheet = ctx.GetSheet(_sheetId);

        if (_snapshot is not null)
        {
            foreach (var (address, oldCell) in _snapshot)
            {
                if (oldCell is null)
                    sheet.ClearCell(address);
                else
                    sheet.SetCell(address, oldCell.Clone());
            }
        }

        if (CommandGuards.TryFindStructuredTable(sheet, _tableId, out var table))
        {
            var columnIndex = table.Columns.FindIndex(c => c.Id == _columnId);
            if (columnIndex >= 0 && _previousColumn is not null)
                table.Columns[columnIndex] = _previousColumn;
        }

        _applied = false;
        _snapshot = null;
        _previousColumn = null;
    }
}
