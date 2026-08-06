using System.Globalization;
using System.Text;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public sealed class RefreshStructuredTableTotalsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly int _tableId;
    private readonly Dictionary<CellAddress, Cell?> _previousCells = [];

    public string Label => "Refresh Table Totals";

    public RefreshStructuredTableTotalsCommand(SheetId sheetId, int tableId)
    {
        _sheetId = sheetId;
        _tableId = tableId;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        if (!CommandGuards.TryFindStructuredTable(sheet, _tableId, out var table))
            return CommandGuards.RejectStructuredTableNotFound();
        if (!table.TotalsRowShown)
            return new CommandOutcome(false, "Table totals row is not shown.");
        if (table.Columns.Count == 0)
            return CommandGuards.RejectStructuredTableHasNoColumns();

        _previousCells.Clear();
        var totalsRow = table.Range.End.Row;
        var affectedCells = new List<CellAddress>(table.Columns.Count);
        for (var index = 0; index < table.Columns.Count; index++)
        {
            var address = new CellAddress(_sheetId, totalsRow, table.Range.Start.Col + (uint)index);
            affectedCells.Add(address);
            var oldCell = sheet.GetCell(address.Row, address.Col);
            _previousCells[address] = oldCell?.Clone();

            // R124: preserve the destination cell's pre-existing formatting (totals-row banding,
            // custom number format, bold, borders, ...) instead of silently resetting it to
            // StyleId.Default via Cell.FromFormula/Cell.FromValue's default construction --
            // matching PropagateCalculatedColumnCommand.Apply's fix for the identical bug class
            // (Commands.cs ~530-536) and EditCellsCommand.Apply's guard for a direct user edit
            // (Commands.cs ~110-117). Without this, growing a table (auto-expand, which relocates
            // the totals row via ResizeStructuredTableCommand with no restyle follow-up) or
            // re-showing a hidden totals row wipes any styling already sitting on that cell --
            // including formatting loaded straight from an Excel-authored totalsRowFunction table.
            var existingStyleId = oldCell?.StyleId ?? sheet.GetStyleOnly(address.Row, address.Col);

            if (ResolveTotalsCell(sheet, table, index) is { } cell)
            {
                if (existingStyleId is { } styleId)
                    cell.StyleId = styleId;
                sheet.SetCell(address, cell);
            }
            else if (existingStyleId is { } blankStyleId)
            {
                var blank = Cell.FromValue(BlankValue.Instance);
                blank.StyleId = blankStyleId;
                sheet.SetCell(address, blank);
            }
            else
            {
                sheet.SetCell(address, BlankValue.Instance);
            }
        }

        return new CommandOutcome(true, AffectedCells: affectedCells);
    }

    public void Revert(ICommandContext ctx)
    {
        if (_previousCells.Count == 0)
            return;

        var sheet = ctx.GetSheet(_sheetId);
        foreach (var (address, cell) in _previousCells)
        {
            if (cell is null)
                sheet.ClearCell(address.Row, address.Col);
            else
                sheet.SetCell(address, cell);
        }
        _previousCells.Clear();
    }

    // P106: Excel's table totals row is always backed by a live =SUBTOTAL(10x,[Column]) formula for
    // every built-in totalsRowFunction (never a static constant) — the 100-series function numbers
    // make SUBTOTAL itself skip manually/filter/group-hidden rows at evaluation time, so no separate
    // hidden-row-aware aggregation is needed here anymore. Writing a formula (instead of a
    // precomputed NumberValue) also keeps the total live across future data edits and recalcs
    // correctly even when it is regenerated before FillGrownCalculatedColumns's newly written
    // formula cells have been recalculated (their cached Values are still blank at that point; a
    // static aggregate computed then would freeze at a wrong, stale number forever).
    private static readonly Dictionary<string, int> TotalsRowFunctionSubtotalNumbers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["average"] = 101,
        ["avg"] = 101,
        ["count"] = 103,
        ["countnums"] = 102,
        ["max"] = 104,
        ["min"] = 105,
        ["stddev"] = 107,
        ["sum"] = 109,
        ["var"] = 110,
    };

    private static Cell? ResolveTotalsCell(Sheet sheet, StructuredTableModel table, int columnIndex)
    {
        var column = table.Columns[columnIndex];
        if (!string.IsNullOrWhiteSpace(column.TotalsRowLabel))
            return Cell.FromValue(new TextValue(column.TotalsRowLabel));
        if (!string.IsNullOrWhiteSpace(column.TotalsRowFormula))
            return Cell.FromFormula(column.TotalsRowFormula);
        if (string.IsNullOrWhiteSpace(column.TotalsRowFunction))
            return null;

        var function = column.TotalsRowFunction.Trim();
        if (!TotalsRowFunctionSubtotalNumbers.TryGetValue(function, out var subtotalNumber))
            return null;

        // R94: an ordinary header-cell edit renames a table column in Excel semantics but only
        // ever updates the sheet cell text -- nothing syncs StructuredTableColumnModel.Name back
        // to match (see StructuredReferenceResolver.ColumnHeaderText for the same gap on the
        // resolve side). Regenerating this cell's own SUBTOTAL(...) reference against the
        // possibly-stale stored Name would silently write a formula that resolves to #NAME? the
        // moment it's recalculated, even though the header the user sees is fine. Mirror the
        // resolver's live-header-first lookup so a freshly (re)generated totals formula always
        // spells the column exactly as it currently reads on the sheet.
        var liveColumnName = ColumnHeaderText(sheet, table, columnIndex);
        var escapedColumnName = EscapeStructuredReferenceColumnName(liveColumnName);
        return Cell.FromFormula($"SUBTOTAL({subtotalNumber.ToString(CultureInfo.InvariantCulture)},[{escapedColumnName}])");
    }

    // Mirrors StructuredReferenceResolver.ColumnHeaderText: resolves the column's EFFECTIVE
    // name using the live header-row cell text when one exists, falling back to the stored
    // model name for a headerless table or a blank header cell.
    private static string ColumnHeaderText(Sheet sheet, StructuredTableModel table, int columnIndex)
    {
        var storedName = table.Columns[columnIndex].Name;
        if (table.HeaderRowCount is 0)
            return storedName;

        var headerCol = table.Range.Start.Col + (uint)columnIndex;
        return sheet.GetCell(table.Range.Start.Row, headerCol)?.Value is TextValue { Value.Length: > 0 } text
            ? text.Value
            : storedName;
    }

    // R12-xlsx-tables-3: a column header containing '[', ']', '#', or an apostrophe must have each
    // such character individually escaped with a leading apostrophe, or FreeX's own formula lexer
    // (see FreeX.Core.Formula.Lexer.ReadStructuredReferenceSelectorSlow) either mis-parses the
    // selector (an unescaped '[' opens a nested/combined-selector bracket group it can never close)
    // or StructuredReferenceResolver.FindColumnIndex simply fails to match the literal header text,
    // leaving the totals cell's SUBTOTAL formula resolving to #NAME?. Escaping only ']' (the
    // previous behavior) left '[' and '#' broken.
    private static readonly char[] StructuredReferenceEscapableChars = ['[', ']', '#', '\''];

    private static string EscapeStructuredReferenceColumnName(string columnName)
    {
        if (columnName.AsSpan().IndexOfAny(StructuredReferenceEscapableChars) < 0)
            return columnName;

        var builder = new StringBuilder(columnName.Length + 4);
        foreach (var ch in columnName)
        {
            if (ch is '[' or ']' or '#' or '\'')
                builder.Append('\'');
            builder.Append(ch);
        }

        return builder.ToString();
    }
}

public sealed class SetStructuredTableTotalsRowCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly int _tableId;
    private readonly bool _showTotalsRow;
    private List<StructuredTableModel>? _previousTables;
    private IWorkbookCommand? _rowCommand;
    private RefreshStructuredTableTotalsCommand? _refreshCommand;

    public string Label => _showTotalsRow ? "Show Table Totals Row" : "Hide Table Totals Row";

    public SetStructuredTableTotalsRowCommand(SheetId sheetId, int tableId, bool showTotalsRow)
    {
        _sheetId = sheetId;
        _tableId = tableId;
        _showTotalsRow = showTotalsRow;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        _previousTables = null;
        _rowCommand = null;
        _refreshCommand = null;

        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        if (!CommandGuards.TryFindStructuredTable(sheet, _tableId, out var table))
            return CommandGuards.RejectStructuredTableNotFound();
        if (table.TotalsRowShown == _showTotalsRow)
            return new CommandOutcome(true, AffectedCells: [table.Range.End]);
        if (table.Columns.Count == 0)
            return CommandGuards.RejectStructuredTableHasNoColumns();

        return _showTotalsRow
            ? ShowTotalsRow(ctx, sheet, table)
            : HideTotalsRow(ctx, sheet, table);
    }

    public void Revert(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        _refreshCommand?.Revert(ctx);
        _rowCommand?.Revert(ctx);

        if (_previousTables is not null)
            RestoreStructuredTables(sheet, _previousTables);

        _refreshCommand = null;
        _rowCommand = null;
        _previousTables = null;
    }

    private CommandOutcome ShowTotalsRow(ICommandContext ctx, Sheet sheet, StructuredTableModel table)
    {
        if (table.Range.End.Row >= CellAddress.MaxRow)
            return new CommandOutcome(false, "Cannot show table totals row below the last worksheet row.");

        var insertRow = table.Range.End.Row + 1;
        if (sheet.StructuredTables.Any(candidate => candidate.Range.End.Row >= insertRow && candidate.Range.End.Row >= CellAddress.MaxRow))
            return new CommandOutcome(false, "Cannot show table totals row: another table would move past the last worksheet row.");

        _previousTables = sheet.StructuredTables.ToList();
        _rowCommand = new InsertRowsCommand(_sheetId, insertRow);
        var insertOutcome = _rowCommand.Apply(ctx);
        if (!insertOutcome.Success)
            return insertOutcome;

        ReplaceStructuredTables(sheet, BuildTablesAfterInsert(_previousTables, table.Id, insertRow));

        _refreshCommand = new RefreshStructuredTableTotalsCommand(_sheetId, table.Id);
        var refreshOutcome = _refreshCommand.Apply(ctx);
        if (!refreshOutcome.Success)
        {
            Revert(ctx);
            return refreshOutcome;
        }

        return new CommandOutcome(true, AffectedCells: [new CellAddress(_sheetId, insertRow, table.Range.Start.Col)]);
    }

    private CommandOutcome HideTotalsRow(ICommandContext ctx, Sheet sheet, StructuredTableModel table)
    {
        if (table.Range.End.Row <= table.Range.Start.Row)
            return new CommandOutcome(false, "Cannot hide the only row in a table.");

        var totalsRow = table.Range.End.Row;
        _previousTables = sheet.StructuredTables.ToList();

        // Real Excel captures whatever is actually in the totals-row cell (a recognized SUBTOTAL
        // aggregate, a custom formula, a label, or blank) back into the column's totals definition
        // before hiding the row, so re-showing it later reproduces the user's last edit instead of
        // reverting to a stale totalsRowFunction. Capture from the live sheet cells here, while the
        // totals row still exists, and use the reconciled columns only for what re-appears after
        // delete — _previousTables (used by Revert) stays the untouched pre-hide snapshot.
        var reconciledColumns = CaptureManualTotalsEdits(sheet, table);
        var tablesForDelete = _previousTables
            .Select(t => t.Id == table.Id ? CopyWith(t, t.Range, columnsOverride: reconciledColumns) : t)
            .ToList();

        _rowCommand = new DeleteRowsCommand(_sheetId, totalsRow);
        var deleteOutcome = _rowCommand.Apply(ctx);
        if (!deleteOutcome.Success)
            return deleteOutcome;

        ReplaceStructuredTables(sheet, BuildTablesAfterDelete(tablesForDelete, table.Id, totalsRow));
        return new CommandOutcome(true, AffectedCells: [new CellAddress(_sheetId, totalsRow, table.Range.Start.Col)]);
    }

    private static List<StructuredTableColumnModel> CaptureManualTotalsEdits(Sheet sheet, StructuredTableModel table)
    {
        var totalsRow = table.Range.End.Row;
        var columns = new List<StructuredTableColumnModel>(table.Columns.Count);
        for (var index = 0; index < table.Columns.Count; index++)
        {
            var column = table.Columns[index];
            var cell = sheet.GetCell(totalsRow, table.Range.Start.Col + (uint)index);
            columns.Add(ReconcileColumnFromTotalsCell(column, cell));
        }
        return columns;
    }

    private static readonly Dictionary<int, string> SubtotalNumberToTotalsRowFunction = new()
    {
        [101] = "average",
        [102] = "countNums",
        [103] = "count",
        [104] = "max",
        [105] = "min",
        [107] = "stdDev",
        [109] = "sum",
        [110] = "var",
    };

    private static readonly System.Text.RegularExpressions.Regex SubtotalTotalsFormulaPattern = new(
        @"^SUBTOTAL\(\s*(\d+)\s*,\s*\[.*\]\s*\)$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Reconciles a column's persisted totals definition against what is actually sitting in the
    /// totals-row cell right now: a recognized <c>SUBTOTAL(n,[Column])</c> aggregate maps back to the
    /// matching <see cref="StructuredTableColumnModel.TotalsRowFunction"/>, any other formula becomes
    /// a custom <see cref="StructuredTableColumnModel.TotalsRowFormula"/>, plain text becomes
    /// <see cref="StructuredTableColumnModel.TotalsRowLabel"/>, and a blank cell clears all three —
    /// matching Excel's behavior of updating the column's totals kind immediately when the totals
    /// cell is edited directly.
    /// </summary>
    private static StructuredTableColumnModel ReconcileColumnFromTotalsCell(StructuredTableColumnModel column, Cell? cell)
    {
        if (cell is null || (!cell.HasFormula && cell.Value is BlankValue))
            return column with { TotalsRowLabel = null, TotalsRowFunction = null, TotalsRowFormula = null };

        if (cell.HasFormula)
        {
            var formulaText = cell.FormulaText!;
            var match = SubtotalTotalsFormulaPattern.Match(formulaText.Trim());
            if (match.Success
                && int.TryParse(match.Groups[1].Value, out var subtotalNumber)
                && SubtotalNumberToTotalsRowFunction.TryGetValue(subtotalNumber, out var functionName))
            {
                return column with { TotalsRowFunction = functionName, TotalsRowFormula = null, TotalsRowLabel = null };
            }

            // ECMA-376 18.3.1.90: a totals-row formula that isn't one of the recognized built-in
            // SUBTOTAL(n,[Column]) aggregates gets totalsRowFunction="custom" written alongside
            // totalsRowFormula, the same way real Excel always serializes a directly-typed custom
            // total. Leaving TotalsRowFunction null here produced a <totalsRowFormula> with no
            // totalsRowFunction attribute at all -- a shape Excel itself never writes.
            return column with { TotalsRowFormula = formulaText, TotalsRowFunction = "custom", TotalsRowLabel = null };
        }

        if (cell.Value is TextValue text)
            return column with { TotalsRowLabel = text.Value, TotalsRowFunction = null, TotalsRowFormula = null };

        // R78-io-table-listobject-5-2: a literal, non-text scalar (a plain number, boolean, date
        // serial, or error) typed directly into the totals cell has no OOXML totals-row slot of its
        // own -- totalsRowLabel is string-only (ECMA-376 18.3.1.90) and totalsRowFunction only covers
        // the built-in SUBTOTAL aggregates plus "custom" for an explicit formula. Round it through the
        // same custom-formula slot the non-SUBTOTAL formula branch above uses, storing the literal's
        // own text as a trivial constant formula, so ResolveTotalsCell's Cell.FromFormula(...)
        // reconstructs the identical value on re-show instead of silently discarding it via the
        // all-null fallback below (previously the only outcome for every scalar but text).
        if (cell.Value is not BlankValue && FormatLiteralTotalsFormula(cell.Value) is { } literalFormula)
            return column with { TotalsRowFormula = literalFormula, TotalsRowFunction = "custom", TotalsRowLabel = null };

        return column with { TotalsRowLabel = null, TotalsRowFunction = null, TotalsRowFormula = null };
    }

    private static string? FormatLiteralTotalsFormula(ScalarValue value) =>
        value switch
        {
            NumberValue number => number.Value.ToString(CultureInfo.InvariantCulture),
            DateTimeValue dateTime => dateTime.Value.ToString(CultureInfo.InvariantCulture),
            BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
            ErrorValue error => error.Code,
            _ => null
        };

    private static IEnumerable<StructuredTableModel> BuildTablesAfterInsert(
        IReadOnlyList<StructuredTableModel> tables,
        int targetTableId,
        uint insertRow)
    {
        foreach (var table in tables)
        {
            if (table.Id == targetTableId)
            {
                yield return CopyWith(
                    table,
                    new GridRange(
                        table.Range.Start,
                        new CellAddress(table.Range.End.Sheet, table.Range.End.Row + 1, table.Range.End.Col)),
                    totalsRowShown: true,
                    totalsRowCount: 1,
                    updateTotalsRowCount: true);
                continue;
            }

            yield return CopyWith(table, ShiftRangeForInsert(table.Range, insertRow));
        }
    }

    private static IEnumerable<StructuredTableModel> BuildTablesAfterDelete(
        IReadOnlyList<StructuredTableModel> tables,
        int targetTableId,
        uint deletedRow)
    {
        foreach (var table in tables)
        {
            if (table.Id == targetTableId)
            {
                yield return CopyWith(
                    table,
                    new GridRange(
                        table.Range.Start,
                        new CellAddress(table.Range.End.Sheet, table.Range.End.Row - 1, table.Range.End.Col)),
                    totalsRowShown: false,
                    totalsRowCount: 0,
                    updateTotalsRowCount: true);
                continue;
            }

            if (ShiftRangeForDelete(table.Range, deletedRow) is { } shiftedRange)
                yield return CopyWith(table, shiftedRange);
        }
    }

    private static GridRange ShiftRangeForInsert(GridRange range, uint insertRow)
    {
        if (range.Start.Row >= insertRow)
        {
            return new GridRange(
                new CellAddress(range.Start.Sheet, range.Start.Row + 1, range.Start.Col),
                new CellAddress(range.End.Sheet, range.End.Row + 1, range.End.Col));
        }

        if (range.End.Row >= insertRow)
        {
            return new GridRange(
                range.Start,
                new CellAddress(range.End.Sheet, range.End.Row + 1, range.End.Col));
        }

        return range;
    }

    private static GridRange? ShiftRangeForDelete(GridRange range, uint deletedRow)
    {
        if (range.End.Row < deletedRow)
            return range;

        if (range.Start.Row > deletedRow)
        {
            return new GridRange(
                new CellAddress(range.Start.Sheet, range.Start.Row - 1, range.Start.Col),
                new CellAddress(range.End.Sheet, range.End.Row - 1, range.End.Col));
        }

        if (range.RowCount <= 1)
            return null;

        return new GridRange(
            range.Start,
            new CellAddress(range.End.Sheet, range.End.Row - 1, range.End.Col));
    }

    private static StructuredTableModel CopyWith(
        StructuredTableModel table,
        GridRange range,
        bool? totalsRowShown = null,
        int? totalsRowCount = null,
        bool updateTotalsRowCount = false,
        IReadOnlyList<StructuredTableColumnModel>? columnsOverride = null)
    {
        var copy = new StructuredTableModel
        {
            Id = table.Id,
            Name = table.Name,
            DisplayName = table.DisplayName,
            Range = range,
            HasAutoFilter = table.HasAutoFilter,
            TotalsRowShown = totalsRowShown ?? table.TotalsRowShown,
            HeaderRowCount = table.HeaderRowCount,
            TotalsRowCount = updateTotalsRowCount ? totalsRowCount : table.TotalsRowCount,
            InsertRow = table.InsertRow,
            InsertRowShift = table.InsertRowShift,
            Published = table.Published,
            Comment = table.Comment,
            StyleName = table.StyleName,
            ShowFirstColumn = table.ShowFirstColumn,
            ShowLastColumn = table.ShowLastColumn,
            ShowRowStripes = table.ShowRowStripes,
            ShowColumnStripes = table.ShowColumnStripes,
            PackagePart = table.PackagePart,
            NativeSortStateXml = table.NativeSortStateXml,
            NativeAttributes = table.NativeAttributes,
            NativeChildXmls = table.NativeChildXmls,
            NativeAutoFilterAttributes = table.NativeAutoFilterAttributes,
            NativeAutoFilterChildXmls = table.NativeAutoFilterChildXmls,
            NativeStyleInfoAttributes = table.NativeStyleInfoAttributes,
            NativeStyleInfoChildXmls = table.NativeStyleInfoChildXmls
        };

        copy.Columns.AddRange(columnsOverride ?? table.Columns);
        copy.FilterColumns.AddRange(table.FilterColumns);
        return copy;
    }

    private static void ReplaceStructuredTables(Sheet sheet, IEnumerable<StructuredTableModel> tables)
    {
        sheet.StructuredTables.Clear();
        sheet.StructuredTables.AddRange(tables);
    }

    private static void RestoreStructuredTables(Sheet sheet, IReadOnlyList<StructuredTableModel> tables)
    {
        sheet.StructuredTables.Clear();
        sheet.StructuredTables.AddRange(tables);
    }
}
