using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R116-commands-pivot-refresh-scope: R115 made <c>PivotTableRefreshService.ReconcileCacheFields</c>
/// (called from <c>Refresh</c>) re-derive EVERY surviving cache field's SharedItems from a full
/// row-by-row scan of that field's entire source column, unconditionally, on every call to
/// <c>Refresh</c> -- but <c>Refresh</c> is not only the F5/"Refresh PivotTable" entry point
/// (<see cref="RefreshPivotTableCommand"/>); it is also the choke point every OTHER pivot-mutating
/// command funnels through, including a single slicer selection click
/// (<see cref="SetSlicerSelectionCommand"/>, fired on every filter-button click). None of those other
/// commands touch a single source cell, so re-scanning every field's entire source column on each of
/// them added an O(fieldCount * rowCount) cost to what must be an instant, frequent, interactive UI
/// action. Real Excel does not re-scan the underlying range for a slicer click on an already-refreshed
/// pivot -- only a genuine refresh (F5/Refresh-All) re-derives shared items.
///
/// The anchor test drives the real product entry point a slicer click actually reaches
/// (<see cref="SetSlicerSelectionCommand"/>) and proves it no longer rescans/reconciles cache.Fields'
/// SharedItems. The sibling proves the genuine refresh entry point (<see cref="RefreshPivotTableCommand"/>)
/// still does -- this is the exact scenario <c>R115_PivotRefreshSharedItemsTests</c> already covers, kept
/// here as an explicit no-regression check against this exact defect's suggested fix (gating instead of
/// removing the reconciliation).
/// </summary>
public sealed class R116_PivotSlicerClickSkipsCacheRescanTests
{
    private static void SeedData(Sheet sheet)
    {
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("B"));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));
    }

    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

    /// <summary>
    /// THE anchor test: a single slicer selection change (exactly what a user clicking a slicer's
    /// filter button reaches -- <see cref="SetSlicerSelectionCommand"/>) must NOT re-derive the cache
    /// field's SharedItems from a fresh scan of the live source column. A brand-new distinct value
    /// introduced into the source data must stay absent from SharedItems after a slicer-only action --
    /// only an explicit refresh (covered by the sibling test below) picks it up.
    /// </summary>
    [Fact]
    public void SetSlicerSelectionCommand_DoesNotRescanSourceColumnsForCacheSharedItems()
    {
        var workbook = new Workbook("R116SlicerNoRescan");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new TestCommandContext(workbook);

        var addPivot = new AddPivotTableCommand(
            sheet.Id,
            Range(sheet, "A1", "B4"),
            Range(sheet, "D3", "E6"),
            "PivotTable1",
            rowFieldIndexes: [0],
            dataFieldIndexes: [1]);
        addPivot.Apply(ctx).Success.Should().BeTrue();

        var cache = workbook.PivotCaches.Should().ContainSingle().Subject;
        var categoryFieldBefore = cache.Fields.Should().ContainSingle(field => field.Name == "Category").Subject;
        categoryFieldBefore.SharedItems!.Should().Equal("A", "B");

        var addSlicer = new AddSlicerCommand("Category Slicer", "PivotTable1", "Category");
        addSlicer.Apply(ctx).Success.Should().BeTrue();

        // Introduce a brand-new distinct value into the EXISTING range -- if a slicer click still
        // triggered the full per-field reconciliation (the pre-fix bug), this would show up in
        // SharedItems immediately, exactly like R115_PivotRefreshSharedItemsTests proves an actual
        // refresh does.
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("C"));

        // The actual product entry point a slicer button click reaches: select a subset of the
        // EXISTING items (mirrors a real user filter click, not a no-op).
        var setSelection = new SetSlicerSelectionCommand("Category Slicer", ["A"]);
        setSelection.Apply(ctx).Success.Should().BeTrue();

        var categoryFieldAfter = cache.Fields.Should().ContainSingle(field => field.Name == "Category").Subject;
        // Bug (before fix): the slicer click's Refresh() call unconditionally ran
        // ReconcileCacheFields, which rescanned column A and picked up "C" into SharedItems even
        // though nothing about the SOURCE data was ever asked to be re-examined by this action. A
        // slicer-selection click must not rescan the pivot's source columns for cache SharedItems --
        // only a genuine refresh does.
        categoryFieldAfter.SharedItems!.Should().Equal("A", "B");

        // The slicer action itself must still have done its actual job: the pivot output is filtered.
        sheet.GetCell(Addr(sheet, "D4"))!.Value.Should().Be(new TextValue("A"));
    }

    /// <summary>
    /// No-regression sibling: the genuine refresh entry point (<see cref="RefreshPivotTableCommand"/>,
    /// what F5/Refresh-All actually constructs) must still pick up a new distinct value discovered in
    /// the live source column -- proving the fix GATES the reconciliation rather than removing it
    /// outright (which would silently resurrect the R115 staleness defect for the one action that is
    /// supposed to re-derive shared items).
    /// </summary>
    [Fact]
    public void RefreshPivotTableCommand_StillRescansSourceColumnsForCacheSharedItems()
    {
        var workbook = new Workbook("R116RefreshStillRescans");
        var sheet = workbook.AddSheet("Data");
        SeedData(sheet);
        var ctx = new TestCommandContext(workbook);

        var addPivot = new AddPivotTableCommand(
            sheet.Id,
            Range(sheet, "A1", "B4"),
            Range(sheet, "D3", "E6"),
            "PivotTable1",
            rowFieldIndexes: [0],
            dataFieldIndexes: [1]);
        addPivot.Apply(ctx).Success.Should().BeTrue();

        var cache = workbook.PivotCaches.Should().ContainSingle().Subject;

        sheet.SetCell(Addr(sheet, "A3"), new TextValue("C"));

        var refresh = new RefreshPivotTableCommand(sheet.Id, "PivotTable1");
        refresh.Apply(ctx).Success.Should().BeTrue();

        var categoryFieldAfter = cache.Fields.Should().ContainSingle(field => field.Name == "Category").Subject;
        categoryFieldAfter.SharedItems!.Should().Contain("C",
            "RefreshPivotTableCommand is the genuine refresh entry point and must still re-derive SharedItems from the live source");
    }
}
