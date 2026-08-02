using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R117-commands-pivot-slicer-growth: an EXISTING pivot slicer's <see cref="SlicerModel.CacheItems"/>
/// was populated exactly once at slicer-creation time (<c>AddSlicerCommand.BuildInitialCacheItems</c>)
/// or by the XLSX loader, and nothing ever appended to it afterwards. r115/r116 made
/// <see cref="PivotCacheFieldModel.SharedItems"/> append-only on refresh precisely so an existing
/// slicer's indices never get renumbered -- but that only relocated the bug: a brand-new distinct value
/// now lands at a brand-new index that no existing slicer's CacheItems represents at all, so it stayed
/// invisible in that slicer's tile list forever, even after Refresh.
///
/// The slicer here is constructed directly with a CacheItems shape carrying a partial selection (index 0
/// = "A" deselected, index 1 = "B" selected) -- mirroring EXACTLY what a workbook loaded from an existing
/// file looks like (its selection is encoded purely as CacheItems[].IsSelected flags; see
/// <see cref="XlsxSlicerTimelineMetadataReader.ReadSlicerCacheItems"/> and R116's own tests for the same
/// pattern), which is the scenario the bug report is specifically about ("An EXISTING pivot slicer never
/// grows its CacheItems"). Pivot creation and the growth itself are driven through the real product entry
/// points: <see cref="AddPivotTableCommand.Apply"/> and <see cref="RefreshPivotTableCommand.Apply"/> (the
/// actual F5 / "Refresh" action), followed by <see cref="SlicerItemResolver.ResolveAvailableItems"/> (the
/// real live-UI/render entry point).
/// </summary>
public sealed class R117_PivotSlicerGrowsCacheItemsOnRefreshTests
{
    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

    private static (Workbook Workbook, Sheet Sheet, TestCommandContext Ctx, SlicerModel Slicer) BuildPivotAndSlicer()
    {
        var workbook = new Workbook("R117PivotSlicerGrowth");
        var sheet = workbook.AddSheet("Data");

        // Source range A1:B4 -- only rows 2-3 filled at first (Category = A, B); row 4 left blank so a
        // later edit can grow the distinct-value set without needing to resize the pivot's SourceRange.
        sheet.SetCell(Addr(sheet, "A1"), new TextValue("Category"));
        sheet.SetCell(Addr(sheet, "B1"), new TextValue("Amount"));
        sheet.SetCell(Addr(sheet, "A2"), new TextValue("A"));
        sheet.SetCell(Addr(sheet, "B2"), new NumberValue(10));
        sheet.SetCell(Addr(sheet, "A3"), new TextValue("B"));
        sheet.SetCell(Addr(sheet, "B3"), new NumberValue(20));

        var ctx = new TestCommandContext(workbook);
        var addPivot = new AddPivotTableCommand(
            sheet.Id,
            Range(sheet, "A1", "B4"),
            Range(sheet, "G3", "H6"),
            "PivotTable1",
            rowFieldIndexes: [0],
            dataFieldIndexes: [1]);
        addPivot.Apply(ctx).Success.Should().BeTrue();

        var cache = workbook.PivotCaches.Should().ContainSingle().Subject;
        var categoryField = cache.Fields.Should().ContainSingle(f => f.Name == "Category").Subject;
        categoryField.SharedItems.Should().Equal("A", "B");

        // Mirrors a slicer loaded from an existing file: the user's selection lives purely as
        // CacheItems[].IsSelected ("A" deselected, "B" selected) -- SelectedItems starts empty until the
        // resolver below projects it, exactly like a just-opened workbook.
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
    /// THE anchor test: a brand-new distinct value ("C") appears in the source after the slicer was
    /// created/loaded and the user already has a real partial selection encoded on the cache items.
    /// Running the real Refresh entry point must make "C" visible through the real resolver entry point,
    /// and must not disturb the user's existing "B"-only selection.
    /// </summary>
    [Fact]
    public void RefreshPivotTable_NewDistinctValue_BecomesVisibleInExistingSlicer_WithoutDisturbingSelection()
    {
        var (workbook, sheet, ctx, slicer) = BuildPivotAndSlicer();

        // A genuinely new distinct value appears in the source's previously-blank row.
        sheet.SetCell(Addr(sheet, "A4"), new TextValue("C"));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));

        var refresh = new RefreshPivotTableCommand(sheet.Id, "PivotTable1");
        refresh.Apply(ctx).Success.Should().BeTrue();

        var cache = CommandGuards.FindPivotCache(workbook, sheet.PivotTables.Single(p => p.Name == "PivotTable1"))!;
        var categoryField = cache.Fields.Should().ContainSingle(f => f.Name == "Category").Subject;
        categoryField.SharedItems.Should().Equal("A", "B", "C");

        // Real live-UI/render entry point.
        var available = SlicerItemResolver.ResolveAvailableItems(slicer, workbook);
        available.Should().Contain("C", "a newly-appeared pivot cache value must become visible in an " +
            "EXISTING slicer after a refresh, not stay invisible forever");
        available.Should().BeEquivalentTo(["A", "B", "C"]);

        // The user's pre-existing partial selection must survive completely unchanged: "A" was
        // explicitly deselected before the refresh and must STILL be excluded afterwards; "B" was
        // selected and must STILL be included. "C" is the brand-new value, which Excel includes by
        // default in a slicer that never explicitly excluded it (matching the CacheItem the fix
        // appends with IsSelected: true), so it also shows up as selected -- but crucially "A" is not
        // resurrected by the refresh.
        slicer.SelectedItems.Should().Contain("B").And.Contain("C").And.NotContain("A");
    }

    /// <summary>No-regression sibling: an ordinary refresh with NO new distinct value leaves CacheItems byte-identical (same count, same indices, same selection flags, same order).</summary>
    [Fact]
    public void RefreshPivotTable_NoNewValue_LeavesCacheItemsByteIdentical()
    {
        var (workbook, sheet, ctx, slicer) = BuildPivotAndSlicer();
        var before = slicer.CacheItems.Select(i => (i.Index, i.IsSelected)).ToList();

        var refresh = new RefreshPivotTableCommand(sheet.Id, "PivotTable1");
        refresh.Apply(ctx).Success.Should().BeTrue();

        var after = slicer.CacheItems.Select(i => (i.Index, i.IsSelected)).ToList();
        after.Should().Equal(before, "a refresh that discovers no new distinct value must be a complete no-op on CacheItems");
    }

    /// <summary>No-regression sibling: a user's explicitly deselected item stays deselected across a refresh that introduces an unrelated new value.</summary>
    [Fact]
    public void RefreshPivotTable_NewValue_DeselectedExistingItemStaysDeselected()
    {
        var (workbook, sheet, ctx, slicer) = BuildPivotAndSlicer();

        sheet.SetCell(Addr(sheet, "A4"), new TextValue("C"));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));

        var refresh = new RefreshPivotTableCommand(sheet.Id, "PivotTable1");
        refresh.Apply(ctx).Success.Should().BeTrue();

        var stillA = slicer.CacheItems.Single(i => i.Index == 0);
        stillA.IsSelected.Should().BeFalse("a refresh must never flip an existing item's own selection flag, only append new ones");

        var newC = slicer.CacheItems.Single(i => i.Index == 2);
        newC.IsSelected.Should().BeTrue("Excel includes a newly-appeared item by default in an unfiltered/not-yet-considered slicer");
    }
}
