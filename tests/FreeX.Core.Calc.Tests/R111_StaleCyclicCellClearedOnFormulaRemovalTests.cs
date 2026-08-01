using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression coverage for R111-calc-stale-cyclic-leak: <see cref="RecalcEngine.CyclicCells"/> (the
/// set FormulaAuditingService's "Formulas with circular references" error-checking rule reads
/// straight through -- see R68_Meta1_CircularReferenceErrorCheckingTests) is only pruned for a given
/// address by the ordinary per-cell evaluation loop, which is gated behind the cell still
/// <c>HasFormula</c>. When a previously-cyclic cell is edited (or Undo/Redo'd) to a plain value
/// instead of a new formula, it stops being visited by that loop entirely, so the stale entry used to
/// survive forever -- across F9/full recalc and save/reload -- until <see
/// cref="RecalcEngine.ClearFormulaDependencies"/> (the choke point every real edit path routes
/// through: WorkbookCellEditService.UpdateFormulaDependencies for a plain edit or Undo/Redo, and
/// RecalcEngine's own ClearVacatedFormulaDependencies for a structural relocation) was taught to also
/// drop the address from _cyclicCells / _activeIterativeCyclicCells.
/// </summary>
public sealed class R111_StaleCyclicCellClearedOnFormulaRemovalTests
{
    private static RecalcEngine Engine() => new(new DependencyGraph(), new FormulaEvaluator());

    [Fact]
    public void CyclicCellOverwrittenWithPlainValue_IsRemovedFromEngineCyclicCells()
    {
        // A1="=B1", B1="=A1" -- classic 2-cycle, iterative calculation off (default).
        var wb = new Workbook();
        var sheet = wb.AddSheet("Sheet1");
        var engine = Engine();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetFormula(a1, "B1");
        sheet.SetFormula(b1, "A1");
        engine.RebuildFormulaDependencies(wb);

        engine.Recalculate(wb, [a1, b1]);
        engine.CyclicCells.Should().Contain(a1).And.Contain(b1, "both cells are seeded cyclic by the initial recalc");

        // Break the cycle the way a real user edit (or an Undo/Redo restoring a prior plain value)
        // does: B1 becomes a plain constant, NOT a new formula. This is exactly what
        // WorkbookCellEditService.ApplyHistoryOutcome does for a plain-value edit/undo -- it calls
        // Sheet.SetCell with a non-formula Cell and then routes through
        // RecalcEngine.ClearFormulaDependencies for the now-non-formula address.
        sheet.SetCell(b1, new NumberValue(42));

        // The subsequent recalc pass: B1 is no longer a formula cell, so it is never enumerated in
        // the ordinary evaluation loop (evaluationPlan.OrderedCells / directFormulaRoots) -- that
        // loop's own _cyclicCells.Remove(addr) at line ~352 is gated behind cell.HasFormula and never
        // runs for B1. Only ClearVacatedFormulaDependencies (called unconditionally at the top of
        // Recalculate for every address in the full changedCells list) reaches it, via
        // RecalcEngine.ClearFormulaDependencies.
        engine.Recalculate(wb, [a1, b1]);

        engine.CyclicCells.Should().NotContain(b1,
            "B1 no longer has a formula at all and so can no longer participate in any circular reference -- Excel never flags a plain-value cell this way");
        sheet.GetValue(b1.Row, b1.Col).Should().Be(new NumberValue(42), "the plain value edit must still take effect normally");

        // A1 still depends on B1 and is not itself cyclic anymore either -- it re-evaluates through
        // the ordinary loop to B1's new value.
        engine.CyclicCells.Should().NotContain(a1);
        sheet.GetValue(a1.Row, a1.Col).Should().Be(new NumberValue(42));
    }

    [Fact]
    public void CyclicCellEditedToNewFormula_StillDropsFromEngineCyclicCells_NoRegression()
    {
        // Sibling/no-regression: the pre-existing path (a cyclic cell edited to a DIFFERENT formula
        // that breaks the cycle, so it DOES run through the ordinary evaluation loop) must keep
        // working exactly as before -- this is the R68_Meta1 lifecycle test's scenario, re-asserted
        // here alongside the new plain-value path so both siblings in the same family (formula-to-
        // formula vs. formula-to-plain-value) are covered in one place.
        var wb = new Workbook();
        var sheet = wb.AddSheet("Sheet1");
        var engine = Engine();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetFormula(a1, "B1");
        sheet.SetFormula(b1, "A1");
        engine.RebuildFormulaDependencies(wb);

        engine.Recalculate(wb, [a1, b1]);
        engine.CyclicCells.Should().Contain(a1).And.Contain(b1);

        sheet.SetFormula(b1, "5");
        engine.Recalculate(wb, [b1]);

        engine.CyclicCells.Should().NotContain(b1, "B1 now has a non-cyclic formula and evaluates normally");
    }

    [Fact]
    public void ClearFormulaDependencies_DirectlyDropsAddressFromCyclicCells()
    {
        // Narrower unit-level check on the choke point itself: any caller of ClearFormulaDependencies
        // (WorkbookCellEditService.UpdateFormulaDependencies, RecalcEngine.ClearVacatedFormulaDependencies,
        // and the two FormulaParseException handlers inside the main/iterative evaluation loops) must
        // see the address drop out of CyclicCells immediately, without depending on a subsequent
        // Recalculate pass to also happen to visit it.
        var wb = new Workbook();
        var sheet = wb.AddSheet("Sheet1");
        var engine = Engine();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetFormula(a1, "B1");
        sheet.SetFormula(b1, "A1");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [a1, b1]);
        engine.CyclicCells.Should().Contain(b1);

        engine.ClearFormulaDependencies(b1);

        engine.CyclicCells.Should().NotContain(b1);
    }
}
