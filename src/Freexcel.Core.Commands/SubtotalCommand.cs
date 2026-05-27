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
        var plan = SubtotalPlanBuilder.Build(
            sheet,
            _range,
            _groupByColumnOffset,
            _pageBreakBetweenGroups,
            _summaryBelowData);
        var affected = new List<CellAddress>();

        if (_summaryBelowData)
        {
            foreach (var subtotalRow in plan.GroupRows)
            {
                if (!ApplyInsertAndEdit(ctx, subtotalRow, affected))
                    return new CommandOutcome(false, "Could not insert subtotal row.");
            }

            AddPlannedPageBreaks(sheet, plan);

            if (!ApplyInsertAndEdit(ctx, plan.GrandTotalRow, affected))
                return new CommandOutcome(false, "Could not insert subtotal row.");

            return new CommandOutcome(true, AffectedCells: affected);
        }

        foreach (var subtotalRow in plan.GroupRows)
        {
            if (!ApplyInsertAndEdit(ctx, subtotalRow, affected))
                return new CommandOutcome(false, "Could not insert subtotal row.");
        }

        if (!ApplyInsertAndEdit(ctx, plan.GrandTotalRow, affected))
            return new CommandOutcome(false, "Could not insert subtotal row.");

        AddPlannedPageBreaks(sheet, plan);

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

    private bool ApplyInsertAndEdit(
        ICommandContext ctx,
        SubtotalInsertionPlan subtotalRow,
        List<CellAddress> affected)
    {
        var insert = new InsertRowsCommand(_sheetId, subtotalRow.InsertRow);
        var insertOutcome = insert.Apply(ctx);
        if (!insertOutcome.Success)
            return false;
        _appliedCommands.Add(insert);

        var labelAddress = new CellAddress(_sheetId, subtotalRow.InsertRow, _range.Start.Col + _groupByColumnOffset);
        var edits = new List<(CellAddress Address, Cell Cell)>
        {
            (labelAddress, Cell.FromValue(new TextValue(subtotalRow.Label)))
        };
        foreach (var subtotalColumnOffset in _subtotalColumnOffsets)
        {
            var formulaAddress = new CellAddress(_sheetId, subtotalRow.InsertRow, _range.Start.Col + subtotalColumnOffset);
            var formula = SubtotalPlanBuilder.BuildSubtotalFormula(
                _functionNumber,
                formulaAddress.Col,
                subtotalRow.FormulaStartRow,
                subtotalRow.FormulaEndRow);
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

    private static void AddPlannedPageBreaks(Sheet sheet, SubtotalPlan plan)
    {
        foreach (var rowBreak in plan.PageBreakRows)
            sheet.RowPageBreaks.Add(rowBreak);
    }
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
        var rows = SubtotalRowFinder.Find(sheet, _sheetId, _range);
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

}
