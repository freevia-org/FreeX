using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Regression tests for two RecalcEngine follow-up-registration bugs:
///  - G13: a same-position paste (zero row/col delta) clones the source cell's cached AST by
///    reference; the pasted cell must still get its own dependency-graph registration.
///  - G28: the spill-target follow-up recalculation must converge across chained/nested spill
///    dependents, not stop after exactly one extra level.
/// </summary>
public class PasteAndSpillFollowUpRecalcTests
{
    private static (RecalcEngine engine, Workbook wb) MakeEngine()
    {
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);
        var wb = new Workbook();
        return (engine, wb);
    }

    [Fact]
    public void SamePositionPasteAcrossSheets_RegistersPastedCellsOwnDependencies()
    {
        var (engine, wb) = MakeEngine();
        var sheet1 = wb.AddSheet("Sheet1");
        var sheet2 = wb.AddSheet("Sheet2");

        // Sheet1!A1 = =B1, Sheet1!B1 = 5. Establish the source formula's cached AST + graph edges.
        var sheet1A1 = new CellAddress(sheet1.Id, 1, 1);
        var sheet1B1 = new CellAddress(sheet1.Id, 1, 2);
        sheet1.SetCell(sheet1B1, new NumberValue(5));
        sheet1.SetFormula(sheet1A1, "=B1");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [sheet1A1]);
        sheet1.GetValue(1, 1).Should().Be(new NumberValue(5));

        // Simulate a same-position (zero row/col delta) paste of Sheet1!A1 onto Sheet2!A1: this is
        // exactly what PasteCommandCellFactory.BuildFormulaOrValueCell/BuildAllCell do when
        // rowDelta==0 && colDelta==0 — clone the source cell (sharing its CachedAst by reference)
        // and write it directly via Sheet.SetCell, without ever calling RegisterFormulaDependencies.
        var sourceCell = sheet1.GetCell(sheet1A1)!;
        // FormulaNode is the abstract AST base — the concrete root here is e.g. CellRefNode.
        sourceCell.CachedAst.Should().BeAssignableTo<FormulaNode>("the source formula must already be cached before the paste");

        var sheet2A1 = new CellAddress(sheet2.Id, 1, 1);
        var sheet2B1 = new CellAddress(sheet2.Id, 1, 2);
        sheet2.SetCell(sheet2B1, new NumberValue(42));
        sheet2.SetCell(sheet2A1, sourceCell.Clone());

        var pastedCell = sheet2.GetCell(sheet2A1)!;
        ReferenceEquals(pastedCell.CachedAst, sourceCell.CachedAst).Should()
            .BeTrue("Cell.Clone() copies CachedAst by reference");

        // Recalculate exactly as WorkbookCellEditService.RecalculateIfAutomatic would for the
        // paste's AffectedCells (just the destination address).
        engine.Recalculate(wb, [sheet2A1]);
        sheet2.GetValue(1, 1).Should().Be(new NumberValue(42), "the pasted formula evaluates =B1 against Sheet2 at eval time");

        // The crux of the bug: editing Sheet2!B1 afterwards must invalidate Sheet2!A1. Before the
        // fix, Sheet2!A1 had no dependency-graph edge to Sheet2!B1 (the guard `cell.CachedAst is
        // FormulaNode` short-circuited registration because the AST reference was already non-null),
        // so this recalculation call would silently leave Sheet2!A1 stale at 42.
        sheet2.SetCell(sheet2B1, new NumberValue(100));
        engine.Recalculate(wb, [sheet2B1]);

        sheet2.GetValue(1, 1).Should().Be(new NumberValue(100),
            "Sheet2!A1 must be registered as a dependent of Sheet2!B1 even though its cached AST was inherited from the paste source");
    }

    [Fact]
    public void SamePositionPasteOnSameSheet_RegistersPastedCellsOwnDependencies()
    {
        // Same bug, but the more common single-sheet flavor: pasting a formula cell onto a
        // different row/col so rowDelta/colDelta are nonzero for the *destination*, but the
        // *source* cell being read is unaffected — verifies the same-address registration path
        // isn't accidentally specific to the cross-sheet case.
        var (engine, wb) = MakeEngine();
        var sheet = wb.AddSheet("Sheet1");

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(b1, new NumberValue(7));
        sheet.SetFormula(a1, "=B1");
        engine.RebuildFormulaDependencies(wb);
        engine.Recalculate(wb, [a1]);

        var sourceCell = sheet.GetCell(a1)!;

        // Directly place a same-position clone at a *different* cell (D1) that also reads B1,
        // bypassing FormulaRewriter entirely (as the zero-delta paste path does for the identical
        // relative reference "=B1" when it happens to land on a cell in the same row as another
        // read of B1 — the key point under test is that Sheet.SetCell + Clone() alone never
        // registers dependencies, regardless of which address receives the clone).
        var d1 = new CellAddress(sheet.Id, 1, 4);
        sheet.SetCell(d1, sourceCell.Clone());
        engine.Recalculate(wb, [d1]);
        sheet.GetValue(1, 4).Should().Be(new NumberValue(7));

        sheet.SetCell(b1, new NumberValue(77));
        engine.Recalculate(wb, [b1]);

        sheet.GetValue(1, 4).Should().Be(new NumberValue(77),
            "the cloned formula cell at D1 must be registered as a dependent of B1");
    }

    [Fact]
    public void SpillFollowUp_ConvergesAcrossChainedSpillDependents()
    {
        // A1 spills into A1:A3 (dynamic array). B1 = A2:A3 (a range read of A1's non-anchor spill
        // targets, so B1 is only discovered by the first CollectSpillTargetDependentFormulaCells
        // pass, not by the initial GetRecalcOrder plan). C1 = B2 (an exact reference to one of B1's
        // *own* future spill targets). Recalculating A1 must ripple all the way through to C1 in a
        // single Recalculate() call, matching Excel's single-pass-feeling recalculation.
        var (engine, wb) = MakeEngine();
        var sheet = wb.AddSheet("Sheet1");

        var a1 = new CellAddress(sheet.Id, 1, 1);
        var b1 = new CellAddress(sheet.Id, 1, 2);
        var c1 = new CellAddress(sheet.Id, 1, 3);

        sheet.SetFormula(a1, "=SEQUENCE(3)");
        sheet.SetFormula(b1, "=A2:A3");
        sheet.SetFormula(c1, "=B2");
        engine.RebuildFormulaDependencies(wb);

        engine.Recalculate(wb, [a1]);

        // A1 spills 1,2,3 down A1:A3.
        sheet.GetValue(1, 1).Should().Be(new NumberValue(1));
        sheet.GetValue(2, 1).Should().Be(new NumberValue(2));
        sheet.GetValue(3, 1).Should().Be(new NumberValue(3));

        // B1 reads A2:A3 = {2,3} and itself spills down B1:B2.
        sheet.GetValue(1, 2).Should().Be(new NumberValue(2));
        sheet.GetValue(2, 2).Should().Be(new NumberValue(3));

        // C1 reads B2, which only received its value as a *second-order* spill-dependent effect
        // (B1 itself was only re-evaluated because it read A1's spill targets). Before the fix,
        // the follow-up pass was capped at exactly one recursion level (resolveSpillDependents is
        // hard-coded false on the recursive call), so C1 was never discovered and stayed stale.
        sheet.GetValue(1, 3).Should().Be(new NumberValue(3),
            "C1 = B2 must converge to B1's freshly-spilled value in the same recalculation pass");
    }

    [Fact]
    public void SpillFollowUp_ThreeLevelChain_StillConverges()
    {
        // Extend the chain one level further (A -> B -> C -> D) to confirm the fixpoint loop
        // isn't merely bumped from one extra level to two, but genuinely iterates until stable.
        var (engine, wb) = MakeEngine();
        var sheet = wb.AddSheet("Sheet1");

        var a1 = new CellAddress(sheet.Id, 1, 1); // SEQUENCE(4) -> A1:A4 = 1,2,3,4
        var b1 = new CellAddress(sheet.Id, 1, 2); // =A2:A4       -> B1:B3 = 2,3,4
        var c1 = new CellAddress(sheet.Id, 1, 3); // =B2:B3       -> C1:C2 = 3,4
        var d1 = new CellAddress(sheet.Id, 1, 4); // =C2          -> 4

        sheet.SetFormula(a1, "=SEQUENCE(4)");
        sheet.SetFormula(b1, "=A2:A4");
        sheet.SetFormula(c1, "=B2:B3");
        sheet.SetFormula(d1, "=C2");
        engine.RebuildFormulaDependencies(wb);

        engine.Recalculate(wb, [a1]);

        sheet.GetValue(1, 4).Should().Be(new NumberValue(4),
            "D1 = C2 must converge through the full A -> B -> C -> D spill-dependent chain in one recalculation pass");
    }
}
