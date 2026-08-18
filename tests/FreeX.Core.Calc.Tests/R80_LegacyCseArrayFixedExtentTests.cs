using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression coverage for R80-formula-array-cse-5-2: a legacy multi-cell CSE array formula
/// (ECMA-376 <c>&lt;f t="array" ref="..."/&gt;</c>, Ctrl+Shift+Enter) must stay confined to its
/// originally declared ref-range extent on every recalc, never negotiating with neighboring cells
/// like a modern dynamic-array formula does. Before the fix, RecalcEngine routed every
/// <c>Cell.ArrayMode == Dynamic</c> formula (the only mode a loaded array-formula anchor ever got)
/// through the free-spilling path: the natural result size determined how many cells were written,
/// silently overwriting a cell just past the declared range if it was blank, or raising a
/// never-produced-by-Excel #SPILL! if it wasn't.
/// </summary>
public sealed class R80LegacyCseArrayFixedExtentTests
{
    private static (RecalcEngine engine, Workbook wb) MakeEngine()
    {
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);
        var wb = new Workbook();
        wb.AddSheet("Sheet1");
        return (engine, wb);
    }

    [Fact]
    public void LegacyCseArray_DeclaredRangeSmallerThanNaturalResult_ConfinesAndNeverTouchesOutsideCell()
    {
        var (engine, wb) = MakeEngine();
        var sheet = wb.Sheets.First();

        // A1:A3 = 1,2,3 (a 3-row x 1-col column).
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(3));

        // H1:I1 (row 1, cols 8-9) was CSE-entered as {=TRANSPOSE(A1:A3)}: a 1-row x 2-col
        // selection over a formula whose natural result is 1x3. Excel fills only H1/I1 and
        // silently drops the third transposed value; J1 (col 10) is never touched.
        var h1 = new CellAddress(sheet.Id, 1, 8);
        var j1 = new CellAddress(sheet.Id, 1, 10);
        var legacyCell = Cell.FromFormula("TRANSPOSE(A1:A3)");
        legacyCell.LegacyArrayRows = 1;
        legacyCell.LegacyArrayCols = 2;
        sheet.SetCell(h1, legacyCell);
        engine.RebuildFormulaDependencies(wb);

        engine.Recalculate(wb, [h1]);

        sheet.GetValue(1, 8).Should().Be(new NumberValue(1), "H1 gets the first transposed value");
        sheet.GetValue(1, 9).Should().Be(new NumberValue(2), "I1 gets the second transposed value");
        sheet.GetValue(1, 10).Should().Be(BlankValue.Instance,
            "J1 sits outside the originally declared H1:I1 ref range and Excel's legacy CSE " +
            "semantics never grow into it, unlike a modern dynamic-array spill");
        sheet.GetCell(j1).Should().BeNull("J1 must not gain a spill-value/cell entry at all");
    }

    [Fact]
    public void LegacyCseArray_DeclaredRangeMatchesNaturalResult_FillsEveryDeclaredCell()
    {
        var (engine, wb) = MakeEngine();
        var sheet = wb.Sheets.First();

        // A1:B2 = a genuine 2x2 block of values.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(2));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(3));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(4));

        // D1:E2 was CSE-entered as {=A1:B2}: a 2-row x 2-col selection whose natural result is
        // exactly 2x2, so every declared cell gets its corresponding value with no truncation.
        var d1 = new CellAddress(sheet.Id, 1, 4);
        var legacyCell = Cell.FromFormula("A1:B2");
        legacyCell.LegacyArrayRows = 2;
        legacyCell.LegacyArrayCols = 2;
        sheet.SetCell(d1, legacyCell);
        engine.RebuildFormulaDependencies(wb);

        engine.Recalculate(wb, [d1]);

        sheet.GetValue(1, 4).Should().Be(new NumberValue(1), "D1");
        sheet.GetValue(1, 5).Should().Be(new NumberValue(2), "E1");
        sheet.GetValue(2, 4).Should().Be(new NumberValue(3), "D2");
        sheet.GetValue(2, 5).Should().Be(new NumberValue(4), "E2");
    }

    /// <summary>
    /// R141 cse-scalar-result-not-replicated: a legacy CSE array whose formula naturally returns a
    /// plain SCALAR (e.g. {=SUM(A1:A5)} entered over a 2x1 block) must have that scalar replicated
    /// into every declared cell, exactly like real Excel does, instead of leaving the non-anchor
    /// cells blank. This is the reproduction of the bug: before the fix, only H1 got the sum
    /// and H2 stayed BlankValue because the scalar branch of RecalcEngine never consulted
    /// LegacyArrayRows/Cols.
    /// </summary>
    [Fact]
    public void LegacyCseArray_ScalarResult_ReplicatesAcrossDeclaredBlock()
    {
        var (engine, wb) = MakeEngine();
        var sheet = wb.Sheets.First();

        // A1:A5 = 1..5, sum = 15.
        for (uint row = 1; row <= 5; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

        // H1:H2 (2 rows x 1 col) was CSE-entered as {=SUM(A1:A5)}: a formula whose natural
        // result is a plain scalar. Excel fills BOTH H1 and H2 with the same sum.
        var h1 = new CellAddress(sheet.Id, 1, 8);
        var h2 = new CellAddress(sheet.Id, 2, 8);
        var legacyCell = Cell.FromFormula("SUM(A1:A5)");
        legacyCell.LegacyArrayRows = 2;
        legacyCell.LegacyArrayCols = 1;
        sheet.SetCell(h1, legacyCell);
        engine.RebuildFormulaDependencies(wb);

        engine.Recalculate(wb, [h1]);

        sheet.GetValue(1, 8).Should().Be(new NumberValue(15), "H1 (anchor) gets the SUM result");
        sheet.GetValue(2, 8).Should().Be(new NumberValue(15),
            "H2 must be replicated with the SAME scalar result, matching real Excel's CSE " +
            "fill-the-whole-selection behavior instead of staying blank");
    }

    /// <summary>
    /// R141 cse-scalar-result-not-replicated (guard survival half): after a scalar-returning CSE
    /// array recalculates, the array-membership guard (TryGetArrayExtent, which backs "You cannot
    /// change part of an array") must still recognize every cell of the declared block. Before the
    /// fix, the scalar branch called ClearSpillRange but never SetSpillRange, so
    /// TryGetArrayExtent(H1) returned false the moment recalc ran -- silently dropping the guard.
    /// </summary>
    [Fact]
    public void LegacyCseArray_ScalarResult_ArrayGuardSurvivesRecalculation()
    {
        var (engine, wb) = MakeEngine();
        var sheet = wb.Sheets.First();

        for (uint row = 1; row <= 5; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(row));

        var h1 = new CellAddress(sheet.Id, 1, 8);
        var h2 = new CellAddress(sheet.Id, 2, 8);
        var legacyCell = Cell.FromFormula("SUM(A1:A5)");
        legacyCell.LegacyArrayRows = 2;
        legacyCell.LegacyArrayCols = 1;
        sheet.SetCell(h1, legacyCell);
        engine.RebuildFormulaDependencies(wb);

        engine.Recalculate(wb, [h1]);

        sheet.TryGetArrayExtent(h1, out var anchor1, out var rows1, out var cols1).Should().BeTrue(
            "the anchor cell must still be recognized as an array member after recalculation");
        anchor1.Should().Be(h1);
        rows1.Should().Be(2u);
        cols1.Should().Be(1u);

        sheet.TryGetArrayExtent(h2, out var anchor2, out var rows2, out var cols2).Should().BeTrue(
            "H2 (a non-anchor declared member) must also still be recognized as part of the array " +
            "after recalculation, so 'You cannot change part of an array' keeps guarding it");
        anchor2.Should().Be(h1);
        rows2.Should().Be(2u);
        cols2.Should().Be(1u);
    }
}
