using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Integration.Tests;

/// <summary>
/// R69-calc-dependency-insert-6-1: DeleteRowsCommand / InsertColumnsCommand / DeleteColumnsCommand
/// build CommandOutcome.AffectedCells as ONLY the cells whose FormulaText was actually rewritten
/// (Enumerable.Empty + _formulaSnapshot). A formula that RELOCATES (its Cell object physically moves
/// during the shift) but needs NO textual rewrite (its references are unaffected by the shift, e.g.
/// they point at a row/column outside the shifted band) was never added to AffectedCells, so the
/// dependency graph never re-registered it at its new address -- orphaning it: an edit to its
/// precedent did not recalc it, leaving it stuck at its stale cached value. InsertRowsCommand already
/// fixed this via RelocatedFormulaCellsPendingDependencyRefresh (see
/// R24_InsertRowsVolatileRelocationTests); this mirrors that fix (and its test shape) onto the other
/// three row/column shift commands.
/// </summary>
public sealed class R69_DeleteInsertColumnsRowsRelocationTests
{
    [Fact]
    public void DeleteRows_RelocatedUnaffectedFormula_IsIncludedInAffectedCellsAndStaysLive()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);
        var ctx = new TestCommandContext(workbook);

        var precedent = new CellAddress(sheet.Id, 1, 1); // A1
        sheet.SetCell(precedent, Cell.FromValue(new NumberValue(10)));

        var dependentOriginal = new CellAddress(sheet.Id, 20, 1); // A20 = "=A1"
        sheet.SetFormula(dependentOriginal, "=A1");
        engine.RegisterFormulaDependencies(dependentOriginal, FormulaEvaluator.ParseFormula("=A1"), sheet.Id, workbook);
        engine.Recalculate(workbook, [dependentOriginal]);
        ((NumberValue)sheet.GetCell(dependentOriginal)!.Value).Value.Should().Be(10);

        // Deleting rows 5:6 relocates A20 up to A18. Its reference (A1) sits above the deleted band,
        // so the formula text "=A1" needs no rewrite -- exactly the case RewriteAllFormulas misses.
        var command = new DeleteRowsCommand(sheet.Id, startRow: 5, count: 2);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue();

        var relocated = new CellAddress(sheet.Id, 18, 1); // A18
        sheet.GetCell(relocated)!.FormulaText.Should().Be("=A1");

        outcome.AffectedCells.Should().Contain(relocated,
            "a relocated formula whose text needed no rewrite must still be reported as affected " +
            "so the dependency graph re-registers it at its new address");

        // Simulate the standard post-command pipeline that drives RecalcEngine off AffectedCells.
        foreach (var affected in outcome.AffectedCells!)
        {
            var cell = sheet.GetCell(affected);
            if (cell?.FormulaText is null)
            {
                engine.ClearFormulaDependencies(affected);
                continue;
            }

            engine.RegisterFormulaDependencies(
                affected, FormulaEvaluator.ParseFormula(cell.FormulaText), affected.Sheet, workbook);
        }

        // Editing the precedent must now recalc the relocated cell instead of leaving it stale.
        sheet.SetCell(precedent, Cell.FromValue(new NumberValue(20)));
        var report = engine.Recalculate(workbook, [precedent]);
        report.RecalculatedCells.Should().Contain(relocated,
            "editing A1 must recalc the relocated A18 formula, not leave it stuck at its stale value");
        ((NumberValue)sheet.GetCell(relocated)!.Value).Value.Should().Be(20);
    }

    [Fact]
    public void DeleteRows_RelocatedFormulaThatIsRewritten_StillWorks()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        // Both the precedent (A25) and the dependent (A30) sit below the deleted band [5,6], so they
        // shift up in lockstep and the dependent's formula text IS rewritten (A25 -> A23).
        sheet.SetCell(new CellAddress(sheet.Id, 25, 1), Cell.FromValue(new NumberValue(7)));
        sheet.SetFormula(new CellAddress(sheet.Id, 30, 1), "=A25");

        var command = new DeleteRowsCommand(sheet.Id, startRow: 5, count: 2);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue();

        var relocatedDependent = new CellAddress(sheet.Id, 28, 1); // A30 -> A28
        sheet.GetCell(relocatedDependent)!.FormulaText.Should().Be("A23");
        outcome.AffectedCells.Should().Contain(relocatedDependent,
            "a formula whose text was rewritten by the shift must still be reported as affected");
    }

    [Fact]
    public void InsertColumns_RelocatedUnaffectedFormula_IsIncludedInAffectedCellsAndStaysLive()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);
        var ctx = new TestCommandContext(workbook);

        var precedent = new CellAddress(sheet.Id, 1, 1); // A1
        sheet.SetCell(precedent, Cell.FromValue(new NumberValue(10)));

        var dependentOriginal = new CellAddress(sheet.Id, 1, 20); // T1 = "=A1"
        sheet.SetFormula(dependentOriginal, "=A1");
        engine.RegisterFormulaDependencies(dependentOriginal, FormulaEvaluator.ParseFormula("=A1"), sheet.Id, workbook);
        engine.Recalculate(workbook, [dependentOriginal]);
        ((NumberValue)sheet.GetCell(dependentOriginal)!.Value).Value.Should().Be(10);

        // Inserting 3 columns before column 5 relocates T1 to column 23. Its reference (A1, column 1)
        // sits before the insert point, so the formula text "=A1" needs no rewrite.
        var command = new InsertColumnsCommand(sheet.Id, beforeCol: 5, count: 3);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue();

        var relocated = new CellAddress(sheet.Id, 1, 23);
        sheet.GetCell(relocated)!.FormulaText.Should().Be("=A1");

        outcome.AffectedCells.Should().Contain(relocated,
            "a relocated formula whose text needed no rewrite must still be reported as affected " +
            "so the dependency graph re-registers it at its new address");

        foreach (var affected in outcome.AffectedCells!)
        {
            var cell = sheet.GetCell(affected);
            if (cell?.FormulaText is null)
            {
                engine.ClearFormulaDependencies(affected);
                continue;
            }

            engine.RegisterFormulaDependencies(
                affected, FormulaEvaluator.ParseFormula(cell.FormulaText), affected.Sheet, workbook);
        }

        sheet.SetCell(precedent, Cell.FromValue(new NumberValue(20)));
        var report = engine.Recalculate(workbook, [precedent]);
        report.RecalculatedCells.Should().Contain(relocated,
            "editing A1 must recalc the relocated formula, not leave it stuck at its stale value");
        ((NumberValue)sheet.GetCell(relocated)!.Value).Value.Should().Be(20);
    }

    [Fact]
    public void InsertColumns_RelocatedFormulaThatIsRewritten_StillWorks()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        // Both the precedent (column 25) and the dependent (column 30) sit at/after the insert point
        // (column 5), so they shift right in lockstep and the dependent's formula text IS rewritten.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 25), Cell.FromValue(new NumberValue(7)));
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 30), "=Y1"); // column 25 = Y

        var command = new InsertColumnsCommand(sheet.Id, beforeCol: 5, count: 3);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue();

        var relocatedDependent = new CellAddress(sheet.Id, 1, 33); // col 30 -> col 33
        sheet.GetCell(relocatedDependent)!.FormulaText.Should().Be("AB1"); // col 25 -> col 28 = AB
        outcome.AffectedCells.Should().Contain(relocatedDependent,
            "a formula whose text was rewritten by the shift must still be reported as affected");
    }

    [Fact]
    public void DeleteColumns_RelocatedUnaffectedFormula_IsIncludedInAffectedCellsAndStaysLive()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);
        var ctx = new TestCommandContext(workbook);

        var precedent = new CellAddress(sheet.Id, 1, 1); // A1
        sheet.SetCell(precedent, Cell.FromValue(new NumberValue(10)));

        var dependentOriginal = new CellAddress(sheet.Id, 1, 20); // T1 = "=A1"
        sheet.SetFormula(dependentOriginal, "=A1");
        engine.RegisterFormulaDependencies(dependentOriginal, FormulaEvaluator.ParseFormula("=A1"), sheet.Id, workbook);
        engine.Recalculate(workbook, [dependentOriginal]);
        ((NumberValue)sheet.GetCell(dependentOriginal)!.Value).Value.Should().Be(10);

        // Deleting columns 5:6 relocates T1 left to column 18. Its reference (A1, column 1) sits
        // before the deleted band, so the formula text "=A1" needs no rewrite.
        var command = new DeleteColumnsCommand(sheet.Id, startCol: 5, count: 2);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue();

        var relocated = new CellAddress(sheet.Id, 1, 18);
        sheet.GetCell(relocated)!.FormulaText.Should().Be("=A1");

        outcome.AffectedCells.Should().Contain(relocated,
            "a relocated formula whose text needed no rewrite must still be reported as affected " +
            "so the dependency graph re-registers it at its new address");

        foreach (var affected in outcome.AffectedCells!)
        {
            var cell = sheet.GetCell(affected);
            if (cell?.FormulaText is null)
            {
                engine.ClearFormulaDependencies(affected);
                continue;
            }

            engine.RegisterFormulaDependencies(
                affected, FormulaEvaluator.ParseFormula(cell.FormulaText), affected.Sheet, workbook);
        }

        sheet.SetCell(precedent, Cell.FromValue(new NumberValue(20)));
        var report = engine.Recalculate(workbook, [precedent]);
        report.RecalculatedCells.Should().Contain(relocated,
            "editing A1 must recalc the relocated formula, not leave it stuck at its stale value");
        ((NumberValue)sheet.GetCell(relocated)!.Value).Value.Should().Be(20);
    }

    [Fact]
    public void DeleteColumns_RelocatedFormulaThatIsRewritten_StillWorks()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        // Both the precedent (column 25) and the dependent (column 30) sit after the deleted band
        // [5,6], so they shift left in lockstep and the dependent's formula text IS rewritten.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 25), Cell.FromValue(new NumberValue(7)));
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 30), "=Y1"); // column 25 = Y

        var command = new DeleteColumnsCommand(sheet.Id, startCol: 5, count: 2);
        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue();

        var relocatedDependent = new CellAddress(sheet.Id, 1, 28); // col 30 -> col 28
        sheet.GetCell(relocatedDependent)!.FormulaText.Should().Be("W1"); // col 25 -> col 23 = W
        outcome.AffectedCells.Should().Contain(relocatedDependent,
            "a formula whose text was rewritten by the shift must still be reported as affected");
    }
}
