using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.App.Services;

public sealed class WorkbookCellEditService
{
    private readonly ICommandBus _commandBus;
    private readonly RecalcEngine _recalcEngine;

    public WorkbookCellEditService(ICommandBus commandBus, RecalcEngine recalcEngine)
    {
        _commandBus = commandBus;
        _recalcEngine = recalcEngine;
    }

    public WorkbookCellEditResult CommitCellText(
        Workbook workbook,
        SheetId sheetId,
        CellAddress address,
        string text,
        bool useR1C1ReferenceStyle = false)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(text);

        if (!address.Sheet.Equals(sheetId))
            throw new ArgumentException("The edit address must belong to the target sheet.", nameof(address));

        var newCell = CellEntryParser.CreateCell(text, address, useR1C1ReferenceStyle);
        var outcome = _commandBus.Execute(workbook.Id, new EditCellsCommand(sheetId, [(address, newCell)]));
        if (!outcome.Success)
        {
            return new WorkbookCellEditResult(
                false,
                outcome.ErrorMessage,
                outcome.AffectedCells ?? [],
                RecalcReport: null);
        }

        var affectedCells = outcome.AffectedCells ?? [address];
        UpdateFormulaDependencies(workbook, affectedCells, newCell);
        var recalcReport = workbook.CalculationMode == WorkbookCalculationMode.Automatic
            ? _recalcEngine.Recalculate(workbook, affectedCells)
            : null;

        return new WorkbookCellEditResult(true, null, affectedCells, recalcReport);
    }

    private void UpdateFormulaDependencies(
        Workbook workbook,
        IReadOnlyList<CellAddress> affectedCells,
        Cell newCell)
    {
        if (newCell.FormulaText is { } formulaText)
        {
            try
            {
                var ast = FormulaEvaluator.ParseFormula(formulaText);
                foreach (var affected in affectedCells)
                    _recalcEngine.RegisterFormulaDependencies(affected, ast, affected.Sheet, workbook);
            }
            catch (FormulaParseException)
            {
                foreach (var affected in affectedCells)
                    _recalcEngine.ClearFormulaDependencies(affected);
            }

            return;
        }

        foreach (var affected in affectedCells)
            _recalcEngine.ClearFormulaDependencies(affected);
    }
}
