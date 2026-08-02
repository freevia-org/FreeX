using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R118-commands-pivot-slicer-changesource: r117 fixed "an existing slicer never sees new values" by
/// making <see cref="PivotTableRefreshService.Refresh"/> extend every bound slicer's
/// <see cref="SlicerModel.CacheItems"/> whenever a surviving cache field's
/// <see cref="PivotCacheFieldModel.SharedItems"/> grew -- but that fix only ran behind Refresh's
/// <c>rescanCacheSharedItems</c> gate, which only <see cref="RefreshPivotTableCommand"/> sets to
/// <see langword="true"/>. <see cref="ChangePivotTableSourceCommand"/> independently reconciles
/// cache.Fields itself (via <see cref="PivotCacheFieldFactory.ReconcileFields"/>) BEFORE calling
/// <see cref="PivotTableRefreshService.Refresh"/> with the default <c>rescanCacheSharedItems: false</c>,
/// so a field's SharedItems can legitimately grow when "Change Data Source" points the pivot at a wider
/// range that still contains a same-named column with an extra distinct value -- but the bound slicer's
/// CacheItems never got extended on this second entry point, reproducing the exact bug r117 fixed, just
/// on a different path to the same choke point.
///
/// Pivot creation and the data-source change are driven through the real product entry points:
/// <see cref="AddPivotTableCommand.Apply"/> and <see cref="ChangePivotTableSourceCommand.Apply"/>,
/// followed by <see cref="SlicerItemResolver.ResolveAvailableItems"/> (the real live-UI/render entry
/// point) -- mirroring R117_PivotSlicerGrowsCacheItemsOnRefreshTests's own structure for the sibling
/// RefreshPivotTableCommand path.
/// </summary>
public sealed class R118_ChangeDataSourceGrowsSlicerCacheItemsTests
{
    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

    private static (Workbook Workbook, Sheet Sheet, TestCommandContext Ctx, SlicerModel Slicer) BuildPivotAndSlicer()
    {
        var workbook = new Workbook("R118ChangeDataSourceSlicerGrowth");
        var sheet = workbook.AddSheet("Data");

        // Original source A1:B3 -- Category values A, B only.
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("B"));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));

        // A separate "new" source range with the SAME header names but a genuinely new distinct
        // Category value ("C") -- exactly the ordinary "point the pivot at a wider/different range
        // that happens to still contain a same-named column with an extra distinct value" scenario the
        // finding describes.
        sheet.SetCell(Addr(sheet, "D1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "E1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "D2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "E2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "D3"), new TextValue("B"));
        sheet.SetCell(Addr(sheet, "E3"), new NumberValue(20));
        sheet.SetCell(Addr(sheet, "D4"), new TextValue("C"));
        sheet.SetCell(Addr(sheet, "E4"), new NumberValue(30));

        var ctx = new TestCommandContext(workbook);
        var addPivot = new AddPivotTableCommand(
            sheet.Id,
            Range(sheet, "A1", "B3"),
            Range(sheet, "G3", "H6"),
            "PivotTable1",
            rowFieldIndexes: [0],
            dataFieldIndexes: [1]);
        addPivot.Apply(ctx).Success.Should().BeTrue();

        var cache = workbook.PivotCaches.Should().ContainSingle().Subject;
        var categoryField = cache.Fields.Should().ContainSingle(f => f.Name == "Category").Subject;
        categoryField.SharedItems.Should().Equal("A", "B");

        // Mirrors a slicer loaded from an existing file: the user's selection lives purely as
        // CacheItems[].IsSelected ("A" deselected, "B" selected).
        var slicer = new SlicerModel
        {
            Name = "Category Slicer",
            CacheName = "Slicer_Category",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Category",
            CacheItems = [new SlicerCacheItem(0, false), new SlicerCacheItem(1, true)],
        };
        workbook.Slicers.Add(slicer);

        return (workbook, sheet, ctx, slicer);
    }

    /// <summary>
    /// THE anchor test: "Change Data Source" points the pivot at a range containing a same-named
    /// "Category" column with a brand-new distinct value ("C"). This must make "C" visible through the
    /// real resolver entry point for the EXISTING bound slicer, without disturbing the user's existing
    /// "B"-only selection -- exactly like an ordinary refresh must (R117), because real Excel does not
    /// treat "Change Data Source" as a weaker refresh for slicer purposes.
    /// </summary>
    [Fact]
    public void ChangeDataSource_NewDistinctValueInWiderRange_BecomesVisibleInExistingSlicer_WithoutDisturbingSelection()
    {
        var (workbook, sheet, ctx, slicer) = BuildPivotAndSlicer();

        var changeSource = new ChangePivotTableSourceCommand(sheet.Id, "PivotTable1", Range(sheet, "D1", "E4"));
        changeSource.Apply(ctx).Success.Should().BeTrue();

        var pivotTable = sheet.PivotTables.Single(p => p.Name == "PivotTable1");
        var cache = CommandGuards.FindPivotCache(workbook, pivotTable)!;
        var categoryField = cache.Fields.Should().ContainSingle(f => f.Name == "Category").Subject;
        categoryField.SharedItems.Should().Equal("A", "B", "C");

        // Real live-UI/render entry point.
        var available = SlicerItemResolver.ResolveAvailableItems(slicer, workbook);
        available.Should().Contain("C", "a newly-appeared pivot cache value must become visible in an " +
            "EXISTING slicer after Change Data Source, not stay invisible forever");
        available.Should().BeEquivalentTo(["A", "B", "C"]);

        // The user's pre-existing partial selection must survive completely unchanged. This slicer
        // already has a manual filter applied ("A" deselected) and the field carries no explicit
        // includeNewItemsInFilter=true, so per Excel's default (ECMA-376
        // pivotField/@includeNewItemsInFilter, default false) the brand-new "C" must NOT be
        // auto-included either -- widening an existing manual filter to include a value the user never
        // asked to see is exactly the bug this fix prevents.
        slicer.SelectedItems.Should().Contain("B").And.NotContain("C").And.NotContain("A");
    }

    /// <summary>No-regression sibling: Change Data Source to a range with NO new distinct value leaves CacheItems byte-identical (same count, same indices, same selection flags, same order).</summary>
    [Fact]
    public void ChangeDataSource_NoNewValue_LeavesCacheItemsByteIdentical()
    {
        var (workbook, sheet, ctx, slicer) = BuildPivotAndSlicer();
        var before = slicer.CacheItems.Select(i => (i.Index, i.IsSelected)).ToList();

        // Redirect to a same-shaped range that still only has "A"/"B" -- no genuinely new value.
        sheet.SetCell(Addr(sheet, "J1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "K1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "J2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "K2"), new NumberValue(100));
        sheet.SetCell(Addr(sheet, "J3"), new TextValue("B"));
        sheet.SetCell(Addr(sheet, "K3"), new NumberValue(200));

        var changeSource = new ChangePivotTableSourceCommand(sheet.Id, "PivotTable1", Range(sheet, "J1", "K3"));
        changeSource.Apply(ctx).Success.Should().BeTrue();

        var after = slicer.CacheItems.Select(i => (i.Index, i.IsSelected)).ToList();
        after.Should().Equal(before, "Change Data Source that discovers no new distinct value must be a complete no-op on CacheItems");
    }
}
