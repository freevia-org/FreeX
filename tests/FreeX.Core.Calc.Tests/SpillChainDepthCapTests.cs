using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// R112: <see cref="RecalcEngine.ResolveSpillTargetDependentsFixpoint"/> re-evaluates formula
/// cells that read another cell's dynamic-array spill target by plain address (they have no
/// dependency-graph edge back to the spilling anchor, since the target is not itself a formula
/// cell) via a bounded fixpoint loop. Each pass discovers exactly one further "generation" of such
/// readers, so a chain of N dependent spilling formulas -- each one's SEQUENCE start argument
/// reading the previous one's non-anchor spill member by plain address, rather than the
/// #/ANCHORARRAY syntax that gets a real dependency-graph edge and converges in one pass --
/// requires N-1 fixpoint passes to fully settle. Before the fix, that loop was hard-capped at a
/// fixed 64 passes (MaxSpillDependentPasses), so a chain deeper than ~65 generations left its tail
/// stale after a single Recalculate()/F9 call -- exactly the defect under test here. The fix raises
/// the ceiling to the workbook's total formula-cell count when that is larger, which is a bound
/// that can never be smaller than the deepest possible chain of dependent spilling formulas (a
/// chain link needs a formula cell to exist).
/// </summary>
public class SpillChainDepthCapTests
{
    private static (RecalcEngine engine, Workbook wb) MakeEngine()
    {
        var graph = new DependencyGraph();
        var evaluator = new FormulaEvaluator();
        var engine = new RecalcEngine(graph, evaluator);
        var wb = new Workbook();
        return (engine, wb);
    }

    /// <summary>
    /// Builds a chain of <paramref name="levelCount"/> dependent spilling formulas, one per column
    /// starting at column 1: level 1's formula is "=SEQUENCE(2,1,1,0)" (spills the value 1 into its
    /// own column, rows 1-2); level k (k &gt;= 2) is "=SEQUENCE(2,1,&lt;prevCol&gt;2,0)", reading
    /// level (k-1)'s row-2 spill member by plain address (not "#") and repeating that value down its
    /// own rows 1-2. Returns the anchor address of the first level (the only cell whose formula must
    /// be directly recalculated -- everything past level 1 is discovered solely by the spill-target
    /// follow-up fixpoint) and the anchor address of the last level (whose settled value, if the
    /// chain fully converged, equals 1).
    ///
    /// Formulas are inserted in descending level order (level <paramref name="levelCount"/> first,
    /// level 1 last) so the test does not rely on ascending insertion order to happen to match the
    /// dependency chain's natural evaluation order.
    /// </summary>
    private static (CellAddress firstAnchor, CellAddress lastAnchor) BuildSpillChain(Sheet sheet, int levelCount)
    {
        CellAddress firstAnchor = default;
        CellAddress lastAnchor = default;

        for (var level = levelCount; level >= 1; level--)
        {
            var anchor = new CellAddress(sheet.Id, 1, (uint)level);
            var formula = level == 1
                ? "=SEQUENCE(2,1,1,0)"
                : $"=SEQUENCE(2,1,{new CellAddress(sheet.Id, 2, (uint)(level - 1)).ToA1()},0)";
            sheet.SetFormula(anchor, formula);

            if (level == 1)
                firstAnchor = anchor;
            if (level == levelCount)
                lastAnchor = anchor;
        }

        return (firstAnchor, lastAnchor);
    }

    [Fact]
    public void R112_DeepSpillChain_ConvergesBeyond64GenerationsInSinglePass()
    {
        // 70 levels: level 70 requires 69 fixpoint passes to be discovered and evaluated (one new
        // generation of plain-address spill-target readers per pass), which exceeds the old fixed
        // cap of 64 passes. A single Recalculate() call (triggered by recalculating only the first
        // anchor, exactly as an edit to its own input would) must still converge the entire chain.
        const int levelCount = 70;
        var (engine, wb) = MakeEngine();
        var sheet = wb.AddSheet("Sheet1");

        var (firstAnchor, lastAnchor) = BuildSpillChain(sheet, levelCount);
        engine.RebuildFormulaDependencies(wb);

        engine.Recalculate(wb, [firstAnchor]);

        sheet.GetValue(lastAnchor.Row, lastAnchor.Col).Should().Be(new NumberValue(1),
            $"a chain of {levelCount} dependent spilling formulas must fully converge in a single Recalculate() call, matching Excel's single-pass recalculation, even though each link is only discovered one generation per fixpoint pass");
    }

    [Fact]
    public void R112_FullWorkbookRecalculate_AlsoConvergesDeepSpillChain()
    {
        // Sibling coverage of the above via the RecalculateAllFormulas (F9) entry point, which
        // routes through the identical ResolveSpillTargetDependentsFixpoint mechanism. This
        // particular construction (every chain link is itself passed to Recalculate as a direct
        // root, since F9 seeds every formula cell in the workbook) happens to already converge
        // even under the old fixed 64-pass cap for this depth, unlike the targeted single-root
        // edit case above -- so this test does not by itself prove the pre-fix defect for F9, but
        // it does confirm the fix does not regress the F9 entry point for a deep chain.
        const int levelCount = 70;
        var (engine, wb) = MakeEngine();
        var sheet = wb.AddSheet("Sheet1");

        var (_, lastAnchor) = BuildSpillChain(sheet, levelCount);

        engine.RecalculateAllFormulas(wb);

        sheet.GetValue(lastAnchor.Row, lastAnchor.Col).Should().Be(new NumberValue(1),
            "a full recalculation (F9) must converge a deep spill-dependent chain just as a targeted Recalculate() does");
    }

    [Fact]
    public void R112_ShallowSpillChain_StillConvergesInSinglePass_NoRegression()
    {
        // No-regression sibling: an ordinary, well-within-the-old-cap chain (5 levels) must keep
        // converging in one pass exactly as before -- the fix must not change behavior for the
        // overwhelmingly common shallow case, only raise the ceiling for pathologically deep ones.
        const int levelCount = 5;
        var (engine, wb) = MakeEngine();
        var sheet = wb.AddSheet("Sheet1");

        var (firstAnchor, lastAnchor) = BuildSpillChain(sheet, levelCount);
        engine.RebuildFormulaDependencies(wb);

        engine.Recalculate(wb, [firstAnchor]);

        sheet.GetValue(lastAnchor.Row, lastAnchor.Col).Should().Be(new NumberValue(1),
            "a short, ordinary spill-dependent chain must still converge in a single recalculation pass");
    }
}
