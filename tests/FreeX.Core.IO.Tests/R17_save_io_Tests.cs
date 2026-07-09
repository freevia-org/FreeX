using System.IO;
using FluentAssertions;
using FreeX.Core.Calc;
using FreeX.Core.Formula;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Round-trip regression tests for round-17 findings in XlsxFileAdapter.Save.cs's full
/// (ClosedXML) save path.
/// </summary>
public sealed class R17_save_io_Tests
{
    /// <summary>
    /// R17-shared-formula-array-io-1: a 1x1 dynamic-array result (UNIQUE over an all-equal input,
    /// which collapses to a single unique value) must still be written with the array-formula
    /// marker (t="array") rather than as a plain &lt;f&gt; formula. Pre-fix, the save's
    /// "hasExtent &amp;&amp; spillRows*spillCols &gt; 1" gate excludes exactly-1x1 extents, so the
    /// formula loses its array identity on reload (demotes to ArrayMode.Implicit) and never
    /// re-spills after an edit widens the result.
    /// </summary>
    [Fact]
    public void OneByOneDynamicArray_RoundTripsAsArrayFormula_AndReSpillsAfterEdit()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("R17ArrayIo1");
        var sheet = workbook.AddSheet("Data");

        // A1:A3 all equal -> UNIQUE(A1:A3) collapses to a single 1x1 result.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(5));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(5));

        var anchor = new CellAddress(sheet.Id, 1, 3);
        sheet.SetFormula(anchor, "UNIQUE(A1:A3)");

        var engine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        engine.RecalculateAllFormulas(workbook);

        // Sanity: the pre-save model really is a registered 1x1 dynamic-array spill (not just a
        // plain scalar formula), so the save-path bug this test targets actually applies.
        sheet.GetValue(1, 3).Should().Be(new NumberValue(5));
        sheet.TryGetSpillExtent(anchor, out var preSaveRows, out var preSaveCols).Should().BeTrue();
        preSaveRows.Should().Be(1u);
        preSaveCols.Should().Be(1u);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var reloaded = adapter.Load(saved);
        var reloadedSheet = reloaded.GetSheetAt(0);
        var reloadedAnchorCell = reloadedSheet.GetCell(1, 3)!;

        reloadedAnchorCell.FormulaText.Should().Be("UNIQUE(A1:A3)");
        reloadedAnchorCell.ArrayMode.Should().Be(FormulaArrayMode.Dynamic,
            "a 1x1 dynamic-array result must keep its array-formula identity across a save/reload " +
            "round-trip, not demote to legacy Implicit mode");

        // Now widen the input so UNIQUE produces two distinct values -> the reloaded formula must
        // still be able to re-spill into a second cell.
        reloadedSheet.SetCell(new CellAddress(reloadedSheet.Id, 2, 1), new NumberValue(9));
        var reloadEngine = new RecalcEngine(new DependencyGraph(), new FormulaEvaluator());
        reloadEngine.RecalculateAllFormulas(reloaded);

        reloadedSheet.GetValue(1, 3).Should().Be(new NumberValue(5));
        reloadedSheet.GetValue(2, 3).Should().Be(new NumberValue(9),
            "after widening the input the reloaded dynamic-array formula must re-spill into a " +
            "second cell instead of staying pinned to a single implicit-intersection scalar");
    }

    /// <summary>
    /// R17-pagesetup-multiregion-1: a null FitToPagesTall (== "automatic/unbounded": fit all
    /// columns onto one page, as many pages tall as needed) must survive a save/reload round-trip
    /// as null. Pre-fix, "scaleToFit.FitToPagesTall ?? 1" collapses the null axis into an explicit
    /// 1-page cap, which would wrongly shrink a tall report onto a single page after save.
    /// </summary>
    [Fact]
    public void FitToPagesWideOnly_RoundTripsWithTallAxisStillAutomatic()
    {
        var adapter = new XlsxFileAdapter();
        var workbook = new Workbook("R17PageSetup1");
        var sheet = workbook.AddSheet("Data");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1));
        sheet.ScaleToFit = new WorksheetScaleToFit(null, 1, null);

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;

        var reloaded = adapter.Load(saved);
        var reloadedSheet = reloaded.GetSheetAt(0);

        reloadedSheet.ScaleToFit.FitToPagesWide.Should().Be(1);
        reloadedSheet.ScaleToFit.FitToPagesTall.Should().BeNull(
            "a null FitToPagesTall means \"automatic/unbounded\" and must not be coerced into an " +
            "explicit 1-page cap on save");
    }
}
