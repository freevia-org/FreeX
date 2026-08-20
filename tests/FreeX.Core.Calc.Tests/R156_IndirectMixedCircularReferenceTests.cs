using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// freex-recalc-order F1: a circular reference formed by one static reference plus one INDIRECT()
/// hop -- A1=B1+1 (a plain static reference to B1), B1=INDIRECT("A1") (a literal-string INDIRECT
/// hop back to A1) -- has no static edge for B1's half of the cycle, so
/// DependencyGraph.GetRecalcOrder sees an ordinary acyclic chain (B1 has zero precedents, A1
/// depends on B1) instead of a cycle. Because INDIRECT makes B1 volatile, B1 is swept into every
/// recalculation and always reads whatever A1 held at the end of the previous pass, which A1 then
/// increments and stores -- the pair drifts by +1 forever instead of Excel's actual behaviour
/// (flag circular, freeze both at 0). This mirrors R86_IndirectSelfReferenceCircularTests, but for
/// a cycle across TWO cells instead of a single cell's direct self-reference.
/// </summary>
public sealed class R156_IndirectMixedCircularReferenceTests
{
    private static RecalcEngine Engine() => new(new DependencyGraph(), new FormulaEvaluator());

    /// <summary>
    /// Bug case: A1=B1+1, B1=INDIRECT("A1") must be flagged circular (both seeded to 0, both
    /// tracked in CyclicCells, "#CIRCULAR!" recorded for A1) instead of silently drifting by +1
    /// on every subsequent recalculation.
    /// </summary>
    [Fact]
    public void StaticReferencePlusIndirectHop_IsFlaggedCircular_SeedsToZero()
    {
        var workbook = new Workbook("Test"); // IterativeCalculation defaults to false
        var sheet = workbook.AddSheet("Sheet1");
        var engine = Engine();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetFormula(a1, "B1+1");
        sheet.SetFormula(b1, "INDIRECT(\"A1\")");

        var report = engine.RecalculateAllFormulas(workbook);

        report.CyclicCells.Should().Contain(a1);
        report.CyclicCells.Should().Contain(b1);
        engine.CyclicCells.Should().Contain(a1);
        engine.CyclicCells.Should().Contain(b1);
        sheet.GetValue(a1.Row, a1.Col).Should().Be(new NumberValue(0));
        sheet.GetValue(b1.Row, b1.Col).Should().Be(new NumberValue(0));
        report.Errors.Should().Contain(e => e.Cell.Equals(a1) && e.Error == "#CIRCULAR!");
    }

    /// <summary>
    /// The exact user gesture from the finding: after the pair is (correctly) frozen circular,
    /// repeatedly recalculating (mirroring several unrelated edits elsewhere in the workbook, or
    /// repeated F9) must NOT let the pair drift upward -- it must stay pinned at 0 every pass,
    /// unlike the pre-fix behaviour where it grew by exactly 1 on every single recalculation.
    /// </summary>
    [Fact]
    public void StaticReferencePlusIndirectHop_StaysPinnedAtZero_AcrossRepeatedRecalculation()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var engine = Engine();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3); // an unrelated cell, edited each pass
        sheet.SetFormula(a1, "B1+1");
        sheet.SetFormula(b1, "INDIRECT(\"A1\")");
        sheet.SetCell(c1, new NumberValue(0));

        engine.RecalculateAllFormulas(workbook);

        for (var i = 1; i <= 15; i++)
        {
            sheet.SetCell(c1, new NumberValue(i));
            var report = engine.Recalculate(workbook, [c1]);
            report.CyclicCells.Should().Contain(a1);
            report.CyclicCells.Should().Contain(b1);
        }

        sheet.GetValue(a1.Row, a1.Col).Should().Be(new NumberValue(0),
            "a genuine circular reference must stay frozen at 0, not drift upward by 1 on every unrelated edit");
        sheet.GetValue(b1.Row, b1.Col).Should().Be(new NumberValue(0));
    }

    /// <summary>
    /// No-regression sibling: a direct single-cell self-reference through INDIRECT (A1=INDIRECT("A1")+1)
    /// must still be handled exactly as R86/R124 already prove -- non-iteratively circular+seeded to
    /// 0, and (separately, per R124) convergent under Iterative Calculation. This confirms the new
    /// static edge for a DIFFERENT-cell literal INDIRECT target does not disturb the existing
    /// same-cell sentinel path, which deliberately skips registering a self-edge.
    /// </summary>
    [Fact]
    public void DirectIndirectSelfReference_StillHandledByExistingSentinelPath_NotByNewEdge()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var engine = Engine();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(a1, "INDIRECT(\"A1\")+1");

        var report = engine.RecalculateAllFormulas(workbook);

        report.CyclicCells.Should().Contain(a1);
        sheet.GetValue(a1.Row, a1.Col).Should().Be(new NumberValue(0));
    }

    /// <summary>
    /// No-regression sibling: INDIRECT referencing a different, non-cyclic cell must keep
    /// evaluating normally and must NOT be flagged circular just because it now registers a real
    /// dependency edge -- mirrors R86's IndirectReferencingAnotherCell_StillEvaluatesNormally_NotFlaggedCircular.
    /// </summary>
    [Fact]
    public void IndirectReferencingUnrelatedCell_StillEvaluatesNormally_NotFlaggedCircular()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var engine = Engine();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetFormula(a1, "5");
        sheet.SetFormula(b1, "INDIRECT(\"A1\")+1");

        var report = engine.RecalculateAllFormulas(workbook);

        report.CyclicCells.Should().BeEmpty();
        sheet.GetValue(b1.Row, b1.Col).Should().Be(new NumberValue(6));
    }

    /// <summary>
    /// No-regression sibling: an ordinary two-cell cycle with NO INDIRECT at all (A1=B1+1,
    /// B1=A1+1) must still be caught by the pre-existing static dependency-graph cycle detection,
    /// unaffected by adding the new INDIRECT-literal edge case alongside it.
    /// </summary>
    [Fact]
    public void PlainTwoCellCycle_StillDetectedCircular_ViaExistingStaticGraph()
    {
        var workbook = new Workbook("Test");
        var sheet = workbook.AddSheet("Sheet1");
        var engine = Engine();

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetFormula(a1, "B1+1");
        sheet.SetFormula(b1, "A1+1");

        var report = engine.RecalculateAllFormulas(workbook);

        report.CyclicCells.Should().Contain(a1);
        report.CyclicCells.Should().Contain(b1);
        sheet.GetValue(a1.Row, a1.Col).Should().Be(new NumberValue(0));
        sheet.GetValue(b1.Row, b1.Col).Should().Be(new NumberValue(0));
    }
}
