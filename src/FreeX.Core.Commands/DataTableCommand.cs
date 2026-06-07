using System.Text.RegularExpressions;
using FreeX.Core.Model;

namespace FreeX.Core.Commands;

public enum DataTableInputOrientation
{
    Column,
    Row
}

public sealed class OneVariableDataTableCommand : IWorkbookCommand
{
    private readonly GridRange _tableRange;
    private readonly CellAddress _formulaCell;
    private readonly CellAddress _inputCell;
    private readonly DataTableInputOrientation _orientation;
    private List<(CellAddress Address, Cell? PreviousCell)>? _snapshot;
    private bool _applied;

    public string Label => "Data Table";

    public OneVariableDataTableCommand(
        GridRange tableRange,
        CellAddress formulaCell,
        CellAddress inputCell,
        DataTableInputOrientation orientation = DataTableInputOrientation.Column)
    {
        _tableRange = tableRange;
        _formulaCell = formulaCell;
        _inputCell = inputCell;
        _orientation = orientation;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var validation = DataTableCommandGuards.ValidateInputs(
            ctx,
            _tableRange,
            _formulaCell,
            [_inputCell],
            out var sheet,
            out var formula);
        if (validation is not null)
            return validation;

        _snapshot = [];
        var affected = new List<CellAddress>();
        if (_orientation == DataTableInputOrientation.Row)
        {
            for (uint col = _tableRange.Start.Col + 1; col <= _tableRange.End.Col; col++)
            {
                var trialInputAddress = new CellAddress(_tableRange.Start.Sheet, _tableRange.Start.Row, col);
                for (uint row = _tableRange.Start.Row + 1; row <= _tableRange.End.Row; row++)
                {
                    var outputAddress = new CellAddress(_tableRange.Start.Sheet, row, col);
                    _snapshot.Add((outputAddress, sheet!.GetCell(outputAddress)?.Clone()));
                    sheet.SetCell(outputAddress, Cell.FromFormula(DataTableFormulaRewriter.ReplaceCellReference(formula!, _inputCell, trialInputAddress)));
                    affected.Add(outputAddress);
                }
            }
        }
        else
        {
            for (uint row = _tableRange.Start.Row + 1; row <= _tableRange.End.Row; row++)
            {
                var trialInputAddress = new CellAddress(_tableRange.Start.Sheet, row, _tableRange.Start.Col);
                for (uint col = _tableRange.Start.Col + 1; col <= _tableRange.End.Col; col++)
                {
                    var outputAddress = new CellAddress(_tableRange.Start.Sheet, row, col);
                    _snapshot.Add((outputAddress, sheet!.GetCell(outputAddress)?.Clone()));
                    sheet.SetCell(outputAddress, Cell.FromFormula(DataTableFormulaRewriter.ReplaceCellReference(formula!, _inputCell, trialInputAddress)));
                    affected.Add(outputAddress);
                }
            }
        }

        _applied = true;
        return new CommandOutcome(true, AffectedCells: affected);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied || _snapshot is null)
            return;

        var sheet = ctx.GetSheet(_tableRange.Start.Sheet);
        foreach (var (address, previousCell) in _snapshot)
        {
            if (previousCell is null)
                sheet.ClearCell(address);
            else
                sheet.SetCell(address, previousCell.Clone());
        }

        _applied = false;
    }

}

public sealed class TwoVariableDataTableCommand : IWorkbookCommand
{
    private readonly GridRange _tableRange;
    private readonly CellAddress _formulaCell;
    private readonly CellAddress _rowInputCell;
    private readonly CellAddress _columnInputCell;
    private List<(CellAddress Address, Cell? PreviousCell)>? _snapshot;
    private bool _applied;

    public string Label => "Data Table";

    public TwoVariableDataTableCommand(
        GridRange tableRange,
        CellAddress formulaCell,
        CellAddress rowInputCell,
        CellAddress columnInputCell)
    {
        _tableRange = tableRange;
        _formulaCell = formulaCell;
        _rowInputCell = rowInputCell;
        _columnInputCell = columnInputCell;
    }

    public CommandOutcome Apply(ICommandContext ctx)
    {
        var validation = DataTableCommandGuards.ValidateInputs(
            ctx,
            _tableRange,
            _formulaCell,
            [_rowInputCell, _columnInputCell],
            out var sheet,
            out var formula);
        if (validation is not null)
            return validation;

        _snapshot = [];
        var affected = new List<CellAddress>();
        for (uint row = _tableRange.Start.Row + 1; row <= _tableRange.End.Row; row++)
        {
            var columnTrialInputAddress = new CellAddress(_tableRange.Start.Sheet, row, _tableRange.Start.Col);
            for (uint col = _tableRange.Start.Col + 1; col <= _tableRange.End.Col; col++)
            {
                var rowTrialInputAddress = new CellAddress(_tableRange.Start.Sheet, _tableRange.Start.Row, col);
                var outputAddress = new CellAddress(_tableRange.Start.Sheet, row, col);
                var rewritten = DataTableFormulaRewriter.ReplaceCellReference(formula!, _columnInputCell, columnTrialInputAddress);
                rewritten = DataTableFormulaRewriter.ReplaceCellReference(rewritten, _rowInputCell, rowTrialInputAddress);
                _snapshot.Add((outputAddress, sheet!.GetCell(outputAddress)?.Clone()));
                sheet.SetCell(outputAddress, Cell.FromFormula(rewritten));
                affected.Add(outputAddress);
            }
        }

        _applied = true;
        return new CommandOutcome(true, AffectedCells: affected);
    }

    public void Revert(ICommandContext ctx)
    {
        if (!_applied || _snapshot is null)
            return;

        var sheet = ctx.GetSheet(_tableRange.Start.Sheet);
        foreach (var (address, previousCell) in _snapshot)
        {
            if (previousCell is null)
                sheet.ClearCell(address);
            else
                sheet.SetCell(address, previousCell.Clone());
        }

        _applied = false;
    }
}

internal static class DataTableCommandGuards
{
    public static CommandOutcome? ValidateInputs(
        ICommandContext ctx,
        GridRange tableRange,
        CellAddress formulaCell,
        ReadOnlySpan<CellAddress> inputCells,
        out Sheet? sheet,
        out string? formula)
    {
        sheet = null;
        formula = null;

        if (tableRange.Start.Sheet != tableRange.End.Sheet ||
            formulaCell.Sheet != tableRange.Start.Sheet ||
            HasInputCellOnDifferentSheet(tableRange, inputCells))
        {
            return new CommandOutcome(false, "Data Table cells must be on one sheet.");
        }

        if (tableRange.RowCount < 2 || tableRange.ColCount < 2)
            return new CommandOutcome(false, "Data Table requires at least two rows and two columns.");

        sheet = ctx.GetSheet(tableRange.Start.Sheet);
        formula = sheet.GetCell(formulaCell)?.FormulaText;
        if (string.IsNullOrWhiteSpace(formula))
            return new CommandOutcome(false, "Data Table formula cell must contain a formula.");

        return null;
    }

    private static bool HasInputCellOnDifferentSheet(GridRange tableRange, ReadOnlySpan<CellAddress> inputCells)
    {
        foreach (var inputCell in inputCells)
        {
            if (inputCell.Sheet != tableRange.Start.Sheet)
                return true;
        }

        return false;
    }
}

internal static class DataTableFormulaRewriter
{
    public static string ReplaceCellReference(string formula, CellAddress from, CellAddress to)
    {
        var pattern = $@"(?<![A-Za-z0-9_])\$?{Regex.Escape(CellAddress.NumberToColumnName(from.Col))}\$?{from.Row}(?![A-Za-z0-9_])";
        return Regex.Replace(formula, pattern, to.ToA1(), RegexOptions.IgnoreCase);
    }
}
