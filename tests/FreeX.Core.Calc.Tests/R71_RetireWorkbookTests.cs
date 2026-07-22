using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression coverage for R71-calc-volatile-recalc-4-1: the app-lifetime singleton
/// <see cref="RecalcEngine"/> never released a closed/replaced workbook's volatile-cell tracking
/// or dependency edges -- <see cref="RecalcEngine.RebuildFormulaDependencies"/> only clears/registers
/// the NEW workbook's own sheets, so an OLD workbook's SheetId-keyed entries in the shared
/// volatile-cell set and dependency graph leaked forever across an Open/New/close session. The fix
/// adds <see cref="RecalcEngine.RetireWorkbook"/>, which the App.Host callers now invoke on the
/// outgoing workbook before swapping in a replacement.
/// </summary>
public sealed class R71_RetireWorkbookTests
{
    private static RecalcEngine Engine(out DependencyGraph graph)
    {
        graph = new DependencyGraph();
        return new RecalcEngine(graph, new FormulaEvaluator());
    }

    [Fact]
    public void RetireWorkbook_RemovesOutgoingWorkbookVolatileCellsAndDependencyEdges()
    {
        var engine = Engine(out var graph);

        var workbookA = new Workbook("A");
        var sheetA = workbookA.AddSheet("Sheet1");
        var aVolatile = new CellAddress(sheetA.Id, 1, 1);
        var aPrecedent = new CellAddress(sheetA.Id, 2, 1);
        var aDependent = new CellAddress(sheetA.Id, 2, 2);
        sheetA.SetFormula(aVolatile, "NOW()");
        sheetA.SetCell(aPrecedent, new NumberValue(5));
        sheetA.SetFormula(aDependent, "B2*2");

        engine.RecalculateAllFormulas(workbookA);

        var workbookB = new Workbook("B");
        var sheetB = workbookB.AddSheet("Sheet1");
        var bVolatile = new CellAddress(sheetB.Id, 1, 1);
        sheetB.SetFormula(bVolatile, "OFFSET(A2,0,0)");

        engine.RecalculateAllFormulas(workbookB);

        // Before retiring A: both workbooks' volatile cells and A's dependency edge are live.
        engine.VolatileCellCountForTests.Should().Be(2, "A's NOW() and B's OFFSET() are both registered");
        graph.HasDependencies(aDependent).Should().BeTrue("A's dependency graph edge is still registered");

        // A is closed/replaced (no sibling window shares it) -- retire it.
        engine.RetireWorkbook(workbookA);

        engine.VolatileCellCountForTests.Should().Be(1, "only B's volatile cell should remain after A is retired");
        graph.HasDependencies(aDependent).Should().BeFalse("A's dependency graph edges must be purged on retire");

        // B's own tracking must be untouched by retiring A.
        var reportB = engine.RecalculateAllFormulas(workbookB);
        reportB.RecalculatedCells.Should().Contain(bVolatile, "B's volatile cell must still be tracked and recalculated");
    }

    [Fact]
    public void RetireWorkbook_RepeatedOpenNewCycle_DoesNotAccumulateVolatileCells()
    {
        // Sibling/no-regression case: repeating the Open/New swap-and-retire cycle several times
        // must leave only the CURRENT workbook's volatile cells registered, not a growing pile of
        // every previously closed workbook's stale entries.
        var engine = Engine(out _);

        Workbook? previous = null;
        for (var i = 0; i < 5; i++)
        {
            var workbook = new Workbook($"Book{i}");
            var sheet = workbook.AddSheet("Sheet1");
            sheet.SetFormula(new CellAddress(sheet.Id, 1, 1), "RAND()");
            engine.RecalculateAllFormulas(workbook);

            if (previous is not null)
                engine.RetireWorkbook(previous);

            previous = workbook;
        }

        engine.VolatileCellCountForTests.Should().Be(1,
            "only the final workbook's volatile cell should remain after four intervening retires");
    }

    [Fact]
    public void RetireWorkbook_ThenNormalEditOnSurvivingWorkbook_StillRecalculatesVolatiles()
    {
        // Sibling/no-regression case: retiring an unrelated closed workbook must not disturb a
        // normal single-workbook edit-and-recalc flow on the surviving workbook.
        var engine = Engine(out _);

        var closedWorkbook = new Workbook("Closed");
        var closedSheet = closedWorkbook.AddSheet("Sheet1");
        closedSheet.SetFormula(new CellAddress(closedSheet.Id, 1, 1), "NOW()");
        engine.RecalculateAllFormulas(closedWorkbook);

        var liveWorkbook = new Workbook("Live");
        var liveSheet = liveWorkbook.AddSheet("Sheet1");
        var a1 = new CellAddress(liveSheet.Id, 1, 1);
        liveSheet.SetFormula(a1, "RAND()");
        engine.RecalculateAllFormulas(liveWorkbook);
        var beforeValue = ((NumberValue)liveSheet.GetValue(a1)).Value;

        engine.RetireWorkbook(closedWorkbook);

        var report = engine.RecalculateAllFormulas(liveWorkbook);
        report.RecalculatedCells.Should().Contain(a1);
        var afterValue = ((NumberValue)liveSheet.GetValue(a1)).Value;
        afterValue.Should().NotBe(beforeValue, "RAND() must still be re-evaluated on the surviving workbook");
    }
}
