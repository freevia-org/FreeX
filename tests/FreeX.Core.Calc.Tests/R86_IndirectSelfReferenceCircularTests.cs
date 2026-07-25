using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// R86-calc-volatile-circular-5-2: a cell that self-references purely through INDIRECT's string
/// argument (e.g. A1=INDIRECT("A1")+1, or the common INDIRECT(ADDRESS(ROW(),COLUMN())) idiom) has
/// no STATIC precedent edge back to itself -- CollectReferences' FunctionCallNode case only walks
/// AST-level CellRefNode/RangeRefNode arguments, and INDIRECT's argument here is a StringNode, so
/// Tarjan's SCC pass over the dependency graph can never see the cycle. Since INDIRECT is also
/// unconditionally volatile, the cell was previously just re-evaluated every pass reading its own
/// last cached value with no #CIRCULAR! ever raised -- a silent divergence from Excel, which flags
/// this as a textbook circular reference (cell seeds to 0, non-iterative).
/// </summary>
public sealed class R86_IndirectSelfReferenceCircularTests
{
    private static RecalcEngine Engine() => new(new DependencyGraph(), new FormulaEvaluator());

    /// <summary>
    /// Bug case: A1=INDIRECT("A1")+1 must be flagged circular (seeded to 0, tracked in
    /// CyclicCells, "#CIRCULAR!" recorded) instead of silently computing a wrong/ever-incrementing
    /// value or hanging/crashing.
    /// </summary>
    [Fact]
    public void SelfReferencingIndirectString_IsFlaggedCircular_SeedsToZero()
    {
        var workbook = new Workbook("Test"); // IterativeCalculation defaults to false
        var sheet = workbook.AddSheet("Sheet1");
        var engine = Engine();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(a1, "INDIRECT(\"A1\")+1");

        var report = engine.RecalculateAllFormulas(workbook);

        report.CyclicCells.Should().Contain(a1, "A1 dynamically references its own cell through INDIRECT's string argument");
        engine.CyclicCells.Should().Contain(a1);
        sheet.GetValue(a1.Row, a1.Col).Should().Be(new NumberValue(0),
            "Excel seeds a non-iterative circular reference to 0, not a fabricated error or an ever-incrementing number");
        report.Errors.Should().Contain(e => e.Cell.Equals(a1) && e.Error == "#CIRCULAR!");

        // Re-running recalc must stay stable at 0 (not runaway-increment or crash) since INDIRECT
        // is volatile and re-evaluates every pass.
        var secondReport = engine.Recalculate(workbook, []);
        sheet.GetValue(a1.Row, a1.Col).Should().Be(new NumberValue(0));
        secondReport.CyclicCells.Should().Contain(a1);
    }

    /// <summary>
    /// Sibling: the common INDIRECT(ADDRESS(ROW(),COLUMN())) self-reference idiom must be caught
    /// the same way as the plain string-literal case above.
    /// </summary>
    [Fact]
    public void SelfReferencingIndirectAddressIdiom_IsFlaggedCircular()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var engine = Engine();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(a1, "INDIRECT(ADDRESS(ROW(),COLUMN()))");

        var report = engine.RecalculateAllFormulas(workbook);

        report.CyclicCells.Should().Contain(a1);
        sheet.GetValue(a1.Row, a1.Col).Should().Be(new NumberValue(0));
    }

    /// <summary>
    /// No-regression sibling: INDIRECT referencing a DIFFERENT cell (not a self-reference) must
    /// keep working normally -- not get swept up into circular-reference handling just because it
    /// is volatile and dynamic.
    /// </summary>
    [Fact]
    public void IndirectReferencingAnotherCell_StillEvaluatesNormally_NotFlaggedCircular()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var engine = Engine();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetFormula(a1, "5");
        sheet.SetFormula(b1, "INDIRECT(\"A1\")+1");

        var report = engine.RecalculateAllFormulas(workbook);

        report.CyclicCells.Should().BeEmpty("B1 dynamically references a different cell, not itself");
        engine.CyclicCells.Should().BeEmpty();
        sheet.GetValue(b1.Row, b1.Col).Should().Be(new NumberValue(6));
    }

    /// <summary>
    /// No-regression sibling: a plain (non-INDIRECT) direct self-loop A1=A1+1 must still be caught
    /// by the pre-existing static dependency-graph cycle detection, unaffected by the new INDIRECT
    /// runtime guard.
    /// </summary>
    [Fact]
    public void PlainDirectSelfLoop_StillDetectedCircular_ViaStaticGraph()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var engine = Engine();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(a1, "A1+1");

        var report = engine.RecalculateAllFormulas(workbook);

        report.CyclicCells.Should().Contain(a1);
        sheet.GetValue(a1.Row, a1.Col).Should().Be(new NumberValue(0));
    }
}
