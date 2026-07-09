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

    /// <summary>
    /// Current depth of the undo stack. Exposed so <see cref="WorkbookSession"/> can record a
    /// save-point depth (mirroring the WPF host's <c>WorkbookDocumentState.SavedUndoDepth</c>) and
    /// detect when Undo/Redo returns the workbook to that point.
    /// </summary>
    public int GetUndoStackDepth(WorkbookId workbookId) =>
        _commandBus.GetUndoStackDepth(workbookId);

    /// <summary>
    /// Current monotonic version token of the undo stack. See
    /// <see cref="ICommandBus.GetUndoStackVersion"/> for why this, not depth alone, is the robust
    /// save-point identity check.
    /// </summary>
    public long GetUndoStackVersion(WorkbookId workbookId) =>
        _commandBus.GetUndoStackVersion(workbookId);

    public RecalcReport? RecalculateIfAutomatic(Workbook workbook, IReadOnlyList<CellAddress> affectedCells)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(affectedCells);

        return workbook.CalculationMode is WorkbookCalculationMode.Automatic or WorkbookCalculationMode.AutomaticExceptDataTables
            ? _recalcEngine.Recalculate(workbook, affectedCells)
            : null;
    }

    /// <summary>
    /// Recalculates <paramref name="affectedCells"/> unconditionally, independent of the workbook's
    /// <see cref="WorkbookCalculationMode"/>. Unlike <see cref="RecalculateIfAutomatic"/> (used after
    /// live cell edits, where Manual mode intentionally defers recalculation until the user asks for
    /// it), some report-generation flows need each intermediate state actually computed no matter
    /// the calc mode -- e.g. Scenario Summary applies one scenario's values at a time and must read
    /// each one's genuinely recalculated result cells rather than repeating the same stale
    /// pre-report value in every scenario column (see ScenarioSummaryReportCommand).
    /// </summary>
    public RecalcReport RecalculateAlways(Workbook workbook, IReadOnlyList<CellAddress> affectedCells)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(affectedCells);

        return _recalcEngine.Recalculate(workbook, affectedCells);
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

        if (!editResult.Success)
            return WorkbookGoalSeekResult.ApplyFailed(request, seekResult, editResult);

        // Excel always refreshes the set cell (and the rest of the dependency chain from the
        // changing cell) once Goal Seek applies its result, even when the workbook is in Manual
        // calculation mode — Goal Seek's recalculation is a deliberate one-time action, not subject
        // to the "only recalc on F9" rule that otherwise governs Manual mode. ApplyHistoryOutcome
        // above already ran RecalculateIfAutomatic, which is a no-op outside Automatic mode, so
        // force the recalculation here when it was skipped, or the set cell would keep displaying
        // its pre-seek value until the user manually recalculates.
        if (workbook.CalculationMode != WorkbookCalculationMode.Automatic)
        {
            var manualRecalcReport = _recalcEngine.Recalculate(workbook, [request.ChangingCell]);
            editResult = editResult with { RecalcReport = manualRecalcReport };
        }

        return WorkbookGoalSeekResult.AppliedResult(request, seekResult, editResult);
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

    // N44: mirrors FreeX.Core.Commands.CommandGuards.CanEditCell (internal to that assembly and not
    // visible here) so Goal Seek's pre-validation agrees with the authoritative guard that
    // GoalSeekCommand.Apply itself runs. A range listed in Sheet.AllowEditRanges only grants access
    // when it has no Allow-Edit-Range password, or the password has already been unlocked this
    // session (Sheet.UnlockedAllowEditRanges) -- otherwise fall through to the locked-style check
    // below, same as an unlisted cell.
    private static bool CanEditCell(Workbook workbook, Sheet sheet, CellAddress address)
    {
        if (!sheet.IsProtected)
            return true;

        foreach (var range in sheet.AllowEditRanges)
        {
            if (!range.Contains(address))
                continue;

            var isPasswordProtected = sheet.AllowEditRangePasswords.TryGetValue(range, out var stored) &&
                !string.IsNullOrEmpty(stored);
            if (!isPasswordProtected || sheet.UnlockedAllowEditRanges.Contains(range))
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
