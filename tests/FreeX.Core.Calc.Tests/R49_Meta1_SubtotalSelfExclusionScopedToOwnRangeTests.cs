using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

// R49-meta-1: RegisterFormulaDependencies' SUBTOTAL/AGGREGATE self-exclusion (ExcludeCell) used to
// apply FORMULA-WIDE — it removed the cell's own self-edge whenever the formula contained ANY
// SUBTOTAL/AGGREGATE call anywhere in the expression, even when the self-reference term was
// INDEPENDENT of that call's own range argument. "=B10+SUBTOTAL(9,B1:B9)" at B10 has a bare "B10"
// addend that has nothing to do with SUBTOTAL(9,B1:B9)'s nested-ignore rule (B1:B9 doesn't even
// contain B10) — this is a genuine circular reference and must be flagged, exactly like Excel does.
// The fix scopes the self-exclusion to only apply when the formula's own cell actually falls INSIDE
// that specific SUBTOTAL/AGGREGATE call's own range argument(s).
public class R49_Meta1_SubtotalSelfExclusionScopedToOwnRangeTests
{
    private static (RecalcEngine engine, Workbook wb, Sheet sheet) Setup()
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("Sheet1");
        for (uint r = 1; r <= 9; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 2), new NumberValue(r));
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        return (engine, wb, sheet);
    }

    [Fact]
    public void SelfReferenceIndependentOfSubtotalRange_IsGenuinelyCircular()
    {
        // "=B10+SUBTOTAL(9,B1:B9)" at B10: the bare "B10" addend is independent of SUBTOTAL's own
        // range argument (B1:B9, which does NOT include B10), so this is a real circular reference —
        // SUBTOTAL's nested-ignore rule must not silently swallow it.
        var (engine, wb, sheet) = Setup();
        var b10 = new CellAddress(sheet.Id, 10, 2);
        sheet.SetFormula(b10, "B10+SUBTOTAL(9,B1:B9)");

        var report = engine.RecalculateAllFormulas(wb);

        report.CyclicCells.Should().Contain(b10);
    }

    [Fact]
    public void SelfReferenceInsideSubtotalRange_StillExcludedAndComputes()
    {
        // Sibling/no-regression: "=1+SUBTOTAL(9,B1:B10)" at B10 — here B10 DOES fall inside
        // SUBTOTAL's own range argument (B1:B10), so Excel's nested-ignore rule legitimately
        // excludes it from its own dependency set and this must NOT be flagged circular.
        var (engine, wb, sheet) = Setup();
        var b10 = new CellAddress(sheet.Id, 10, 2);
        sheet.SetFormula(b10, "1+SUBTOTAL(9,B1:B10)");

        var report = engine.RecalculateAllFormulas(wb);

        report.CyclicCells.Should().BeEmpty();
        sheet.GetValue(10, 2).Should().Be(new NumberValue(46)); // 1 + SUM(B1:B9) = 1 + 45
    }
}
