using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Integration.Tests;

/// <summary>
/// R99-core-commands-2: NamedDefinitionRecalcHelper.FindCellsReferencingName (used by
/// DefineNamedRangeCommand/DefineNamedFormulaCommand/RemoveNamedRangeCommand/RemoveNamedFormulaCommand
/// to compute AffectedCells on a name redefine/delete) only checked whether a candidate formula
/// cell's OWN parsed AST directly contained a NamedRangeNode for the redefined name. It never
/// resolved a referenced name that was itself a named FORMULA and recursed into THAT formula's text
/// to see if it (transitively) referenced the redefined name. So if named formula "DoubleRate" is
/// defined as "=Rate*2" and a cell contains "=DoubleRate", redefining "Rate" never included that
/// cell in AffectedCells even though real Excel's Name Manager recalculates it immediately in
/// Automatic mode -- the cell kept showing its stale pre-redefine value until a full F9. Fixed by
/// having NamedDefinitionRecalcHelper recurse into a referenced name's own formula text (mirroring
/// RecalcEngine.CollectReferences' namedFormulaStack expansion), cycle-guarded by visited name.
/// </summary>
public sealed class R99_NamedDefinitionTransitiveRecalcTests
{
    [Fact]
    public void RedefiningNamedFormula_RecalculatesCellThatReferencesItOnlyThroughNestedNamedFormula()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);
        var ctx = new TestCommandContext(workbook);

        // Rate = 1.05, DoubleRate = Rate*2 (a named formula referencing another named formula),
        // and B1 references DoubleRate only -- never "Rate" directly.
        workbook.NamedFormulas["Rate"] = "1.05";
        workbook.NamedFormulas["DoubleRate"] = "Rate*2";

        var formulaAddress = new CellAddress(sheet.Id, 1, 2); // B1
        sheet.SetFormula(formulaAddress, "DoubleRate");
        engine.RegisterFormulaDependencies(
            formulaAddress, FormulaEvaluator.ParseFormula("DoubleRate"), sheet.Id, workbook);
        engine.Recalculate(workbook, [formulaAddress]);

        sheet.GetCell(formulaAddress)!.Value.Should().Be(new NumberValue(2.1));

        // Redefine Rate via the real command entry point (Name Manager "Refers To" edit path).
        var command = new DefineNamedFormulaCommand("Rate", "2");
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.AffectedCells.Should().Contain(formulaAddress,
            "B1 references DoubleRate, which itself references the redefined Rate -- Excel's Name " +
            "Manager recalculates transitive dependents through any depth of named formulas");
        command.AffectedCells.Should().Contain(formulaAddress);

        engine.Recalculate(workbook, outcome.AffectedCells!);

        sheet.GetCell(formulaAddress)!.Value.Should().Be(new NumberValue(4),
            "B1 must reflect the redefined Rate (2*2=4), not the stale 2.1 from the old rate of 1.05");
    }

    [Fact]
    public void RedefiningNamedRange_RecalculatesCellThatReferencesItOnlyThroughNestedNamedFormula()
    {
        // Sibling path: the redefined name is a plain RANGE (not a formula), reached transitively
        // through a named FORMULA that wraps it in SUM(...).
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
        workbook.NamedFormulas["MyTotal"] = "SUM(MyRange)";

        var formulaAddress = new CellAddress(sheet.Id, 1, 2); // B1
        sheet.SetFormula(formulaAddress, "MyTotal");
        engine.RegisterFormulaDependencies(
            formulaAddress, FormulaEvaluator.ParseFormula("MyTotal"), sheet.Id, workbook);
        engine.Recalculate(workbook, [formulaAddress]);

        sheet.GetCell(formulaAddress)!.Value.Should().Be(new NumberValue(15));

        var newRange = new GridRange(new CellAddress(sheet.Id, 1, 3), new CellAddress(sheet.Id, 5, 3));
        var command = new DefineNamedRangeCommand("MyRange", newRange);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.AffectedCells.Should().Contain(formulaAddress,
            "B1 references MyTotal, which itself references the redefined MyRange");

        engine.Recalculate(workbook, outcome.AffectedCells!);

        sheet.GetCell(formulaAddress)!.Value.Should().Be(new NumberValue(500),
            "B1 must reflect the redefined range (C1:C5 = 100 each), not the stale 15 from A1:A5");
    }

    [Fact]
    public void RedefiningNamedFormula_DoesNotAffectUnrelatedNestedChain_AndSelfReferencingFormulaDoesNotHang()
    {
        // No-regression: a nested named-formula chain that never reaches the redefined name must
        // not be reported, and a (malformed) self-referencing named formula must be handled by the
        // cycle guard rather than recursing forever.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);

        workbook.NamedFormulas["Rate"] = "1.05";
        workbook.NamedFormulas["Unrelated"] = "42";
        workbook.NamedFormulas["WrapsUnrelated"] = "Unrelated*2";
        // Deliberately malformed: a named formula that references itself. Must not hang or crash.
        workbook.NamedFormulas["SelfRef"] = "SelfRef+1";

        var unrelatedCell = new CellAddress(sheet.Id, 1, 2); // B1
        sheet.SetFormula(unrelatedCell, "WrapsUnrelated");
        var selfRefCell = new CellAddress(sheet.Id, 2, 2); // B2
        sheet.SetFormula(selfRefCell, "SelfRef");

        var command = new DefineNamedFormulaCommand("Rate", "2");
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue();
        outcome.AffectedCells.Should().NotContain(unrelatedCell,
            "WrapsUnrelated never reaches Rate through any depth of nesting");
        outcome.AffectedCells.Should().NotContain(selfRefCell,
            "SelfRef never reaches Rate either; this also proves the cycle guard prevented a hang");
    }
}
