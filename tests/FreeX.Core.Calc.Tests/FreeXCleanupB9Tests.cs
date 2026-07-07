using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression tests for cleanup batch group 9 (RecalcEngine findings):
///  - P73: a #SPILL! anchor must re-spill as soon as the cell(s) blocking it are cleared, even
///    though the dependency graph has no edge from the blocking cell back to the anchor (the
///    anchor's formula never referenced it). Recalculating only the cleared blocking cell must be
///    enough to un-stick the anchor, matching Excel's immediate re-spill behaviour.
///  - P102: the dependency graph must register a formula cell's references against whichever named
///    definition eval actually prefers (sheet-scoped named FORMULA takes precedence over a
///    same-named workbook-global named RANGE, per Excel §18.2.6 — scope wins over kind). Before the
///    fix, CollectReferences's NamedRangeNode case called Workbook.TryGetNamedRange first, which
///    silently falls back to the global range when no scoped RANGE exists (it has no notion of a
///    scoped FORMULA), so the graph recorded a dependency on the global range's cells instead of the
///    scoped formula's real inputs.
/// </summary>
public class FreeXCleanupB9Tests
{
    private static (RecalcEngine engine, Workbook wb) MakeEngine()
    {
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);
        var wb = new Workbook();
        return (engine, wb);
    }

    // ── P73: stale #SPILL! must clear when only the blocking cell is recalculated ──────────────

    [Fact]
    public void Recalc_ClearingOnlyTheBlockingCell_UnsticksStaleSpillAnchor()
    {
        var (engine, wb) = MakeEngine();
        var sheet = wb.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 1, 1);
        var blocker = new CellAddress(sheet.Id, 2, 1);

        sheet.SetCell(blocker, new NumberValue(99));
        sheet.SetFormula(anchor, "SEQUENCE(3)");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [anchor]);

        // Sanity: the anchor is indeed blocked, exactly like Excel would show #SPILL!.
        sheet.GetValue(1, 1).Should().Be(ErrorValue.Spill);

        // User deletes the blocking cell. Crucially, we recalculate ONLY the blocker here -- the
        // anchor's formula (SEQUENCE(3)) has no reference to the blocker, so the dependency graph
        // has no edge that would otherwise dirty the anchor. Excel re-spills immediately regardless.
        sheet.ClearCell(blocker);
        engine.Recalculate(wb, [blocker]);

        sheet.GetValue(1, 1).Should().Be(new NumberValue(1));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(2));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(3));
    }

    [Fact]
    public void Recalc_ClearingUnrelatedCell_LeavesStillBlockedSpillAnchorAlone()
    {
        var (engine, wb) = MakeEngine();
        var sheet = wb.AddSheet("Sheet1");
        var anchor = new CellAddress(sheet.Id, 1, 1);
        var blocker = new CellAddress(sheet.Id, 2, 1);
        var unrelated = new CellAddress(sheet.Id, 10, 10);

        sheet.SetCell(blocker, new NumberValue(99));
        sheet.SetCell(unrelated, new NumberValue(1));
        sheet.SetFormula(anchor, "SEQUENCE(3)");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [anchor]);
        sheet.GetValue(1, 1).Should().Be(ErrorValue.Spill);

        // Editing a cell that has nothing to do with the blocked anchor must not force a retry that
        // spuriously "succeeds" -- the blocker is still there, so the anchor must remain #SPILL!.
        sheet.SetCell(unrelated, new NumberValue(2));
        engine.Recalculate(wb, [unrelated]);

        sheet.GetValue(1, 1).Should().Be(ErrorValue.Spill);
        sheet.GetValue(2, 1).Should().Be(new NumberValue(99));
    }

    // ── P102: dependency-graph registration must follow the same scope-first precedence as eval ──

    [Fact]
    public void Recalc_EditingShadowedSheetScopedFormulaInput_DirtiesDependentCell()
    {
        var (engine, wb) = MakeEngine();
        var sheet = wb.AddSheet("Sheet1");

        // Workbook-global named range Foo = Sheet1!$A$1.
        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new NumberValue(1000));
        wb.DefineNamedRange("Foo", new GridRange(a1, a1));

        // Sheet-scoped named FORMULA Foo = $B$1*2 on Sheet1 -- shadows the global range on this sheet.
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(b1, new NumberValue(5));
        wb.DefineNamedFormula("Foo", "$B$1*2", sheet.Id);

        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetFormula(c1, "SUM(Foo)");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [c1]);

        // First calc must use the scope-first-wins-over-kind rule: the scoped FORMULA (B1*2), not
        // the shadowed global RANGE (A1).
        sheet.GetValue(1, 3).Should().Be(new NumberValue(10));

        // Edit B1 (the scoped formula's real input). Before the fix, CollectReferences registered
        // A1 (the global range's cell) as C1's only dependency, so this edit would never dirty C1
        // and it would keep showing the stale value 10 forever.
        sheet.SetCell(b1, new NumberValue(7));
        engine.Recalculate(wb, [b1]);

        sheet.GetValue(1, 3).Should().Be(new NumberValue(14));
    }

    [Fact]
    public void Recalc_EditingShadowedGlobalRangeCell_DoesNotRegisterCellAsDependent()
    {
        var (engine, wb) = MakeEngine();
        var sheet = wb.AddSheet("Sheet1");

        var a1 = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(a1, new NumberValue(1000));
        wb.DefineNamedRange("Foo", new GridRange(a1, a1));

        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(b1, new NumberValue(5));
        wb.DefineNamedFormula("Foo", "$B$1*2", sheet.Id);

        var c1 = new CellAddress(sheet.Id, 1, 3);
        sheet.SetFormula(c1, "SUM(Foo)");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [c1]);
        sheet.GetValue(1, 3).Should().Be(new NumberValue(10));

        // A1 belongs to the shadowed global range, which the scoped formula never reads. Editing it
        // must not even schedule C1 for recalculation -- if the graph still (wrongly) treats A1 as
        // C1's precedent, this recalc pass would report C1 as recalculated even though its value
        // happens to come out the same (the evaluator ignores the wrongly-registered edge and
        // resolves the scope-first formula correctly on every run). Asserting on the recalc report,
        // not just the final value, is what actually distinguishes the bug from the fix.
        sheet.SetCell(a1, new NumberValue(4242));
        var report = engine.Recalculate(wb, [a1]);

        report.RecalculatedCells.Should().NotContain(c1);
        sheet.GetValue(1, 3).Should().Be(new NumberValue(10));
    }
}
