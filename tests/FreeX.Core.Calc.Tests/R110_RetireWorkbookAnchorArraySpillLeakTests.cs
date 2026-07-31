using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression coverage: <see cref="RecalcEngine"/> is a single, app-lifetime singleton shared by
/// every open workbook (see its class remarks). <see cref="RecalcEngine.RetireWorkbook"/> ->
/// PurgeSheetsFromSharedState purges every SheetId-keyed piece of shared state it documents --
/// dependency graph, volatile cells, spill-blocked anchors, cyclic-cell markers, and the
/// dependency-plan cache -- but used to skip the ANCHORARRAY(anchor,end) 2-arg spill-union
/// bookkeeping (_anchorArraySpillDependents / _anchorArraySpillDependentAnchors), so any workbook
/// that ever used the "=SUM(A1#:B5)" syntax left its anchor/dependent CellAddress entries
/// permanently resident in those dictionaries for the lifetime of the host process even after the
/// workbook was fully closed and retired.
/// </summary>
public sealed class R110_RetireWorkbookAnchorArraySpillLeakTests
{
    private static RecalcEngine Engine()
    {
        var graph = new DependencyGraph();
        return new RecalcEngine(graph, new FormulaEvaluator());
    }

    [Fact]
    public void RetireWorkbook_PurgesAnchorArraySpillTracking()
    {
        var engine = Engine();

        var workbookA = new Workbook("A");
        var sheetA = workbookA.AddSheet("Sheet1");
        var anchor = new CellAddress(sheetA.Id, 1, 1); // A1
        var dependent = new CellAddress(sheetA.Id, 1, 4); // D1

        sheetA.SetFormula(anchor, "=SEQUENCE(3)");
        sheetA.SetFormula(dependent, "=SUM(A1#:B5)");
        engine.RebuildFormulaDependencies(workbookA);
        engine.Recalculate(workbookA, [anchor, dependent]);

        // Real product entry point (RegisterFormulaDependencies via RebuildFormulaDependencies/
        // Recalculate) must have populated both directions of the ANCHORARRAY spill-union tracking.
        engine.AnchorArraySpillDependentsCountForTests.Should().Be(1,
            "A1's forward-map entry (its dependent set containing D1) must be registered");
        engine.AnchorArraySpillDependentAnchorsCountForTests.Should().Be(1,
            "D1's reverse-map entry (its anchor list containing A1) must be registered");

        // A second, unrelated live workbook must survive A's retirement untouched.
        var workbookB = new Workbook("B");
        var sheetB = workbookB.AddSheet("Sheet1");
        var anchorB = new CellAddress(sheetB.Id, 1, 1);
        var dependentB = new CellAddress(sheetB.Id, 1, 4);
        sheetB.SetFormula(anchorB, "=SEQUENCE(2)");
        sheetB.SetFormula(dependentB, "=SUM(A1#:B5)");
        engine.RebuildFormulaDependencies(workbookB);
        engine.Recalculate(workbookB, [anchorB, dependentB]);

        engine.AnchorArraySpillDependentsCountForTests.Should().Be(2);
        engine.AnchorArraySpillDependentAnchorsCountForTests.Should().Be(2);

        // Close/retire A (no sibling window shares it).
        engine.RetireWorkbook(workbookA);

        engine.AnchorArraySpillDependentsCountForTests.Should().Be(1,
            "retiring A must purge its A1 anchor forward-map entry, leaving only B's");
        engine.AnchorArraySpillDependentAnchorsCountForTests.Should().Be(1,
            "retiring A must purge its D1 dependent reverse-map entry, leaving only B's");

        // B's own live ANCHORARRAY tracking must still function correctly after A's retirement.
        sheetB.SetFormula(anchorB, "=SEQUENCE(10)");
        engine.Recalculate(workbookB, [anchorB]);
        sheetB.GetValue(1, 4).Should().Be(new NumberValue(55),
            "B's dependent must still track its own anchor's live spill extent after an unrelated workbook's retire");
    }

    [Fact]
    public void RetireWorkbook_RepeatedOpenCloseCycleWithAnchorArrayFormulas_DoesNotAccumulate()
    {
        // Sibling/no-regression case, mirroring R71_RetireWorkbookTests' repeated-cycle shape:
        // repeatedly opening a workbook that uses ANCHORARRAY spill-union syntax, recalculating it,
        // then retiring it before opening the next must not leave a growing pile of stale anchor/
        // dependent entries in the shared engine.
        var engine = Engine();

        Workbook? previous = null;
        for (var i = 0; i < 5; i++)
        {
            var workbook = new Workbook($"Book{i}");
            var sheet = workbook.AddSheet("Sheet1");
            var anchor = new CellAddress(sheet.Id, 1, 1);
            var dependent = new CellAddress(sheet.Id, 1, 4);
            sheet.SetFormula(anchor, "=SEQUENCE(3)");
            sheet.SetFormula(dependent, "=SUM(A1#:B5)");
            engine.RebuildFormulaDependencies(workbook);
            engine.Recalculate(workbook, [anchor, dependent]);

            if (previous is not null)
                engine.RetireWorkbook(previous);

            previous = workbook;
        }

        engine.AnchorArraySpillDependentsCountForTests.Should().Be(1,
            "only the final workbook's anchor forward-map entry should remain after four intervening retires");
        engine.AnchorArraySpillDependentAnchorsCountForTests.Should().Be(1,
            "only the final workbook's dependent reverse-map entry should remain after four intervening retires");
    }
}
