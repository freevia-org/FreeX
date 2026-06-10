using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

// SUBTOTAL/AGGREGATE ignore other SUBTOTAL/AGGREGATE cells (incl. themselves) in their range, so a SUBTOTAL
// that sits inside its own referenced range — e.g. a totals-style =SUBTOTAL(109,B4:B12) placed at B12 — is
// NOT a circular reference in Excel. FreeX's dependency graph must not count the SUBTOTAL cell as its own
// precedent. A plain =SUM over a self-including range stays circular. Mirrors the ConditionalFormattingSamples
// fidelity finding (=SUBTOTAL(109,[Sales]) inside the Sales column returned #CIRCULAR!).
public class SubtotalSelfReferenceTests
{
    private static (RecalcEngine engine, Workbook wb, Sheet sheet) Setup()
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("Sheet1");
        // B4:B11 = 1..8 (sum 36); B12 will hold the SUBTOTAL that references the whole B4:B12 column.
        for (uint r = 4; r <= 11; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 2), new NumberValue(r - 3));
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        return (engine, wb, sheet);
    }

    [Fact]
    public void Subtotal_InsideOwnRange_ComputesInsteadOfCircular()
    {
        var (engine, wb, sheet) = Setup();
        sheet.SetFormula(new CellAddress(sheet.Id, 12, 2), "SUBTOTAL(109,B4:B12)"); // 109 = visible SUM
        var report = engine.RecalculateAllFormulas(wb);

        report.CyclicCells.Should().BeEmpty();
        sheet.GetValue(12, 2).Should().Be(new NumberValue(36)); // SUBTOTAL ignores its own cell
    }

    [Fact]
    public void Subtotal_BelowDependingOnInRangeSubtotal_AlsoComputes()
    {
        var (engine, wb, sheet) = Setup();
        sheet.SetFormula(new CellAddress(sheet.Id, 12, 2), "SUBTOTAL(109,B4:B12)");  // at B12, inside its range
        sheet.SetFormula(new CellAddress(sheet.Id, 13, 2), "SUBTOTAL(9,B4:B12)");    // at B13, range incl B12
        var report = engine.RecalculateAllFormulas(wb);

        report.CyclicCells.Should().BeEmpty();
        sheet.GetValue(12, 2).Should().Be(new NumberValue(36));
        sheet.GetValue(13, 2).Should().Be(new NumberValue(36)); // SUBTOTAL excludes the nested B12 subtotal
    }

    [Fact]
    public void PlainSum_InsideOwnRange_StaysCircular()
    {
        // A non-SUBTOTAL aggregate that includes its own cell is a genuine circular reference.
        var (engine, wb, sheet) = Setup();
        sheet.SetFormula(new CellAddress(sheet.Id, 12, 2), "SUM(B4:B12)");
        var report = engine.RecalculateAllFormulas(wb);

        report.CyclicCells.Should().Contain(new CellAddress(sheet.Id, 12, 2));
    }
}
