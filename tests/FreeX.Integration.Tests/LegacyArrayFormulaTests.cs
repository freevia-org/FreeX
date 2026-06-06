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
}
