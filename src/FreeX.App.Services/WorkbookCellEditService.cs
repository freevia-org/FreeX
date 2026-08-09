using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

using CommandHistoryEntry = Free.Shared.Commands.CommandHistoryEntry;

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

    public IReadOnlyList<CommandHistoryEntry> GetUndoHistory(WorkbookId workbookId, int maxCount) =>
        _commandBus is ICommandHistoryProvider historyProvider
            ? historyProvider.GetUndoHistory(workbookId, maxCount)
            : [];

    public IReadOnlyList<CommandHistoryEntry> GetRedoHistory(WorkbookId workbookId, int maxCount) =>
        _commandBus is ICommandHistoryProvider historyProvider
            ? historyProvider.GetRedoHistory(workbookId, maxCount)
            : [];

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

    /// <summary>
    /// Releases the recalculation engine's workbook-keyed state when the owning
    /// session is no longer reachable by any workbook window.
    /// </summary>
    internal void RetireWorkbook(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        _recalcEngine.RetireWorkbook(workbook);
    }

    public RecalcReport? RecalculateIfAutomatic(Workbook workbook, IReadOnlyList<CellAddress> affectedCells)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(affectedCells);

        // R118-calc-except-data-tables: both Automatic and AutomaticExceptDataTables recalculate
        // live on every edit, but "Automatic Except for Data Tables" must leave any What-If
        // Analysis Data Table's result body frozen at its last computed value until the user
        // explicitly asks for it (F9 / Shift+F9 -- see RecalculateAll/RecalculateSheet, neither of
        // which passes this flag, so they always force a fresh Data Table result). Passing
        // skipDataTableBodyCells here is what actually implements that carve-out -- previously this
        // method treated the two modes identically, so a Data Table recalculated on every ordinary
        // edit exactly as in plain Automatic mode, defeating the whole point of selecting this
        // option (see CalculationOptions.cs's WorkbookCalculationMode.AutomaticExceptDataTables doc
        // comment).
        return workbook.CalculationMode switch
        {
            WorkbookCalculationMode.Automatic => _recalcEngine.Recalculate(workbook, affectedCells),
            WorkbookCalculationMode.AutomaticExceptDataTables =>
                _recalcEngine.Recalculate(workbook, affectedCells, skipDataTableBodyCells: true),
            _ => null
        };
    }

    /// <summary>
    /// Applies the workbook's normal post-edit calculation policy. Manual mode still evaluates
    /// formulas that were entered by this edit once, while leaving dependent formulas deferred.
    /// </summary>
    public RecalcReport? RecalculateAfterChanges(Workbook workbook, IReadOnlyList<CellAddress> affectedCells)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(affectedCells);

        return RecalculateIfAutomatic(workbook, affectedCells)
            ?? RecalculateFreshlyEnteredFormulasOnce(workbook, affectedCells);
    }

    /// <summary>
    /// Recalculates the dirty dependency graph for Calculate Now (F9), forcing Data Table bodies
    /// fresh without rebuilding and evaluating every formula in the workbook.
    /// </summary>
    public RecalcReport RecalculateDirty(Workbook workbook)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        if (workbook.CalculationMode == WorkbookCalculationMode.Manual)
            return RecalculateAll(workbook);

        var context = new WorkbookCommandContext(workbook);
        List<CellAddress>? refreshedCells = null;
        foreach (var sheet in workbook.Sheets)
        {
            var refreshed = DataTableAutoRefreshEffects.RefreshAllTables(context, sheet);
            if (refreshed.Count > 0)
                (refreshedCells ??= []).AddRange(refreshed);
        }

        var report = _recalcEngine.Recalculate(workbook, refreshedCells ?? []);
        workbook.HasPendingManualRecalculation = false;
        return report;
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

        // R118-calc-except-data-tables: F9 always forces every Data Table fresh regardless of calc
        // mode, including re-deriving a body's formula TEXT from its driver formula's current state --
        // not just re-evaluating whatever text it already has -- to pick up a driver/precedent edit
        // that DataTableAutoRefreshEffects.Apply's own CalculationMode gate left frozen (untouched) at
        // edit time in AutomaticExceptDataTables/Manual mode. Must run before the ordinary recalc below
        // so any body cell it rewrites gets evaluated in the very same pass rather than staying at
        // whatever value the just-rewritten (freshly blank) Cell started with.
        var ctx = new WorkbookCommandContext(workbook);
        foreach (var sheet in workbook.Sheets)
            DataTableAutoRefreshEffects.RefreshAllTables(ctx, sheet);

        var report = _recalcEngine.RecalculateAllFormulas(workbook);

        // R128-status-bar-calculate-indicator: F9 / Calculate Now is exactly the action Excel's
        // "Calculate" cell-mode indicator is warning the user to take -- once it has run, nothing is
        // left un-recalculated, so clear whatever pending state Manual mode accumulated (see
        // Workbook.HasPendingManualRecalculation). Also covers the Automatic/AutomaticExceptDataTables
        // mode-switch handlers in both shells, which call this immediately after leaving Manual mode.
        workbook.HasPendingManualRecalculation = false;

        return report;
    }

    /// <summary>Forces a recalculation of every formula on a single worksheet (Shift+F9 / Calculate Sheet).</summary>
    public RecalcReport RecalculateSheet(Workbook workbook, SheetId sheetId)
    {
        ArgumentNullException.ThrowIfNull(workbook);

        // See RecalculateAll's matching comment -- Shift+F9 gets the same "always force Data Tables
        // fresh" treatment, scoped to just this sheet.
        if (workbook.GetSheet(sheetId) is { } sheet)
            DataTableAutoRefreshEffects.RefreshAllTables(new WorkbookCommandContext(workbook), sheet);

        var report = _recalcEngine.RecalculateSheetFormulas(workbook, sheetId);

        // R128-status-bar-calculate-indicator: see RecalculateAll's matching comment. The pending flag
        // is workbook-scoped (matching Excel's own workbook-level "Calculate" indicator), so Shift+F9
        // clears it the same as F9 rather than tracking staleness per sheet.
        workbook.HasPendingManualRecalculation = false;

        return report;
    }

    /// <summary>
    /// Cells the engine's most recent recalculation classified as part of a non-iterative circular
    /// reference (see <see cref="RecalcEngine.CyclicCells"/>). Exposed so callers can feed
    /// <c>FormulaAuditingService.FindFormulaErrors</c>/<c>FindFormulaErrorIssues</c>'s
    /// <c>cyclicCells</c> parameter and surface the "Formulas with circular references"
    /// Error-Checking rule.
    /// </summary>
    public IReadOnlyCollection<CellAddress> CyclicCells => _recalcEngine.CyclicCells;

    public WorkbookGoalSeekResult ExecuteGoalSeek(Workbook workbook, GoalSeekRequest request)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(request);

        var proposal = FindGoalSeekProposal(workbook, request);
        if (!proposal.Success)
            return WorkbookGoalSeekResult.Invalid(request, proposal.ErrorMessage!);

        var seekResult = proposal.SeekResult!;

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

    /// <summary>Calculates a Goal Seek proposal without applying it to the workbook.</summary>
    public GoalSeekResult FindGoalSeekSolution(Workbook workbook, GoalSeekRequest request)
    {
        var proposal = FindGoalSeekProposal(workbook, request);
        if (!proposal.Success)
            throw new ArgumentException(proposal.ErrorMessage, nameof(request));

        return proposal.SeekResult!;
    }

    /// <summary>Validates and calculates a Goal Seek proposal without applying it.</summary>
    public WorkbookGoalSeekProposal FindGoalSeekProposal(Workbook workbook, GoalSeekRequest request)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(request);

        if (TryValidateGoalSeekRequest(workbook, request, out var errorMessage))
            return WorkbookGoalSeekProposal.Invalid(request, errorMessage);

        var maxIterations = workbook.MaxCalculationIterations is int configuredIterations && configuredIterations > 0
            ? configuredIterations
            : 1000;
        var tolerance = workbook.MaxCalculationChange is double configuredChange && configuredChange > 0
            ? configuredChange
            : 1e-6;

        return WorkbookGoalSeekProposal.Ready(
            request,
            GoalSeekService.Seek(
                workbook,
                _recalcEngine,
                request.SetCell,
                request.TargetValue,
                request.ChangingCell,
                maxIterations,
                tolerance));
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

        var plan = CellEntryCommitPlanner.BuildSingle(
            text,
            address,
            useR1C1ReferenceStyle,
            workbook);
        if (!plan.Success)
            return new WorkbookCellEditResult(false, plan.ErrorMessage, [], RecalcReport: null);

        return ExecuteEditCommand(workbook, new EditCellsCommand(sheetId, plan.Edits));
    }

    private WorkbookCellEditResult ApplyHistoryOutcome(Workbook workbook, CommandOutcome outcome)
    {
        if (!outcome.Success)
        {
            return new WorkbookCellEditResult(
                false,
                outcome.ErrorMessage,
                outcome.AffectedCells ?? [],
                RecalcReport: null,
                DrawingObjectSelection: outcome.DrawingObjectSelection);
        }

        var affectedCells = outcome.AffectedCells ?? [];
        if (outcome.IsNoOp)
        {
            return new WorkbookCellEditResult(
                true,
                null,
                affectedCells,
                RecalcReport: null,
                IsNoOp: true,
                DrawingObjectSelection: outcome.DrawingObjectSelection);
        }

        UpdateFormulaDependencies(workbook, affectedCells);

        // R84-calc-crosssheet-3d-5-1: Undo/Redo of a structural sheet command (Add/Delete/Move/
        // Duplicate Sheet) is flagged via RequiresFullRecalc (see IWholeWorkbookRecalcCommand)
        // because it reports no AffectedCells of its own, even though it can change which sheets
        // fall inside a 3-D span reference (e.g. =SUM(Sheet1:Sheet3!A1)). RecalculateIfAutomatic
        // would short-circuit to an empty recalc for an empty cell list, leaving those span
        // aggregates stale until the next F9 -- force a full recalculation instead, mirroring the
        // explicit RecalculateWorkbook() compensation on the forward Execute path (WorkbookSession's
        // DeleteActiveSheet/MoveActiveSheetTo/DuplicateActiveSheet) that Undo/Redo, which calls
        // straight into the command bus, never reaches.
        var recalcReport = outcome.RequiresFullRecalc
            ? RecalculateAll(workbook)
            : RecalculateAfterChanges(workbook, affectedCells);

        if (!outcome.RequiresFullRecalc &&
            workbook.CalculationMode == WorkbookCalculationMode.Manual &&
            affectedCells.Count > 0)
        {
            workbook.HasPendingManualRecalculation = true;
        }

        return new WorkbookCellEditResult(
            true,
            null,
            affectedCells,
            recalcReport,
            IsNoOp: outcome.IsNoOp,
            DrawingObjectSelection: outcome.DrawingObjectSelection);
    }

    /// <summary>
    /// Manual-calculation-mode counterpart to <see cref="RecalculateIfAutomatic"/> (which is a
    /// no-op outside Automatic/AutomaticExceptDataTables). Excel always computes a formula the
    /// instant it is entered or edited, no matter the calculation mode -- only recalculation
    /// triggered by a later edit to one of that formula's PRECEDENTS is what "Manual" mode defers
    /// until the next F9 (see <see cref="RecalculateAll"/>). This restricts the recalculation to
    /// the cells among <paramref name="affectedCells"/> that are themselves formulas (i.e. the
    /// ones the user just typed/edited), so a precedent-only edit -- e.g. committing a plain value
    /// into a cell some other, untouched formula depends on -- correctly leaves that other formula
    /// stale instead of rippling through it. Returns null (matching RecalculateIfAutomatic's
    /// "nothing to do" contract) when none of the affected cells hold a formula.
    /// </summary>
    private RecalcReport? RecalculateFreshlyEnteredFormulasOnce(Workbook workbook, IReadOnlyList<CellAddress> affectedCells)
    {
        List<CellAddress>? enteredFormulaCells = null;
        foreach (var address in affectedCells)
        {
            if (workbook.GetSheet(address.Sheet)?.GetCell(address)?.HasFormula == true)
                (enteredFormulaCells ??= []).Add(address);
        }

        return enteredFormulaCells is null ? null : _recalcEngine.Recalculate(workbook, enteredFormulaCells);
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

        if (workbook.GetSheet(request.SetCell.Sheet) is not { } setSheet)
        {
            errorMessage = "Goal Seek set cell sheet was not found.";
            return true;
        }

        if (workbook.GetSheet(request.ChangingCell.Sheet) is not { } changingSheet)
        {
            errorMessage = "Goal Seek changing cell sheet was not found.";
            return true;
        }

        if (string.IsNullOrEmpty(setSheet.GetCell(request.SetCell)?.FormulaText))
        {
            errorMessage = "Goal Seek set cell must contain a formula.";
            return true;
        }

        // R90-app-goalseek-whatif-5-1: Excel refuses to run Goal Seek when the changing cell
        // itself holds a formula -- it requires a constant there so the search has something it
        // can freely overwrite. Without this guard, GoalSeekCommand.Apply (via GoalSeekService.Seek
        // during the search, and again once it applies) unconditionally replaces the changing
        // cell's content with a bare NumberValue, silently destroying the user's formula.
        if (!string.IsNullOrEmpty(changingSheet.GetCell(request.ChangingCell)?.FormulaText))
        {
            errorMessage = "Goal Seek changing cell must contain a constant value, not a formula.";
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
