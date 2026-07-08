using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

// ── Round-13 fix bucket S8 ────────────────────────────────────────────────────
//
// R13-crosscutting-perf-mem-1 [MED]: BuildConditionalFormatContext caches a CfEvaluationContext
// (including a color-scale's precomputed min/max aggregates) keyed only on
// (SheetId, Sheet.ContentVersion, ConditionalFormats.Version). RecalcEngine updates a formula
// cell's cached value via `cell.Value = result` directly (RecalcEngine.cs), bypassing
// Sheet.SetCell/SetFormula, so Sheet.ContentVersion never bumps when a cross-sheet dependency (or
// a volatile F9 recalc) changes a sheet's own formula-cell values. The next render then reuses the
// STALE cached min/max, producing wrong color-scale shading until an unrelated direct edit finally
// bumps ContentVersion. This test reproduces the exact scenario from the finding: Sheet1!A1:A3 hold
// linking formulas =Sheet2!A1..A3 under a 2-color scale; editing Sheet2 and recalculating must
// refresh Sheet1's cached CF context.
public sealed class FreeXR13S8Tests
{
    private static RecalcEngine Engine() => new(new DependencyGraph(), new FormulaEvaluator());

    private static DisplayCell GetCell(ViewportModel vp, uint row, uint col) =>
        vp.Cells.Single(c => c.Row == row && c.Col == col);

    [Fact]
    public void ColorScale_CrossSheetLinkingFormulas_RebuildsCachedContextAfterRecalc()
    {
        var workbook = new Workbook("test");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");

        // Sheet2 seed values.
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(10));
        sheet2.SetCell(new CellAddress(sheet2.Id, 2, 1), new NumberValue(20));
        sheet2.SetCell(new CellAddress(sheet2.Id, 3, 1), new NumberValue(30));

        // Sheet1!A1:A3 are linking formulas into Sheet2 — exactly the finding's reproduction.
        sheet1.SetFormula(new CellAddress(sheet1.Id, 1, 1), "Sheet2!A1");
        sheet1.SetFormula(new CellAddress(sheet1.Id, 2, 1), "Sheet2!A2");
        sheet1.SetFormula(new CellAddress(sheet1.Id, 3, 1), "Sheet2!A3");

        // Default 2-color scale: MinThresholdType/MaxThresholdType default to Min/Max, so the
        // aggregate cache's range min/max drive the interpolation.
        sheet1.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 3, 1)),
            Priority = 1,
            RuleType = CfRuleType.ColorScale
        });

        var engine = Engine();
        engine.RecalculateAllFormulas(workbook);

        var svc = new ViewportService();
        var request = new ViewportRequest(1, 1, 500, 500);

        var vp1 = svc.GetViewport(workbook, sheet1.Id, request);

        // A1 = 10 is the min of {10,20,30} -> MinColor (green); A3 = 30 is the max -> MaxColor (red).
        GetCell(vp1, 1, 1).Style!.FillColor.Should().Be(new CellColor(99, 190, 123), "A1 holds the smallest value");
        GetCell(vp1, 3, 1).Style!.FillColor.Should().Be(new CellColor(248, 105, 107), "A3 holds the largest value");

        // Change Sheet2's values to a completely different range and recalculate. Sheet1's own
        // cells are updated purely by RecalcEngine writing `cell.Value = result` for the
        // cross-sheet dependency -- Sheet1 never goes through SetCell/SetFormula itself.
        sheet2.SetCell(new CellAddress(sheet2.Id, 1, 1), new NumberValue(1000));
        sheet2.SetCell(new CellAddress(sheet2.Id, 2, 1), new NumberValue(2000));
        sheet2.SetCell(new CellAddress(sheet2.Id, 3, 1), new NumberValue(3000));
        engine.RecalculateAllFormulas(workbook);

        sheet1.GetValue(1, 1).Should().Be(new NumberValue(1000));
        sheet1.GetValue(3, 1).Should().Be(new NumberValue(3000));

        var vp2 = svc.GetViewport(workbook, sheet1.Id, request);

        // Post-fix: A1 (now the smallest of the refreshed values, 1000) must be MinColor again.
        // Pre-fix, the stale cached context (min=10, max=30) would clamp 1000 far above the old
        // max and wrongly render it as MaxColor -- the exact symptom the finding describes.
        GetCell(vp2, 1, 1).Style!.FillColor.Should().Be(new CellColor(99, 190, 123),
            "the cached CF context must be rebuilt against the fresh cross-sheet values (min=1000), not the stale min=10/max=30");
        GetCell(vp2, 3, 1).Style!.FillColor.Should().Be(new CellColor(248, 105, 107),
            "A3 remains the largest of the refreshed values (max=3000)");
    }

    [Fact]
    public void ColorScale_SameSheetRecalcWithNoInterveningEdit_RebuildsCachedContext()
    {
        // Same underlying bug, same-sheet "pure F9" variant: pressing F9 re-evaluates every formula
        // cell via RecalcEngine's direct `cell.Value = result` write even when NOTHING was explicitly
        // edited (no SetCell/SetFormula call at all) between the two recalculation passes. Using a
        // plain (non-volatile) same-sheet formula dependency here is deliberate: it isolates the bug
        // from Sheet.SetCell's own (already-correct) ContentVersion bump -- if the test instead
        // mutated a precedent cell via SetCell to force new values, that SetCell call alone would
        // bump ContentVersion and the cache would correctly invalidate regardless of this fix. Here
        // the recalculated values are identical both times (nothing changed), so only
        // CfContextBuildCount -- not the rendered color, which cannot change -- can distinguish a
        // correct rebuild from a stale-cache hit.
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.SetCell(new CellAddress(sheet.Id, 10, 1), new NumberValue(100));
        sheet.SetFormula(new CellAddress(sheet.Id, 1, 2), "A10*1");
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 2), "A10*2");
        sheet.SetFormula(new CellAddress(sheet.Id, 3, 2), "A10*3");

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 3, 2)),
            Priority = 1,
            RuleType = CfRuleType.ColorScale
        });

        var engine = Engine();
        engine.RecalculateAllFormulas(workbook);

        var svc = new ViewportService();
        var request = new ViewportRequest(1, 1, 500, 500);

        svc.GetViewport(workbook, sheet.Id, request);
        var buildCountAfterFirst = svc.CfContextBuildCount;

        // Re-run the exact same full recalc with zero intervening edits (the RecalcEngine analogue
        // of pressing F9 again with nothing changed). Every formula cell is re-evaluated and its
        // Cell.Value rewritten in place, but no SetCell/SetFormula/SetSpillRange call ever touches
        // the sheet in between, so ContentVersion only advances if RecalcEngine's own notification
        // (this fix) is in place.
        engine.RecalculateAllFormulas(workbook);
        svc.GetViewport(workbook, sheet.Id, request);
        var buildCountAfterSecond = svc.CfContextBuildCount;

        buildCountAfterFirst.Should().Be(1, "the first render must build the CF context");
        buildCountAfterSecond.Should().Be(2,
            "a second full recalc pass rewrites every formula cell's value via RecalcEngine directly; " +
            "the cached CF context must be rebuilt even though no SetCell/SetFormula edit ever occurred");
    }
}
