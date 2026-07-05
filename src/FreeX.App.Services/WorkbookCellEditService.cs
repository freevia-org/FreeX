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

    /// <summary>
    /// Executes <paramref name="commandFactory"/> as a repeatable command (F4 / Repeat Last
    /// Action), matching the WPF host's <c>TryExecuteRepeatable*</c> helpers. The factory is
    /// invoked again by <see cref="RepeatLastEdit"/> so it must re-resolve any live state (e.g.
    /// the current selection) rather than closing over a stale range.
    /// </summary>
    public WorkbookCellEditResult ExecuteRepeatableEditCommand(Workbook workbook, Func<IWorkbookCommand> commandFactory)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(commandFactory);

        return ApplyHistoryOutcome(workbook, _commandBus.ExecuteRepeatable(workbook.Id, commandFactory));
    }

    /// <summary>Repeats the last repeatable command (F4), matching Excel/the WPF host.</summary>
    public WorkbookCellEditResult RepeatLastEdit(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        return ApplyHistoryOutcome(workbook, _commandBus.RepeatLast(workbook.Id));
    }

    /// <summary>Whether a repeatable command is available to replay via <see cref="RepeatLastEdit"/>.</summary>
    public bool CanRepeatLastEdit(WorkbookId workbookId) =>
        _commandBus.CanRepeat(workbookId);

    public RecalcReport? RecalculateIfAutomatic(Workbook workbook, IReadOnlyList<CellAddress> affectedCells)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(affectedCells);

        return workbook.CalculationMode == WorkbookCalculationMode.Automatic
            ? _recalcEngine.Recalculate(workbook, affectedCells)
            : null;
    }

    /// <summary>Forces a full recalculation of every formula in the workbook (F9 / Calculate Now).</summary>
    public RecalcReport RecalculateAll(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        return _recalcEngine.RecalculateAllFormulas(workbook);
    }

    /// <summary>Forces a recalculation of every formula on a single worksheet (Shift+F9 / Calculate Sheet).</summary>
    public RecalcReport RecalculateSheet(Workbook workbook, SheetId sheetId)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        return _recalcEngine.RecalculateSheetFormulas(workbook, sheetId);
    }

    public WorkbookGoalSeekResult ExecuteGoalSeek(Workbook workbook, GoalSeekRequest request)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(request);

        if (TryValidateGoalSeekRequest(workbook, request, out var errorMessage))
            return WorkbookGoalSeekResult.Invalid(request, errorMessage);

        var seekResult = GoalSeekService.Seek(
            workbook,
            _recalcEngine,
            request.SetCell,
            request.TargetValue,
            request.ChangingCell);

        if (!seekResult.Converged)
            return WorkbookGoalSeekResult.NotConverged(request, seekResult);

        var editResult = ExecuteEditCommand(
            workbook,
            new GoalSeekCommand(request.ChangingCell, seekResult.FoundValue));

        return editResult.Success
            ? WorkbookGoalSeekResult.AppliedResult(request, seekResult, editResult)
            : WorkbookGoalSeekResult.ApplyFailed(request, seekResult, editResult);
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
        var recalcReport = RecalculateIfAutomatic(workbook, affectedCells);

        return new WorkbookCellEditResult(true, null, affectedCells, recalcReport);
    }

    private static bool TryValidateGoalSeekRequest(
        Workbook workbook,
        GoalSeekRequest request,
        out string errorMessage)
    {
        if (!double.IsFinite(request.TargetValue))
        {
            errorMessage = "Goal Seek target value must be a finite number.";
            return true;
        }

        if (!IsValidAddress(request.SetCell) || !IsValidAddress(request.ChangingCell))
        {
            errorMessage = "Goal Seek cell references must be inside the worksheet bounds.";
            return true;
        }

        if (request.SetCell == request.ChangingCell)
        {
            errorMessage = "Goal Seek set cell and changing cell must be different.";
            return true;
        }

        if (workbook.GetSheet(request.SetCell.Sheet) is null)
        {
            errorMessage = "Goal Seek set cell sheet was not found.";
            return true;
        }

        if (workbook.GetSheet(request.ChangingCell.Sheet) is not { } changingSheet)
        {
            errorMessage = "Goal Seek changing cell sheet was not found.";
            return true;
        }

        if (!CanEditCell(workbook, changingSheet, request.ChangingCell))
        {
            errorMessage = "The sheet is protected.";
            return true;
        }

        errorMessage = "";
        return false;
    }

    private static bool IsValidAddress(CellAddress address) =>
        address.Row is >= 1 and <= CellAddress.MaxRow &&
        address.Col is >= 1 and <= CellAddress.MaxCol;

    private static bool CanEditCell(Workbook workbook, Sheet sheet, CellAddress address)
    {
        if (!sheet.IsProtected)
            return true;

        foreach (var range in sheet.AllowEditRanges)
        {
            if (range.Contains(address))
                return true;
        }

        var styleId = sheet.GetCell(address)?.StyleId ??
            sheet.GetStyleOnly(address.Row, address.Col) ??
            StyleId.Default;
        return !workbook.GetStyle(styleId).Locked;
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
