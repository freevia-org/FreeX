using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

/// <summary>
/// R128-status-bar-calculate-indicator: R79 (see R79_StatusBarCalculateIndicatorTests.cs) added
/// <see cref="StatusBarTextResourceKeys.CellModeResourceKey"/> and the "StatusBar_CalculateText"
/// resource but nothing in either shell ever called it -- <see cref="Workbook.CalculationMode"/> had
/// no matching "a recalculation is pending" flag, and neither <c>StatusBarRefreshPlanner</c> (the
/// WPF host's real status-bar choke point) nor the Avalonia shell's status-bar renderer ever passed
/// calc-mode state into the ready-text resolution. These tests cover the actual wiring: the new
/// <see cref="Workbook.HasPendingManualRecalculation"/> flag set/cleared by the real
/// <see cref="WorkbookCellEditService"/> entry point (the same one both shells route every edit,
/// undo, and redo through), and the shared planners that thread it into "Calculate" vs "Ready" text.
/// </summary>
public sealed class R128_StatusBarCalculateIndicatorWiringTests
{
    // ── Workbook.HasPendingManualRecalculation set/cleared through the real edit pipeline ──────────

    [Fact]
    public void CommitCellText_ManualMode_PrecedentEdit_SetsHasPendingManualRecalculation()
    {
        // Failing before the fix: Workbook had no HasPendingManualRecalculation flag at all, and
        // WorkbookCellEditService.ApplyHistoryOutcome never set anything resembling it, so a Manual
        // -mode precedent edit that left B1 stale (see the sibling
        // CommitCellText_LeavesDependentsStaleWhenCalculationModeIsManual test) left no trace the
        // status bar could read to show Excel's "Calculate" indicator.
        var (workbook, sheet, _, service, recalcEngine) = CreateEditService();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetFormula(b1, "A1+1");
        recalcEngine.RecalculateAllFormulas(workbook);
        workbook.CalculationMode = WorkbookCalculationMode.Manual;
        workbook.HasPendingManualRecalculation.Should().BeFalse("nothing has been edited yet");

        var result = service.CommitCellText(workbook, sheet.Id, a1, "4");

        result.Success.Should().BeTrue();
        sheet.GetCell(b1)!.Value.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(2, "B1 must stay stale -- this is the same defect R79 covered");
        workbook.HasPendingManualRecalculation.Should().BeTrue(
            "Excel shows the status bar's \"Calculate\" indicator the instant a Manual-mode edit " +
            "could have left some formula stale");
    }

    [Fact]
    public void CommitCellText_ManualMode_FreshFormulaEntry_AlsoSetsHasPendingManualRecalculation()
    {
        // Sibling family member: even the "freshly entered formula computes once immediately" path
        // (R79-calc-volatile-recalc-5-2) only recalculates the cell the user just typed into, never
        // any OTHER cell that might depend on it -- so it must set the flag too, not just the
        // precedent-edit path above. Matches Excel's own coarse, workbook-wide "something changed"
        // indicator rather than a narrower "this specific formula is stale" signal.
        var (workbook, sheet, _, service, _) = CreateEditService();
        workbook.CalculationMode = WorkbookCalculationMode.Manual;
        var c1 = new CellAddress(sheet.Id, 1, 3);

        var result = service.CommitCellText(workbook, sheet.Id, c1, "=1+1");

        result.Success.Should().BeTrue();
        workbook.HasPendingManualRecalculation.Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CommitCellText_AutomaticModes_NeverSetHasPendingManualRecalculation(bool exceptDataTables)
    {
        // No-regression sibling covering the OTHER two WorkbookCalculationMode family members
        // (Automatic and AutomaticExceptDataTables): both recalculate synchronously on every edit, so
        // Excel never shows "Calculate" for them (see R79's CellModeResourceKey_NotBothManualAndPending
        // -_ReturnsReadyText) -- the flag must stay false no matter how many edits land.
        var (workbook, sheet, _, service, _) = CreateEditService();
        workbook.CalculationMode = exceptDataTables
            ? WorkbookCalculationMode.AutomaticExceptDataTables
            : WorkbookCalculationMode.Automatic;
        var a1 = new CellAddress(sheet.Id, 1, 1);

        var result = service.CommitCellText(workbook, sheet.Id, a1, "4");

        result.Success.Should().BeTrue();
        workbook.HasPendingManualRecalculation.Should().BeFalse();
    }

    [Fact]
    public void RecalculateAll_ClearsHasPendingManualRecalculation()
    {
        // F9 / Calculate Now is exactly the action the indicator is telling the user to take.
        var (workbook, sheet, _, service, recalcEngine) = CreateEditService();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetFormula(b1, "A1+1");
        recalcEngine.RecalculateAllFormulas(workbook);
        workbook.CalculationMode = WorkbookCalculationMode.Manual;
        service.CommitCellText(workbook, sheet.Id, a1, "4");
        workbook.HasPendingManualRecalculation.Should().BeTrue("precondition: edit left B1 stale");

        var report = service.RecalculateAll(workbook);

        report.Should().NotBeNull();
        sheet.GetCell(b1)!.Value.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(5, "F9 must actually catch B1 up, not just clear the flag");
        workbook.HasPendingManualRecalculation.Should().BeFalse();
    }

    [Fact]
    public void RecalculateSheet_ClearsHasPendingManualRecalculation()
    {
        // Shift+F9 / Calculate Sheet is the per-sheet sibling of RecalculateAll above.
        var (workbook, sheet, _, service, recalcEngine) = CreateEditService();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetFormula(b1, "A1+1");
        recalcEngine.RecalculateAllFormulas(workbook);
        workbook.CalculationMode = WorkbookCalculationMode.Manual;
        service.CommitCellText(workbook, sheet.Id, a1, "4");
        workbook.HasPendingManualRecalculation.Should().BeTrue("precondition: edit left B1 stale");

        service.RecalculateSheet(workbook, sheet.Id);

        sheet.GetCell(b1)!.Value.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(5);
        workbook.HasPendingManualRecalculation.Should().BeFalse();
    }

    [Fact]
    public void UndoRedo_RouteThroughSameFlagWiring()
    {
        // Undo/Redo both call WorkbookCellEditService.ApplyHistoryOutcome too (the single choke
        // point) -- confirm the flag reacts to a Redo the same way a forward edit does, proving the
        // choke point genuinely covers every AffectedCells-producing path, not just CommitCellText.
        var (workbook, sheet, _, service, recalcEngine) = CreateEditService();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new NumberValue(1));
        recalcEngine.RecalculateAllFormulas(workbook);
        workbook.CalculationMode = WorkbookCalculationMode.Manual;
        service.CommitCellText(workbook, sheet.Id, a1, "4");
        service.RecalculateAll(workbook);
        workbook.HasPendingManualRecalculation.Should().BeFalse("cleared by the F9 above");

        service.UndoLastEdit(workbook);
        workbook.HasPendingManualRecalculation.Should().BeTrue("Undo re-applies a Manual-mode edit");

        service.RecalculateAll(workbook);
        var redoResult = service.RedoLastEdit(workbook);

        redoResult.Success.Should().BeTrue();
        workbook.HasPendingManualRecalculation.Should().BeTrue("Redo re-applies the edit again");
    }

    // ── StatusBarRefreshPlanner (the WPF host's real status-bar choke point) ────────────────────────

    private sealed class CalcModeAwareTextProvider : IStatusBarTextProvider
    {
        public string GetReadyText() => "Ready";

        public string GetReadyText(bool isManualCalculationMode, bool hasPendingRecalculation) =>
            isManualCalculationMode && hasPendingRecalculation ? "Calculate" : "Ready";

        public string GetReadoutFormat(StatusBarReadoutKind kind) => "{0}";

        public string GetReadoutLabel(StatusBarReadoutKind kind) => kind.ToString();
    }

    private static readonly CalcModeAwareTextProvider CalcAwareProvider = new();

    [Theory]
    [InlineData(false, false, "Ready")]
    [InlineData(false, true, "Ready")]
    [InlineData(true, false, "Ready")]
    [InlineData(true, true, "Calculate")]
    public void StatusBarRefreshPlanner_Build_NoSelection_ThreadsCalcModeIntoReadyText(
        bool isManualCalculationMode,
        bool hasPendingRecalculation,
        string expectedReadyText)
    {
        // Family completeness for the (isManualCalculationMode, hasPendingRecalculation) pair --
        // mirrors R79's CellModeResourceKey_NotBothManualAndPending_ReturnsReadyText matrix, but
        // through the actual production planner instead of the bare resource-key function.
        var plan = StatusBarRefreshPlanner.Build(
            sheet: null,
            selectedRange: null,
            selectionStats: null,
            isFileOperationProgressVisible: false,
            zoomPercent: 100,
            CalcAwareProvider,
            isManualCalculationMode: isManualCalculationMode,
            hasPendingRecalculation: hasPendingRecalculation);

        plan.Action.Should().Be(StatusBarRefreshAction.Ready);
        plan.ReadyText.Should().Be(expectedReadyText);
    }

    [Fact]
    public void StatusBarRefreshPlanner_Build_EmptyStats_ManualPending_ShowsCalculate()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        var cell = new CellAddress(sheet.Id, 1, 1);

        var plan = StatusBarRefreshPlanner.Build(
            sheet,
            new GridRange(cell, cell),
            new WorkbookSelectionStats(0, 0, 0, null, null, null),
            isFileOperationProgressVisible: false,
            zoomPercent: 100,
            CalcAwareProvider,
            isManualCalculationMode: true,
            hasPendingRecalculation: true);

        plan.Action.Should().Be(StatusBarRefreshAction.Ready);
        plan.ReadyText.Should().Be("Calculate");
    }

    [Fact]
    public void StatusBarRefreshPlanner_Build_EmptyStats_DataValidationPromptStillWinsOverCalculate()
    {
        // No-regression sibling: Excel shows the active cell's data-validation input prompt over the
        // cell-mode indicator -- the "Calculate" text must not clobber a genuine prompt.
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        var cell = new CellAddress(sheet.Id, 1, 1);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(cell, cell),
            ShowInputMessage = true,
            PromptTitle = "Input",
            PromptMessage = "Use a whole number"
        });

        var plan = StatusBarRefreshPlanner.Build(
            sheet,
            new GridRange(cell, cell),
            new WorkbookSelectionStats(0, 0, 0, null, null, null),
            isFileOperationProgressVisible: false,
            zoomPercent: 100,
            CalcAwareProvider,
            isManualCalculationMode: true,
            hasPendingRecalculation: true);

        plan.ReadyText.Should().Be("Input: Use a whole number");
    }

    // ── StatusBarReadyTextPlanner.NormalizeTransientReadyText (Avalonia's default-ready choke point) ─

    [Fact]
    public void NormalizeTransientReadyText_LiteralReady_ManualPending_ResolvesToCalculate()
    {
        // Failing before the fix: the calc-mode overload did not exist, and the literal "Ready"
        // placeholder (what ~40 Avalonia MainWindow.cs RefreshShell("Ready") call sites pass) was
        // never special-cased at all -- it passed straight through unchanged.
        var text = StatusBarReadyTextPlanner.NormalizeTransientReadyText(
            "Ready",
            CalcAwareProvider,
            isManualCalculationMode: true,
            hasPendingRecalculation: true);

        text.Should().Be("Calculate");
    }

    [Fact]
    public void NormalizeTransientReadyText_LiteralReady_AutomaticMode_StaysReady()
    {
        var text = StatusBarReadyTextPlanner.NormalizeTransientReadyText(
            "Ready",
            CalcAwareProvider,
            isManualCalculationMode: false,
            hasPendingRecalculation: false);

        text.Should().Be("Ready");
    }

    [Fact]
    public void NormalizeTransientReadyText_GenuineTransientMessage_PassesThroughEvenWhilePending()
    {
        // No-regression sibling: a real transient status message (e.g. "Recalculated all formulas")
        // must not be clobbered by the calc-mode substitution -- only the literal "Ready" placeholder
        // and the pre-existing "Showing "/"Hiding " cases are special-cased.
        var text = StatusBarReadyTextPlanner.NormalizeTransientReadyText(
            "Draw Border mode active",
            CalcAwareProvider,
            isManualCalculationMode: true,
            hasPendingRecalculation: true);

        text.Should().Be("Draw Border mode active");
    }

    [Fact]
    public void NormalizeTransientReadyText_ShowingPrefix_StillNormalizesToFallback()
    {
        // No-regression sibling: the pre-existing "Showing "/"Hiding " special case must survive the
        // new "Ready" literal check being added alongside it.
        var text = StatusBarReadyTextPlanner.NormalizeTransientReadyText(
            "Showing all sheets",
            CalcAwareProvider,
            isManualCalculationMode: true,
            hasPendingRecalculation: true);

        text.Should().Be("Calculate");
    }

    // ── ResourceKeyStatusBarTextProvider (the concrete provider both shells actually construct) ────

    [Fact]
    public void ResourceKeyStatusBarTextProvider_GetReadyText_ManualPending_ResolvesCalculateKey()
    {
        var provider = new ResourceKeyStatusBarTextProvider(key => key);

        provider.GetReadyText(isManualCalculationMode: true, hasPendingRecalculation: true)
            .Should().Be(StatusBarTextResourceKeys.CalculateText);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ResourceKeyStatusBarTextProvider_GetReadyText_NotBothManualAndPending_ResolvesReadyKey(
        bool isManualCalculationMode,
        bool hasPendingRecalculation)
    {
        var provider = new ResourceKeyStatusBarTextProvider(key => key);

        provider.GetReadyText(isManualCalculationMode, hasPendingRecalculation)
            .Should().Be(StatusBarTextResourceKeys.ReadyText);
    }

    private static (
        Workbook Workbook,
        Sheet Sheet,
        CommandBus CommandBus,
        WorkbookCellEditService Service,
        RecalcEngine RecalcEngine) CreateEditService()
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Sheet1");
        workbook.ActiveSheetIndex = 0;
        var recalcEngine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        var commandBus = new CommandBus(_ => new WorkbookCommandContext(workbook));
        var service = new WorkbookCellEditService(commandBus, recalcEngine);
        return (workbook, sheet, commandBus, service, recalcEngine);
    }
}
