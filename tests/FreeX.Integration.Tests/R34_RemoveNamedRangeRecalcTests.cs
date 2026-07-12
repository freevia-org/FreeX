using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Integration.Tests;

/// <summary>
/// R34-commands-name-manager-deep-1: deleting a defined name via RemoveNamedRangeCommand reported
/// NO AffectedCells and the class did not implement IAffectedCellsCommand, so a formula referencing
/// the just-deleted name (e.g. B1=Rate*2) kept showing its stale pre-delete value instead of
/// recalculating to #NAME?, unlike real Excel's Name Manager Delete which recalculates dependents
/// immediately. RemoveNamedRangeCommand now reports every formula cell whose parsed AST references
/// the removed name as AffectedCells (mirroring DefineNamedRangeCommand's redefine case), for both
/// the range-deletion and the named-formula-deletion fallback path.
/// </summary>
public sealed class R34_RemoveNamedRangeRecalcTests
{
    [Fact]
    public void RemovingNamedRange_ReportsAffectedCells_AndReferencingFormulaBecomesNameError()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);
        var ctx = new TestCommandContext(workbook);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(2)); // A1 = 2

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1));
        workbook.DefineNamedRange("Rate", range);

        var formulaAddress = new CellAddress(sheet.Id, 1, 2); // B1
        sheet.SetFormula(formulaAddress, "Rate*2");
        engine.RegisterFormulaDependencies(
            formulaAddress, FormulaEvaluator.ParseFormula("Rate*2"), sheet.Id, workbook);
        engine.Recalculate(workbook, [formulaAddress]);

        sheet.GetCell(formulaAddress)!.Value.Should().Be(new NumberValue(4));

        var command = new RemoveNamedRangeCommand("Rate");
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.AffectedCells.Should().Contain(formulaAddress,
            "deleting Rate must report B1 (which references it) as affected so it recalculates");
        command.AffectedCells.Should().Contain(formulaAddress,
            "RemoveNamedRangeCommand must implement IAffectedCellsCommand so Undo also recalculates dependents");

        // Simulate the standard post-command pipeline (WorkbookCellEditService.RecalculateIfAutomatic).
        engine.Recalculate(workbook, outcome.AffectedCells!);

        sheet.GetCell(formulaAddress)!.Value.Should().Be(ErrorValue.Name,
            "B1 must become #NAME? once Rate no longer exists, not keep showing the stale 4");
    }

    [Fact]
    public void RemovingNamedFormula_ReportsAffectedCells_AndReferencingFormulaBecomesNameError()
    {
        // Sibling path: deleting a name that resolves to a named FORMULA (not a range) must be
        // reported the same way as the range-deletion path above.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);
        var ctx = new TestCommandContext(workbook);

        workbook.NamedFormulas["Rate"] = "1.05";

        var formulaAddress = new CellAddress(sheet.Id, 1, 2); // B1
        sheet.SetFormula(formulaAddress, "Rate*100");
        engine.RegisterFormulaDependencies(
            formulaAddress, FormulaEvaluator.ParseFormula("Rate*100"), sheet.Id, workbook);
        engine.Recalculate(workbook, [formulaAddress]);

        sheet.GetCell(formulaAddress)!.Value.Should().Be(new NumberValue(105));

        var command = new RemoveNamedRangeCommand("Rate");
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.AffectedCells.Should().Contain(formulaAddress,
            "deleting the named formula Rate must report B1 as affected so it recalculates");
        command.AffectedCells.Should().Contain(formulaAddress);

        engine.Recalculate(workbook, outcome.AffectedCells!);

        sheet.GetCell(formulaAddress)!.Value.Should().Be(ErrorValue.Name,
            "B1 must become #NAME? once the named formula Rate no longer exists");
    }

    [Fact]
    public void RemovingNamedRange_WithNoReferencingFormulas_ReportsNoAffectedCells()
    {
        // Already-working sibling case this fix must not disturb: deleting a name that nothing
        // references must not spuriously report any cells as affected.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1));
        workbook.DefineNamedRange("Unused", range);

        var command = new RemoveNamedRangeCommand("Unused");
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.AffectedCells.Should().BeEmpty();
        command.AffectedCells.Should().BeEmpty();
    }
}
