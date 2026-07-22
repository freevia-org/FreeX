using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

// R68-meta-1: the r66 "circular -> 0" fix (see VolatileCircularReferenceTests) made a cyclic cell's
// VALUE seed to a plain 0 instead of ErrorValue.Circular, so FormulaAuditingService's "Formulas with
// circular references" error-checking rule — which only ever scanned cell.Value for an ErrorValue —
// could never match again. The fix threads RecalcEngine's newly-exposed CyclicCells set into
// FindFormulaErrorIssues/FindFormulaErrors so the rule can flag a cyclic cell without relying on a
// fabricated error value.
public class R68_Meta1_CircularReferenceErrorCheckingTests
{
    private static (RecalcEngine engine, Workbook wb, Sheet sheet) Setup()
    {
        var wb = new Workbook(); // IterativeCalculation defaults to false
        var sheet = wb.AddSheet("Sheet1");
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        return (engine, wb, sheet);
    }

    [Fact]
    public void SelfCircularReference_IsFlaggedByErrorCheckingRule_ViaEngineCyclicCells()
    {
        // A1=A1, iterative calculation off.
        var (engine, wb, sheet) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(a1, "A1");

        var report = engine.RecalculateAllFormulas(wb);
        report.CyclicCells.Should().Contain(a1);
        engine.CyclicCells.Should().Contain(a1, "the engine must keep exposing currently-cyclic cells after the recalc pass completes");

        var issues = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id, engine.CyclicCells);

        issues.Should().Contain(issue =>
            issue.Address == a1 &&
            issue.ErrorCode == ErrorValue.Circular.Code,
            "Formulas > Error Checking must flag a self-referencing formula as a circular reference again");
    }

    [Fact]
    public void FindFormulaErrors_AlsoSurfacesCircularReference_ViaEngineCyclicCells()
    {
        var (engine, wb, sheet) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetFormula(a1, "B1");
        sheet.SetFormula(b1, "A1");

        engine.RecalculateAllFormulas(wb);

        var errors = FormulaAuditingService.FindFormulaErrors(wb, sheet.Id, engine.CyclicCells);

        errors.Should().Contain(e => e.Address == a1 && e.Error == ErrorValue.Circular);
        errors.Should().Contain(e => e.Address == b1 && e.Error == ErrorValue.Circular);
    }

    [Fact]
    public void NoCycle_NothingFlagged_AsCircularReference()
    {
        // Sibling/no-regression: a workbook with no circular reference at all must not have any
        // cell flagged with the circular-reference error code, whether or not a (necessarily empty)
        // cyclic-cells set is passed through.
        var (engine, wb, sheet) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(b1, new NumberValue(5));
        sheet.SetFormula(a1, "B1+1");

        var report = engine.RecalculateAllFormulas(wb);
        report.CyclicCells.Should().BeEmpty();
        engine.CyclicCells.Should().BeEmpty();

        var issues = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id, engine.CyclicCells);

        issues.Should().NotContain(issue => issue.ErrorCode == ErrorValue.Circular.Code);
        sheet.GetValue(a1.Row, a1.Col).Should().Be(new NumberValue(6));
    }

    [Fact]
    public void NormalDivByZeroRule_StillWorks_AlongsideCircularReferenceThreading()
    {
        // Sibling/no-regression: an ordinary error-value-based rule (#DIV/0!) must still be found
        // through the plain cell.Value scan, unaffected by the new cyclicCells parameter.
        var (engine, wb, sheet) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(a1, "1/0");

        engine.RecalculateAllFormulas(wb);
        engine.CyclicCells.Should().BeEmpty();

        var issues = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id, engine.CyclicCells);

        issues.Should().Contain(issue => issue.Address == a1 && issue.ErrorCode == ErrorValue.DivByZero.Code);
    }

    [Fact]
    public void CircularReferenceRule_CanStillBeDisabled_ViaDisabledFormulaErrorCodes()
    {
        // Sibling: disabling the rule (Excel's "Error checking rules" options) must still suppress
        // it exactly like every other rule, even though it's now driven by the cyclicCells set.
        var (engine, wb, sheet) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetFormula(a1, "A1");
        wb.DisabledFormulaErrorCodes.Add(ErrorValue.Circular.Code);

        engine.RecalculateAllFormulas(wb);

        var issues = FormulaAuditingService.FindFormulaErrorIssues(wb, sheet.Id, engine.CyclicCells);

        issues.Should().NotContain(issue => issue.ErrorCode == ErrorValue.Circular.Code);
    }

    [Fact]
    public void CyclicCellResolvedByEdit_IsRemovedFromEngineCyclicCells()
    {
        // No-regression on the persisted-set lifecycle: once a formerly-cyclic cell's formula is
        // edited to break the cycle and it recalculates normally, it must drop out of
        // RecalcEngine.CyclicCells so a stale entry doesn't keep flagging it forever.
        var (engine, wb, sheet) = Setup();
        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetFormula(a1, "B1");
        sheet.SetFormula(b1, "A1");

        engine.RecalculateAllFormulas(wb);
        engine.CyclicCells.Should().Contain(a1).And.Contain(b1);

        // Break the cycle: B1 now references a plain value instead of A1.
        sheet.SetFormula(b1, "5");
        engine.Recalculate(wb, [b1]);

        engine.CyclicCells.Should().NotContain(b1, "B1 no longer participates in any cycle");
    }
}
