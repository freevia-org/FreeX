using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression tests for R15-crosscutting-concurrency-reentrancy-1/2: the WPF host shares ONE
/// singleton RecalcEngine/DependencyGraph across every open workbook. These tests reproduce that
/// by constructing a single RecalcEngine and feeding it two independent Workbook instances (A and
/// B), mirroring how MainWindow's shared engine is handed a different Workbook per open document.
/// </summary>
public class R15_concurrency_Tests
{
    private static RecalcEngine MakeSharedEngine() => new(new DependencyGraph(), new FormulaEvaluator());

    [Fact]
    public void RebuildFormulaDependencies_ScopedToWorkbook_PreservesOtherWorkbooksEdges()
    {
        // One engine shared by two workbooks, as the WPF host does.
        var engine = MakeSharedEngine();

        var wbA = new Workbook("A");
        var sheetA = wbA.AddSheet("Sheet1");
        var aA1 = new CellAddress(sheetA.Id, 1, 1);
        var aB1 = new CellAddress(sheetA.Id, 1, 2);
        sheetA.SetCell(aA1, new NumberValue(10));
        sheetA.SetFormula(aB1, "=A1");
        engine.RebuildFormulaDependencies(wbA);
        engine.Recalculate(wbA, [aB1]);
        sheetA.GetValue(1, 2).Should().Be(new NumberValue(10));

        var wbB = new Workbook("B");
        var sheetB = wbB.AddSheet("Sheet1");
        var bA1 = new CellAddress(sheetB.Id, 1, 1);
        var bB1 = new CellAddress(sheetB.Id, 1, 2);
        sheetB.SetCell(bA1, new NumberValue(1));
        sheetB.SetFormula(bB1, "=A1");
        engine.RebuildFormulaDependencies(wbB);
        engine.Recalculate(wbB, [bB1]);
        sheetB.GetValue(1, 2).Should().Be(new NumberValue(1));

        // Simulate File > Open (or any full-formula reload) re-registering workbook B's formulas
        // against the shared singleton engine. Before the fix, RebuildFormulaDependencies called
        // DependencyGraph.ClearAll(), wiping every edge for every open workbook — including A's.
        engine.RebuildFormulaDependencies(wbB);

        // Workbook A's edge A!A1 -> A!B1 must have survived rebuilding workbook B: editing A!A1
        // must still recalculate A!B1.
        sheetA.SetCell(aA1, new NumberValue(99));
        engine.Recalculate(wbA, [aA1]);

        sheetA.GetValue(1, 2).Should().Be(new NumberValue(99),
            "rebuilding workbook B's dependencies must not erase workbook A's edges from the shared graph");
    }

    [Fact]
    public void SpillRetryLoop_ForeignAnchorFromOtherWorkbook_SurvivesRecalculationOfDifferentWorkbook()
    {
        // One engine shared by two workbooks, as the WPF host does.
        var engine = MakeSharedEngine();

        var wbA = new Workbook("A");
        var sheetA = wbA.AddSheet("Sheet1");
        var spillAnchor = new CellAddress(sheetA.Id, 1, 1);
        var blocker = new CellAddress(sheetA.Id, 2, 1); // A2 — occupies the second spill row.
        sheetA.SetFormula(spillAnchor, "=SEQUENCE(2)");
        sheetA.SetCell(blocker, new NumberValue(999));
        engine.RebuildFormulaDependencies(wbA);
        engine.Recalculate(wbA, [spillAnchor]);

        sheetA.GetValue(1, 1).Should().Be(ErrorValue.Spill,
            "A2 is occupied so A1's SEQUENCE(2) cannot spill and must be tracked as a blocked anchor");

        var wbB = new Workbook("B");
        var sheetB = wbB.AddSheet("Sheet1");
        var bCell = new CellAddress(sheetB.Id, 1, 1);
        sheetB.SetCell(bCell, new NumberValue(1));
        engine.RebuildFormulaDependencies(wbB);

        // Recalculating a completely unrelated workbook (B) must not evict workbook A's
        // #SPILL!-blocked anchor from the shared engine's tracking set. Before the fix, the
        // spill-retry loop ran unconditionally over every tracked anchor regardless of which
        // workbook was passed in, and treated A's anchor as "stale" (its sheet is absent from B)
        // and evicted it — permanently stranding A1 at #SPILL! even after the blocker clears.
        engine.Recalculate(wbB, [bCell]);

        // Clear the blocker and recalculate workbook A: the edit to A2 has no dependency-graph
        // edge back to A1 (A1's formula never statically referenced A2), so only the surviving
        // _spillBlockedAnchors retry mechanism can make A1 re-spill. Use ClearCell (not
        // SetCell(BlankValue)) so the cell is truly removed from _cells — IsSpillBlocked blocks on
        // cell *presence*, so a materialized BlankValue would still occupy the spill row.
        sheetA.ClearCell(blocker);
        engine.Recalculate(wbA, [blocker]);

        sheetA.GetValue(1, 1).Should().Be(new NumberValue(1),
            "clearing the blocker should let A1 re-spill now that its anchor tracking survived workbook B's recalculation");
        sheetA.GetValue(2, 1).Should().Be(new NumberValue(2));
    }
}
