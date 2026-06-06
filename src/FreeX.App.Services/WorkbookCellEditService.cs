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

    public bool CanUndo(WorkbookId workbookId) =>
        _commandBus.CanUndo(workbookId);

    public bool CanRedo(WorkbookId workbookId) =>
        _commandBus.CanRedo(workbookId);

    public WorkbookCellEditResult UndoLastEdit(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        return ApplyHistoryOutcome(workbook, _commandBus.Undo(workbook.Id));
    }

    public WorkbookCellEditResult RedoLastEdit(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        return ApplyHistoryOutcome(workbook, _commandBus.Redo(workbook.Id));
    }

    public WorkbookCellEditResult ExecuteEditCommand(Workbook workbook, IWorkbookCommand command)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(command);

        return ApplyHistoryOutcome(workbook, _commandBus.Execute(workbook.Id, command));
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
        return ExecuteEditCommand(workbook, new EditCellsCommand(sheetId, [(address, newCell)]));
    }

    private WorkbookCellEditResult ApplyHistoryOutcome(Workbook workbook, CommandOutcome outcome)
    {
        if (!outcome.Success)
        {
            return new WorkbookCellEditResult(
                false,
                outcome.ErrorMessage,
                outcome.AffectedCells ?? [],
                RecalcReport: null);
        }

        var affectedCells = outcome.AffectedCells ?? [];
        UpdateFormulaDependencies(workbook, affectedCells);
        var recalcReport = workbook.CalculationMode == WorkbookCalculationMode.Automatic
            ? _recalcEngine.Recalculate(workbook, affectedCells)
            : null;

        return new WorkbookCellEditResult(true, null, affectedCells, recalcReport);
    }

    private void UpdateFormulaDependencies(Workbook workbook, IReadOnlyList<CellAddress> affectedCells)
    {
        foreach (var affected in affectedCells)
        {
            var cell = workbook.GetSheet(affected.Sheet)?.GetCell(affected);
            if (cell?.FormulaText is null)
            {
                _recalcEngine.ClearFormulaDependencies(affected);
                continue;
            }

            try
            {
                var ast = FormulaEvaluator.ParseFormula(cell.FormulaText);
                _recalcEngine.RegisterFormulaDependencies(affected, ast, affected.Sheet, workbook);
            }
            catch (FormulaParseException)
            {
                _recalcEngine.ClearFormulaDependencies(affected);
            }
        }
    }
}
