using ClosedXML.Excel;
using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.IO;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Integration.Tests;

// Legacy multi-cell array formulas (CSE / Ctrl+Shift+Enter) store one formula on the top-left anchor with a
// declared <f t="array" ref="..."> range; Excel propagates the formula to every covered cell. FreeX must load
// only the anchor and fill the declared range on recalc — not create a duplicate formula per covered cell
// (which mutually block each other's spill and produce #SPILL!). Mirrors the MatrixFormulaEvalTestData fidelity
// finding where MMULT/MINVERSE/TRANSPOSE/matrix-arithmetic array formulas all returned #SPILL!.
public class LegacyArrayFormulaTests
{
    private static MemoryStream BuildWorkbookWithMultiCellArrayFormula()
    {
        var ms = new MemoryStream();
        using (var xl = new XLWorkbook())
        {
            var ws = xl.AddWorksheet("S");
            ws.Cell(1, 1).Value = 1; ws.Cell(1, 2).Value = 2;   // A1:B1
            ws.Cell(2, 1).Value = 3; ws.Cell(2, 2).Value = 4;   // A2:B2
            ws.Cell(1, 5).Value = 10; ws.Cell(1, 6).Value = 20; // E1:F1
            ws.Cell(2, 5).Value = 30; ws.Cell(2, 6).Value = 40; // E2:F2
            // Multi-cell legacy array formula across H1:I2 (writes <f t="array" ref="H1:I2">).
            ws.Range("H1:I2").FormulaArrayA1 = "A1:B2+E1:F2";
            xl.SaveAs(ms);
        }
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void MultiCellArrayFormula_FillsDeclaredRange_NotSpillError()
    {
        using var ms = BuildWorkbookWithMultiCellArrayFormula();
        var wb = new XlsxFileAdapter().Load(ms);
        var sheet = wb.GetSheetAt(0);

        new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()).RecalculateAllFormulas(wb);

        // Element-wise A1:B2 + E1:F2 must fill the declared H1:I2 range — no #SPILL!.
        sheet.GetValue(1, 8).Should().Be(new NumberValue(11)); // H1 = 1 + 10
        sheet.GetValue(1, 9).Should().Be(new NumberValue(22)); // I1 = 2 + 20
        sheet.GetValue(2, 8).Should().Be(new NumberValue(33)); // H2 = 3 + 30
        sheet.GetValue(2, 9).Should().Be(new NumberValue(44)); // I2 = 4 + 40
    }

    [Fact]
    public void MultiCellArrayFormula_LoadsOnlyAnchorAsFormulaCell()
    {
        using var ms = BuildWorkbookWithMultiCellArrayFormula();
        var wb = new XlsxFileAdapter().Load(ms);
        var sheet = wb.GetSheetAt(0);

        // Only the top-left anchor H1 carries the formula; covered cells are not independent formula cells.
        sheet.GetCell(1, 8)!.HasFormula.Should().BeTrue();
        sheet.GetCell(1, 9)?.HasFormula.Should().NotBe(true); // I1
        sheet.GetCell(2, 8)?.HasFormula.Should().NotBe(true); // H2
        sheet.GetCell(2, 9)?.HasFormula.Should().NotBe(true); // I2
    }

    [Fact]
    public void MultiCellArrayFormula_SurvivesSaveReloadRecalc()
    {
        using var ms = BuildWorkbookWithMultiCellArrayFormula();
        var adapter = new XlsxFileAdapter();
        var wb = adapter.Load(ms);
        new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()).RecalculateAllFormulas(wb);

        using var saved = new MemoryStream();
        adapter.Save(wb, saved);
        saved.Position = 0;

        var reloaded = adapter.Load(saved);
        var sheet = reloaded.GetSheetAt(0);
        new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()).RecalculateAllFormulas(reloaded);

        // The array formula's declared range must still fill correctly after a save/reload round-trip.
        sheet.GetValue(1, 8).Should().Be(new NumberValue(11)); // H1
        sheet.GetValue(1, 9).Should().Be(new NumberValue(22)); // I1
        sheet.GetValue(2, 8).Should().Be(new NumberValue(33)); // H2
        sheet.GetValue(2, 9).Should().Be(new NumberValue(44)); // I2
    }

    private static MemoryStream BuildWorkbookWithScalarResultArrayFormula()
    {
        var ms = new MemoryStream();
        using (var xl = new XLWorkbook())
        {
            var ws = xl.AddWorksheet("S");
            // A1:A5 = 1..5.
            for (var r = 1; r <= 5; r++)
                ws.Cell(r, 1).Value = r;
            // H1:H2 (2 rows x 1 col) CSE-entered as {=SUM(A1:A5)}: a formula whose natural
            // result is a plain scalar, not a range -- writes <f t="array" ref="H1:H2">.
            ws.Range("H1:H2").FormulaArrayA1 = "SUM(A1:A5)";
            xl.SaveAs(ms);
        }
        ms.Position = 0;
        return ms;
    }

    // R141 cse-scalar-result-not-replicated: a real .xlsx authored the way Excel itself would
    // (Ctrl+Shift+Enter over a multi-cell block with a formula that naturally returns a scalar)
    // must, on load + recalc through the real production XlsxFileAdapter + RecalcEngine, fill
    // every declared cell with the same scalar value -- exactly like MultiCellArrayFormula_*
    // above proves for a naturally-range-shaped formula. Before the fix, only H1 (the anchor)
    // got the SUM result; H2 stayed blank because the scalar branch of RecalcEngine never
    // consulted LegacyArrayRows/Cols.
    [Fact]
    public void ScalarResultArrayFormula_FillsDeclaredRange_OnLoadAndRecalc()
    {
        using var ms = BuildWorkbookWithScalarResultArrayFormula();
        var wb = new XlsxFileAdapter().Load(ms);
        var sheet = wb.GetSheetAt(0);

        new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()).RecalculateAllFormulas(wb);

        sheet.GetValue(1, 8).Should().Be(new NumberValue(15), "H1 (anchor) gets the SUM result");
        sheet.GetValue(2, 8).Should().Be(new NumberValue(15),
            "H2 must be replicated with the same scalar result, matching real Excel's CSE " +
            "fill-the-whole-selection behavior instead of staying blank");
    }

    // R141 cse-scalar-result-not-replicated (guard-survival half): after a freshly loaded
    // workbook's scalar-returning CSE array recalculates for the first time, the array-membership
    // guard (TryGetArrayExtent, which backs "You cannot change part of an array") must still
    // recognize every cell of the declared block. Before the fix, the scalar branch called
    // ClearSpillRange but never SetSpillRange, so the provisional load-time membership was torn
    // down and TryGetArrayExtent(H1) returned false the moment recalc ran once.
    [Fact]
    public void ScalarResultArrayFormula_ArrayGuardSurvives_OnLoadAndRecalc()
    {
        using var ms = BuildWorkbookWithScalarResultArrayFormula();
        var wb = new XlsxFileAdapter().Load(ms);
        var sheet = wb.GetSheetAt(0);
        var h1 = new CellAddress(sheet.Id, 1, 8);
        var h2 = new CellAddress(sheet.Id, 2, 8);

        new RecalcEngine(new DependencyGraph(), new FormulaEvaluator()).RecalculateAllFormulas(wb);

        sheet.TryGetArrayExtent(h1, out var anchor1, out var rows1, out var cols1).Should().BeTrue(
            "the anchor cell must still be recognized as an array member after the first recalc " +
            "following load");
        anchor1.Should().Be(h1);
        rows1.Should().Be(2u);
        cols1.Should().Be(1u);

        sheet.TryGetArrayExtent(h2, out var anchor2, out _, out _).Should().BeTrue(
            "H2 (a non-anchor declared member) must also still be recognized as part of the " +
            "array after the first recalc, so 'You cannot change part of an array' keeps guarding it");
        anchor2.Should().Be(h1);
    }
}
