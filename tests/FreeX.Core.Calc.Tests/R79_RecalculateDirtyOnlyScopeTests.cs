using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// R79-calc-volatile-recalc-5-1: plain F9's "Calculate Now" scope must recalculate only what is
/// actually dirty (volatile cells + whatever the existing dependency graph says depends on
/// explicitly changed cells) -- NOT rebuild the dependency graph and re-evaluate every formula
/// cell in the workbook, which is Ctrl+Alt+F9's ("Calculate Full") distinct, more expensive scope.
/// See MainWindow.WorkbookUiState.cs: RecalculateDirtyCells (F9) calls
/// RecalcEngine.Recalculate(workbook, []) while RecalculateWorkbook (Ctrl+Alt+F9) calls
/// RecalcEngine.RecalculateAllFormulas, which unconditionally rebuilds the graph and evaluates
/// every formula cell.
/// </summary>
public sealed class R79_RecalculateDirtyOnlyScopeTests
{
    private static RecalcEngine Engine() =>
        new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());

    [Fact]
    public void Recalculate_WithNoChangedCells_OnlyRecalculatesVolatileCells_NotOrdinaryFormulaCells()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var engine = Engine();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 2, 1);
        var c1 = new CellAddress(sheet.Id, 3, 1);

        // An ordinary formula cell with no volatile dependency -- once settled, plain F9 (dirty-
        // only) must leave it alone since nothing about it is actually dirty.
        sheet.SetCell(a1, new NumberValue(5));
        sheet.SetFormula(b1, "A1*2");

        // A volatile cell, unrelated to B1 -- Excel always re-evaluates volatile functions on
        // every calc pass, even F9's cheap scope.
        sheet.SetFormula(c1, "RAND()");

        // Seed the dependency graph and volatile-cell tracking, matching steady state after
        // Automatic-mode edits have already settled everything.
        engine.RecalculateAllFormulas(workbook);

        // Plain F9's scope: recalculate with no explicitly changed cells.
        var report = engine.Recalculate(workbook, []);

        report.RecalculatedCells.Should().Contain(c1, "volatile cells must still recalculate on plain F9");
        report.RecalculatedCells.Should().NotContain(b1,
            "an ordinary formula cell with nothing dirty must NOT be re-evaluated by plain F9's cheap scope");
    }

    [Fact]
    public void RecalculateAllFormulas_AlwaysReevaluatesEveryFormulaCell_RegardlessOfDirtyState()
    {
        // No-regression sibling: Ctrl+Alt+F9's ("Calculate Full") scope is unchanged -- it must
        // still force-evaluate every formula cell in the workbook even when nothing is dirty.
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var engine = Engine();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 2, 1);

        sheet.SetCell(a1, new NumberValue(5));
        sheet.SetFormula(b1, "A1*2");

        engine.RecalculateAllFormulas(workbook);

        // Nothing has changed since the previous full recalc, yet Calculate Full must still
        // report every formula cell as recalculated.
        var report = engine.RecalculateAllFormulas(workbook);

        report.RecalculatedCells.Should().Contain(b1,
            "Ctrl+Alt+F9's Calculate Full scope must re-evaluate every formula cell regardless of dirty state");
    }
}
