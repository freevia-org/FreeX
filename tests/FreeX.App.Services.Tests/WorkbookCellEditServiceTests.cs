using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookCellEditServiceTests
{
    [Fact]
    public void CommitCellText_UsesCommandBusAndRecalculatesDependents()
    {
        var (workbook, sheet, commandBus, service, recalcEngine) = CreateEditService();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetFormula(b1, "A1+1");
        recalcEngine.RecalculateAllFormulas(workbook);

        var result = service.CommitCellText(workbook, sheet.Id, a1, "4");

        result.Success.Should().BeTrue();
        result.AffectedCells.Should().ContainSingle().Which.Should().Be(a1);
        commandBus.CanUndo(workbook.Id).Should().BeTrue();
        sheet.GetCell(a1)!.Value.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(4);
        sheet.GetCell(b1)!.Value.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(5);
    }

    [Fact]
    public void CommitCellText_ConvertsFormulaAndRecalculatesEditedFormula()
    {
        var (workbook, sheet, _, service, _) = CreateEditService();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b2 = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(a1, new NumberValue(3));

        var result = service.CommitCellText(workbook, sheet.Id, b2, "=R[-1]C[-1]*2", useR1C1ReferenceStyle: true);

        result.Success.Should().BeTrue();
        sheet.GetCell(b2)!.FormulaText.Should().Be("A1*2");
        sheet.GetCell(b2)!.Value.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(6);
    }

    [Fact]
    public void CommitCellText_LeavesDependentsStaleWhenCalculationModeIsManual()
    {
        var (workbook, sheet, _, service, recalcEngine) = CreateEditService();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(a1, new NumberValue(1));
        sheet.SetFormula(b1, "A1+1");
        recalcEngine.RecalculateAllFormulas(workbook);
        workbook.CalculationMode = WorkbookCalculationMode.Manual;

        var result = service.CommitCellText(workbook, sheet.Id, a1, "4");

        result.Success.Should().BeTrue();
        result.RecalcReport.Should().BeNull();
        sheet.GetCell(a1)!.Value.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(4);
        sheet.GetCell(b1)!.Value.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(2);
    }

    // ── R79-calc-volatile-recalc-5-2: a brand-new/freshly edited formula must still compute once
    // on entry even in Manual calculation mode -- only recalculation triggered by a later edit to
    // one of that formula's PRECEDENTS is deferred until the next F9 (see the sibling test below).

    [Fact]
    public void CommitCellText_ManualCalculationMode_ComputesNewlyEnteredFormulaOnce()
    {
        var (workbook, sheet, _, service, _) = CreateEditService();
        workbook.CalculationMode = WorkbookCalculationMode.Manual;
        var c1 = new CellAddress(sheet.Id, 1, 3);

        var result = service.CommitCellText(workbook, sheet.Id, c1, "=1+1");

        result.Success.Should().BeTrue();
        result.RecalcReport.Should().NotBeNull(
            "Excel always computes a brand-new formula once on entry, even in Manual calculation mode");
        sheet.GetCell(c1)!.Value.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(2);
    }

    // Sibling no-regression: once the freshly entered formula has computed, a later edit to one of
    // its PRECEDENTS (not the formula cell itself) must not cascade into a recompute -- it stays
    // stale until the user explicitly recalculates (F9), exactly like a pre-existing formula does.
    [Fact]
    public void CommitCellText_ManualCalculationMode_LaterPrecedentEditDoesNotRecomputeFreshlyEnteredFormula()
    {
        var (workbook, sheet, _, service, _) = CreateEditService();
        workbook.CalculationMode = WorkbookCalculationMode.Manual;
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetCell(a1, new NumberValue(5));

        var enterResult = service.CommitCellText(workbook, sheet.Id, c1, "=A1+1");

        enterResult.Success.Should().BeTrue();
        sheet.GetCell(c1)!.Value.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(6, "the freshly entered formula computes once immediately, using A1's current value");

        var precedentEditResult = service.CommitCellText(workbook, sheet.Id, a1, "100");

        precedentEditResult.Success.Should().BeTrue();
        precedentEditResult.RecalcReport.Should().BeNull(
            "editing a precedent (a1 has no formula of its own) must not trigger any recalculation in Manual mode");
        sheet.GetCell(c1)!.Value.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(6, "C1 must stay stale at its previously computed result until the user recalculates (F9)");
    }

    // R149-formula-volatility-manual-mode-fresh-formula-recalc: RecalculateFreshlyEnteredFormulasOnce
    // must compute ONLY the just-typed formula cell(s) -- it must not sweep every pre-existing
    // volatile cell (and its dependents) elsewhere in the workbook into the same pass. Before the
    // fix, this call unconditionally folded the whole registered volatile-cell set into the
    // traversal, so D1's RAND() silently re-rolled (and E1 recomputed from it) even though the
    // user only typed an unrelated brand-new formula into C1.
    [Fact]
    public void CommitCellText_ManualCalculationMode_UnrelatedNewFormulaDoesNotRerollExistingVolatileCells()
    {
        var (workbook, sheet, _, service, recalcEngine) = CreateEditService();
        var d1 = new CellAddress(sheet.Id, 1, 4);
        var e1 = new CellAddress(sheet.Id, 1, 5);
        var c1 = new CellAddress(sheet.Id, 1, 3);

        sheet.SetFormula(d1, "RAND()");
        sheet.SetFormula(e1, "D1+1");
        recalcEngine.RecalculateAllFormulas(workbook);
        var d1Baseline = sheet.GetCell(d1)!.Value;
        var e1Baseline = sheet.GetCell(e1)!.Value;

        workbook.CalculationMode = WorkbookCalculationMode.Manual;

        var result = service.CommitCellText(workbook, sheet.Id, c1, "=1+1");

        result.Success.Should().BeTrue();
        sheet.GetCell(c1)!.Value.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(2, "the freshly entered formula still computes once immediately");
        sheet.GetCell(d1)!.Value.Should().Be(d1Baseline,
            "an unrelated brand-new formula entry must not re-roll a pre-existing volatile cell in Manual mode");
        sheet.GetCell(e1)!.Value.Should().Be(e1Baseline,
            "the volatile cell's dependent must stay stale too, matching D1 staying stale");
    }

    [Fact]
    public void CommitCellText_ReturnsCommandFailureForProtectedSheet()
    {
        var (workbook, sheet, commandBus, service, _) = CreateEditService();
        sheet.IsProtected = true;
        var a1 = new CellAddress(sheet.Id, 1, 1);

        var result = service.CommitCellText(workbook, sheet.Id, a1, "blocked");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("The sheet is protected.");
        commandBus.CanUndo(workbook.Id).Should().BeFalse();
        sheet.GetCell(a1).Should().BeNull();
    }

    [Fact]
    public void CommitCellText_AllowsLockedCellInsideAllowedEditRangeOnProtectedSheet()
    {
        var (workbook, sheet, commandBus, service, _) = CreateEditService();
        var b2 = new CellAddress(sheet.Id, 2, 2);
        sheet.AllowEditRanges.Add(new GridRange(b2, b2));
        sheet.IsProtected = true;

        var result = service.CommitCellText(workbook, sheet.Id, b2, "allowed");

        result.Success.Should().BeTrue();
        commandBus.CanUndo(workbook.Id).Should().BeTrue();
        sheet.GetCell(b2)!.Value.Should().Be(new TextValue("allowed"));
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
