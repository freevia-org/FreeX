using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

// R48-formula-aggregate-subtotal-3-1 / R48-formula-aggregate-subtotal-3-2:
//
// SUBTOTAL always ignores other SUBTOTAL/AGGREGATE cells (including itself) within its range, so
// a self-referencing =SUBTOTAL(...) is not circular in Excel. AGGREGATE only ignores nested
// SUBTOTAL/AGGREGATE cells for options 0-3 ("...and ignore nested SUBTOTAL and AGGREGATE
// functions"); options 4-7 do NOT ignore nested cells, so a self-range AGGREGATE with options 4-7
// is genuinely circular, exactly like any other self-referencing formula. Separately, the
// self-exclusion must fire whenever the formula CONTAINS a SUBTOTAL/AGGREGATE call anywhere in
// the expression (e.g. "=1+SUBTOTAL(...)"), not only when that call is the formula's literal
// root node — mirroring BuiltInFunctions.Subtotal's own ContainsFunctionCall text-scan rule used
// to exclude OTHER cells within an aggregated range.
public class R48_AggregateSubtotalSelfExclusionOptionsTests
{
    private static (RecalcEngine engine, Workbook wb, Sheet sheet) Setup()
    {
        var wb = new Workbook();
        var sheet = wb.AddSheet("Sheet1");
        // B1:B9 = 1..9 (sum 45); B10 will hold the AGGREGATE/SUBTOTAL formula referencing B1:B10.
        for (uint r = 1; r <= 9; r++)
            sheet.SetCell(new CellAddress(sheet.Id, r, 2), new NumberValue(r));
        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        return (engine, wb, sheet);
    }

    // --- R48-formula-aggregate-subtotal-3-1 -------------------------------------------------

    [Fact]
    public void Aggregate_SelfRange_OptionsFourToSeven_IsGenuinelyCircular()
    {
        // options=6 ("ignore error values" only) does NOT ignore nested SUBTOTAL/AGGREGATE cells
        // per Excel's documented options table, so B10 depends on itself: a real circular reference.
        var (engine, wb, sheet) = Setup();
        sheet.SetFormula(new CellAddress(sheet.Id, 10, 2), "AGGREGATE(9,6,B1:B10)");
        var report = engine.RecalculateAllFormulas(wb);

        report.CyclicCells.Should().Contain(new CellAddress(sheet.Id, 10, 2));
    }

    [Fact]
    public void Aggregate_SelfRange_OptionsZeroToThree_ComputesInsteadOfCircular()
    {
        // options=0 ("ignore nested SUBTOTAL/AGGREGATE") DOES ignore nested cells (incl. itself),
        // so this is the same non-circular self-exclusion SUBTOTAL always gets.
        var (engine, wb, sheet) = Setup();
        sheet.SetFormula(new CellAddress(sheet.Id, 10, 2), "AGGREGATE(9,0,B1:B10)");
        var report = engine.RecalculateAllFormulas(wb);

        report.CyclicCells.Should().BeEmpty();
        sheet.GetValue(10, 2).Should().Be(new NumberValue(45));
    }

    // --- R48-formula-aggregate-subtotal-3-2 -------------------------------------------------

    [Fact]
    public void Subtotal_EmbeddedInLargerExpression_ExcludesSelfLikeRootCall()
    {
        // "=1+SUBTOTAL(9,B1:B10)" — SUBTOTAL is not the formula's literal root (the '+' is), but
        // Excel's nested-ignore rule still applies to it: B10 must not depend on itself.
        var (engine, wb, sheet) = Setup();
        sheet.SetFormula(new CellAddress(sheet.Id, 10, 2), "1+SUBTOTAL(9,B1:B10)");
        var report = engine.RecalculateAllFormulas(wb);

        report.CyclicCells.Should().BeEmpty();
        sheet.GetValue(10, 2).Should().Be(new NumberValue(46)); // 1 + SUM(B1:B9) = 1 + 45
    }

    [Fact]
    public void Subtotal_AsLiteralRootCall_StillExcludesSelf()
    {
        // No-regression sibling: the plain root-call shape (already relied upon elsewhere) must
        // keep working once the check is generalized to look past the root node.
        var (engine, wb, sheet) = Setup();
        sheet.SetFormula(new CellAddress(sheet.Id, 10, 2), "SUBTOTAL(9,B1:B10)");
        var report = engine.RecalculateAllFormulas(wb);

        report.CyclicCells.Should().BeEmpty();
        sheet.GetValue(10, 2).Should().Be(new NumberValue(45));
    }
}
