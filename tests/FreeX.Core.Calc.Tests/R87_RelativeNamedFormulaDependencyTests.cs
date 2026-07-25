using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;
using Xunit;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// R87-calc-defname: a relative-ref defined name (RefersTo with no $, e.g. "=B2") must be
/// evaluated -- and DEPEND ON -- the per-using-cell shifted target, exactly like
/// FormulaEvaluator.ApplyRelativeNameAnchor shifts it at evaluation time. Before the fix,
/// RecalcEngine.CollectReferences' NamedRangeNode case registered a dependency on the name's
/// literal (unshifted) RefersTo cell instead, so editing the true (shifted) target never marked
/// the consumer cell dirty, and editing the literal RefersTo cell spuriously dirtied it instead.
/// </summary>
public sealed class R87_RelativeNamedFormulaDependencyTests
{
    private static RecalcEngine Engine() =>
        new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());

    [Fact]
    public void Recalculate_RelativeNamedFormula_TwoConsumersDependOnTheirOwnShiftedTarget()
    {
        // RelName's RefersTo is relative ("=B2", anchored at A1 per Excel's own convention).
        // D10 (row 10, col 4) shifts B2 by (+9,+3) -> E11 (row 11, col 5).
        // F20 (row 20, col 6) shifts B2 by (+19,+5) -> G21 (row 21, col 7).
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        var engine = Engine();

        var d10 = new CellAddress(sheet.Id, 10, 4);
        var f20 = new CellAddress(sheet.Id, 20, 6);
        var e11 = new CellAddress(sheet.Id, 11, 5);
        var g21 = new CellAddress(sheet.Id, 21, 7);

        sheet.SetCell(e11, new NumberValue(100));
        sheet.SetCell(g21, new NumberValue(200));

        wb.NamedFormulas["RelName"] = "B2";
        sheet.SetFormula(d10, "RelName");
        sheet.SetFormula(f20, "RelName");

        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [d10, f20]);

        // Sanity: each consumer evaluates against its OWN shifted target.
        sheet.GetValue(d10).Should().Be(new NumberValue(100));
        sheet.GetValue(f20).Should().Be(new NumberValue(200));

        // Act: change D10's true (shifted) target, E11, and recalc incrementally off just that cell.
        sheet.SetCell(e11, new NumberValue(999));
        var report = engine.Recalculate(wb, [e11]);

        // Assert: D10 must be marked dirty and recalculate to reflect E11's new value...
        report.RecalculatedCells.Should().Contain(d10,
            "D10's relative-named-range target is E11 (its own shifted target), so editing E11 must dirty D10");
        sheet.GetValue(d10).Should().Be(new NumberValue(999));

        // ...while F20 (whose own shifted target is G21, unaffected) must NOT have recalculated
        // off of E11's edit, and must still reflect G21's original value.
        report.RecalculatedCells.Should().NotContain(f20,
            "F20 depends on its own shifted target G21, not E11 -- it must not be dirtied by E11's edit");
        sheet.GetValue(f20).Should().Be(new NumberValue(200));

        // Act: now change F20's own shifted target, G21, and confirm it (and only it) reacts.
        sheet.SetCell(g21, new NumberValue(555));
        var report2 = engine.Recalculate(wb, [g21]);

        report2.RecalculatedCells.Should().Contain(f20,
            "F20's relative-named-range target is G21 (its own shifted target), so editing G21 must dirty F20");
        sheet.GetValue(f20).Should().Be(new NumberValue(555));
        report2.RecalculatedCells.Should().NotContain(d10,
            "D10 depends on its own shifted target E11, not G21 -- it must not be dirtied by G21's edit");
        sheet.GetValue(d10).Should().Be(new NumberValue(999));
    }

    [Fact]
    public void Recalculate_AbsoluteNamedFormula_NoRegression_AllConsumersShareTheSameLiteralTarget()
    {
        // No-regression sibling: an ABSOLUTE-ref name ("=$B$2") is never shifted, so every
        // consumer cell -- regardless of its own position -- must keep depending on the exact
        // same literal B2, exactly as before this fix (ShiftFormulaForCell is a no-op for
        // fully-absolute references).
        var wb = new Workbook("T");
        var sheet = wb.AddSheet("S");
        var engine = Engine();

        var b2 = new CellAddress(sheet.Id, 2, 2);
        var d10 = new CellAddress(sheet.Id, 10, 4);
        var f20 = new CellAddress(sheet.Id, 20, 6);

        sheet.SetCell(b2, new NumberValue(42));

        wb.NamedFormulas["AbsName"] = "$B$2";
        sheet.SetFormula(d10, "AbsName");
        sheet.SetFormula(f20, "AbsName");

        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [d10, f20]);

        sheet.GetValue(d10).Should().Be(new NumberValue(42));
        sheet.GetValue(f20).Should().Be(new NumberValue(42));

        sheet.SetCell(b2, new NumberValue(7));
        var report = engine.Recalculate(wb, [b2]);

        report.RecalculatedCells.Should().Contain(d10, "both consumers share the same absolute target B2");
        report.RecalculatedCells.Should().Contain(f20, "both consumers share the same absolute target B2");
        sheet.GetValue(d10).Should().Be(new NumberValue(7));
        sheet.GetValue(f20).Should().Be(new NumberValue(7));
    }
}
