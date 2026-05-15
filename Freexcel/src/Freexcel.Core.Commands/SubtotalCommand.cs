using Freexcel.Core.Model;

namespace Freexcel.Core.Commands;

public sealed class SubtotalCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly uint _groupByColumnOffset;
    private readonly IReadOnlyList<uint> _subtotalColumnOffsets;
    private readonly int _functionNumber;
    private readonly bool _pageBreakBetweenGroups;
    private readonly bool _summaryBelowData;
    private readonly List<IWorkbookCommand> _appliedCommands = [];
    private List<uint>? _previousRowPageBreaks;

    public string Label => "Subtotal";

    public SubtotalCommand(
        SheetId sheetId,
        GridRange range,
        uint groupByColumnOffset,
        uint subtotalColumnOffset,
        int functionNumber = 9,
        bool pageBreakBetweenGroups = false,
        bool summaryBelowData = true)
        : this(
            sheetId,
            range,
            groupByColumnOffset,
            [subtotalColumnOffset],
            functionNumber,
            pageBreakBetweenGroups,
            summaryBelowData)
    {
    }

    public SubtotalCommand(
        SheetId sheetId,
        GridRange range,
        uint groupByColumnOffset,
        IReadOnlyList<uint> subtotalColumnOffsets,
        int functionNumber = 9,
        bool pageBreakBetweenGroups = false,
        bool summaryBelowData = true)
    {
        _sheetId = sheetId;
        _range = range;
        _groupByColumnOffset = groupByColumnOffset;
        _subtotalColumnOffsets = subtotalColumnOffsets;
        _functionNumber = functionNumber;
        _pageBreakBetweenGroups = pageBreakBetweenGroups;
        _summaryBelowData = summaryBelowData;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;
        if (_range.RowCount < 2)
            return new CommandOutcome(false, "Subtotal requires a header row and at least one data row.");
        if (_groupByColumnOffset >= _range.ColCount ||
            _subtotalColumnOffsets.Count == 0 ||
            _subtotalColumnOffsets.Any(offset => offset >= _range.ColCount))
            return new CommandOutcome(false, "Subtotal columns must be inside the selected range.");

        _appliedCommands.Clear();
        _previousRowPageBreaks = sheet.RowPageBreaks.ToList();
        var groups = GetGroups(sheet);
        var affected = new List<CellAddress>();
        var pageBreakRows = new List<uint>();

        if (_summaryBelowData)
        {
            foreach (var group in groups.OrderByDescending(g => g.EndRow))
            {
                var insertRow = group.EndRow + 1;
                if (!ApplyInsertAndEdit(ctx, insertRow, $"{group.Label} Total", group.StartRow, group.EndRow, affected))
                    return new CommandOutcome(false, "Could not insert subtotal row.");

                if (_pageBreakBetweenGroups && group.EndRow < _range.End.Row)
                    pageBreakRows.Add(insertRow + 1);
            }

            foreach (var rowBreak in pageBreakRows)
                sheet.RowPageBreaks.Add(rowBreak);

            uint grandTotalRow = _range.End.Row + (uint)groups.Count + 1;
            if (!ApplyInsertAndEdit(ctx, grandTotalRow, "Grand Total", _range.Start.Row + 1, grandTotalRow - 1, affected))
                return new CommandOutcome(false, "Could not insert subtotal row.");

            return new CommandOutcome(true, AffectedCells: affected);
        }

        foreach (var group in groups.OrderByDescending(g => g.StartRow))
        {
            if (!ApplyInsertAndEdit(ctx, group.StartRow, $"{group.Label} Total", group.StartRow + 1, group.EndRow + 1, affected))
                return new CommandOutcome(false, "Could not insert subtotal row.");
        }

        uint summaryRow = _range.Start.Row + 1;
        uint summaryEndRow = _range.End.Row + (uint)groups.Count + 1;
        if (!ApplyInsertAndEdit(ctx, summaryRow, "Grand Total", summaryRow + 1, summaryEndRow, affected))
            return new CommandOutcome(false, "Could not insert subtotal row.");

        return new CommandOutcome(true, AffectedCells: affected);
    }

    public void Revert(ICommandContext ctx)
    {
        for (int i = _appliedCommands.Count - 1; i >= 0; i--)
            _appliedCommands[i].Revert(ctx);
        _appliedCommands.Clear();
        if (_previousRowPageBreaks is not null)
        {
            var sheet = ctx.GetSheet(_sheetId);
            sheet.RowPageBreaks.Clear();
            foreach (var rowBreak in _previousRowPageBreaks)
                sheet.RowPageBreaks.Add(rowBreak);
            _previousRowPageBreaks = null;
        }
    }

    private List<GroupSpan> GetGroups(Sheet sheet)
    {
        var groupColumn = _range.Start.Col + _groupByColumnOffset;
        var groups = new List<GroupSpan>();
        var groupStart = _range.Start.Row + 1;
        var currentLabel = FormatLabel(sheet.GetValue(groupStart, groupColumn));

        for (uint row = groupStart + 1; row <= _range.End.Row; row++)
        {
            var label = FormatLabel(sheet.GetValue(row, groupColumn));
            if (label == currentLabel)
                continue;

            groups.Add(new GroupSpan(currentLabel, groupStart, row - 1));
            groupStart = row;
            currentLabel = label;
        }

        groups.Add(new GroupSpan(currentLabel, groupStart, _range.End.Row));
        return groups;
    }

    private bool ApplyInsertAndEdit(
        ICommandContext ctx,
        uint insertRow,
        string label,
        uint formulaStartRow,
        uint formulaEndRow,
        List<CellAddress> affected)
    {
        var insert = new InsertRowsCommand(_sheetId, insertRow);
        var insertOutcome = insert.Apply(ctx);
        if (!insertOutcome.Success)
            return false;
        _appliedCommands.Add(insert);

        var labelAddress = new CellAddress(_sheetId, insertRow, _range.Start.Col + _groupByColumnOffset);
        var edits = new List<(CellAddress Address, Cell Cell)>
        {
            (labelAddress, Cell.FromValue(new TextValue(label)))
        };
        foreach (var subtotalColumnOffset in _subtotalColumnOffsets)
        {
            var formulaAddress = new CellAddress(_sheetId, insertRow, _range.Start.Col + subtotalColumnOffset);
            var subtotalColumnName = CellAddress.NumberToColumnName(formulaAddress.Col);
            var formula = $"SUBTOTAL({_functionNumber},{subtotalColumnName}{formulaStartRow}:{subtotalColumnName}{formulaEndRow})";
            edits.Add((formulaAddress, Cell.FromFormula(formula)));
        }

        var edit = new EditCellsCommand(_sheetId, edits);
        var editOutcome = edit.Apply(ctx);
        if (!editOutcome.Success)
            return false;

        _appliedCommands.Add(edit);
        affected.Add(labelAddress);
        affected.AddRange(edits.Skip(1).Select(editItem => editItem.Address));
        return true;
    }

    private static string FormatLabel(ScalarValue value) => value switch
    {
        TextValue text => text.Value,
        NumberValue number => number.Value.ToString(System.Globalization.CultureInfo.CurrentCulture),
        BoolValue boolean => boolean.Value ? "TRUE" : "FALSE",
        ErrorValue error => error.Code,
        _ => ""
    };

    private sealed record GroupSpan(string Label, uint StartRow, uint EndRow);
}

public sealed class RemoveSubtotalRowsCommand : IWorkbookCommand
{
    private readonly SheetId _sheetId;
    private readonly GridRange _range;
    private readonly List<DeleteRowsCommand> _deletes = [];

    public string Label => "Remove Subtotals";

    public RemoveSubtotalRowsCommand(SheetId sheetId, GridRange range)
    {
        _sheetId = sheetId;
        _range = range;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var sheet = ctx.GetSheet(_sheetId);
        if (CommandGuards.RejectIfProtected(sheet) is { } protectedOutcome)
            return protectedOutcome;

        _deletes.Clear();
        var rows = FindSubtotalRows(sheet);
        foreach (var row in rows.OrderByDescending(r => r))
        {
            var delete = new DeleteRowsCommand(_sheetId, row);
            var outcome = delete.Apply(ctx);
            if (!outcome.Success)
                return outcome;

            _deletes.Add(delete);
        }

        return new CommandOutcome(true);
    }

    public void Revert(ICommandContext ctx)
    {
        for (var i = _deletes.Count - 1; i >= 0; i--)
            _deletes[i].Revert(ctx);
        _deletes.Clear();
    }

    private List<uint> FindSubtotalRows(Sheet sheet)
    {
        var rows = new List<uint>();
        for (uint row = _range.Start.Row; row <= _range.End.Row; row++)
        {
            for (uint col = _range.Start.Col; col <= _range.End.Col; col++)
            {
                var formula = sheet.GetCell(new CellAddress(_sheetId, row, col))?.FormulaText;
                if (formula is not null &&
                    formula.TrimStart().StartsWith("SUBTOTAL(", StringComparison.OrdinalIgnoreCase))
                {
                    rows.Add(row);
                    break;
                }
            }
        }

        return rows;
    }
}
