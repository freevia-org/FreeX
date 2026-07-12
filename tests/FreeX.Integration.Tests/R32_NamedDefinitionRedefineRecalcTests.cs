using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Integration.Tests;

/// <summary>
/// R32-calc-dependency-volatile-deep-2: redefining an existing named range/named formula via
/// DefineNamedRangeCommand/DefineNamedFormulaCommand reported NO AffectedCells and neither class
/// implemented IAffectedCellsCommand, so a formula referencing that name (e.g. B1=SUM(MyRange))
/// never recalculated when the name was repointed to a different range/value -- it kept showing
/// its stale pre-redefine result until a full F9, unlike real Excel's Name Manager which
/// recalculates dependents immediately. Both commands now report every formula cell whose parsed
/// AST references the redefined name as AffectedCells, but only on a genuine REDEFINE -- never on
/// first creation, since nothing could reference a name that did not exist yet.
/// </summary>
public sealed class R32_NamedDefinitionRedefineRecalcTests
{
    [Fact]
    public void RedefiningNamedRange_ReportsAndRecalculatesReferencingFormula()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);
        var ctx = new TestCommandContext(workbook);

        for (uint row = 1; row <= 5; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row)); // A1:A5 = 1..5
        for (uint row = 1; row <= 5; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(100)); // C1:C5 = 100 each

        var oldRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1));
        workbook.DefineNamedRange("MyRange", oldRange);

        var formulaAddress = new CellAddress(sheet.Id, 1, 2); // B1
        sheet.SetFormula(formulaAddress, "SUM(MyRange)");
        engine.RegisterFormulaDependencies(
            formulaAddress, FormulaEvaluator.ParseFormula("SUM(MyRange)"), sheet.Id, workbook);
        engine.Recalculate(workbook, [formulaAddress]);

        sheet.GetCell(formulaAddress)!.Value.Should().Be(new NumberValue(15));

        // Redefine MyRange from A1:A5 to C1:C5 -- mirrors editing "Refers To" in the Name Manager.
        var newRange = new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 5, 3));
        var command = new DefineNamedRangeCommand("MyRange", newRange);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.AffectedCells.Should().Contain(formulaAddress,
            "redefining MyRange must report B1 (which references it) as affected so it recalculates");
        command.AffectedCells.Should().Contain(formulaAddress,
            "DefineNamedRangeCommand must implement IAffectedCellsCommand so Undo also recalculates dependents");

        // Simulate the standard post-command pipeline (WorkbookCellEditService.RecalculateIfAutomatic).
        engine.Recalculate(workbook, outcome.AffectedCells!);

        sheet.GetCell(formulaAddress)!.Value.Should().Be(new NumberValue(500),
            "B1 must reflect the redefined range (C1:C5 = 100 each), not the stale 15 from the old A1:A5");
    }

    [Fact]
    public void RedefiningNamedFormula_ReportsAndRecalculatesReferencingFormula()
    {
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

        var command = new DefineNamedFormulaCommand("Rate", "2");
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.AffectedCells.Should().Contain(formulaAddress,
            "redefining Rate must report B1 (which references it) as affected so it recalculates");
        command.AffectedCells.Should().Contain(formulaAddress,
            "DefineNamedFormulaCommand must implement IAffectedCellsCommand so Undo also recalculates dependents");

        engine.Recalculate(workbook, outcome.AffectedCells!);

        sheet.GetCell(formulaAddress)!.Value.Should().Be(new NumberValue(200),
            "B1 must reflect the redefined Rate (2*100), not the stale 105 from the old rate of 1.05");
    }

    [Fact]
    public void DefiningBrandNewNamedRange_ReportsNoAffectedCells()
    {
        // Sibling already-working case this fix must not disturb: creating a name for the FIRST
        // time can never have a pre-existing referencing formula, so AffectedCells stays empty --
        // exactly like before the fix -- rather than needlessly scanning/reporting anything.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        // An unrelated formula that happens to mention the not-yet-defined name text; since the
        // name doesn't exist yet at Apply time, this must not be reported as affected.
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 2), "SUM(MyRange)");

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1));
        var command = new DefineNamedRangeCommand("MyRange", range);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.AffectedCells.Should().BeEmpty();
        command.AffectedCells.Should().BeEmpty();
    }
}
