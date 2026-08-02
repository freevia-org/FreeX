using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R118-commands-pivot-slicer-includeNewItemsInFilter: r117/r118 made an EXISTING pivot slicer's
/// <see cref="SlicerModel.CacheItems"/> grow to represent a brand-new distinct pivot-cache value that
/// appears after a refresh / "Change Data Source" -- but both rounds unconditionally defaulted the new
/// item to <c>IsSelected: true</c>, even when the field already had a MANUAL FILTER applied (at least
/// one existing item deselected). Excel's <c>pivotField/@includeNewItemsInFilter</c> (ECMA-376
/// §18.10.1.65, default <see langword="false"/> when absent) governs exactly this: when a manual filter
/// is already in effect, a newly-appeared item is only auto-selected when that flag is explicitly true;
/// otherwise it must be added deselected so the user's deliberately-narrowed filter is not silently
/// widened by data they never asked to see reappear. An UNFILTERED slicer (no existing item deselected)
/// has nothing to preserve, so a new item is still selected by default there -- otherwise the slicer
/// would spontaneously start filtering data it never filtered before.
///
/// These tests drive the real product entry points: <see cref="AddPivotTableCommand.Apply"/> and
/// <see cref="RefreshPivotTableCommand.Apply"/> (the actual F5 / "Refresh" action), followed by
/// <see cref="SlicerItemResolver.ResolveAvailableItems"/> (the real live-UI/render entry point) --
/// mirroring R117_PivotSlicerGrowsCacheItemsOnRefreshTests's own structure.
/// </summary>
public sealed class R118_IncludeNewItemsInFilterGovernsSlicerGrowthTests
{
    private static CellAddress Addr(Sheet sheet, string a1) => CellAddress.Parse(a1, sheet.Id);

    private static GridRange Range(Sheet sheet, string start, string end) =>
        new(Addr(sheet, start), Addr(sheet, end));

    private static (Workbook Workbook, Sheet Sheet, TestCommandContext Ctx, SlicerModel Slicer) BuildPivotAndSlicer(
        bool filtered,
        bool? includeNewItemsInFilter = null)
    {
        var workbook = new Workbook("R118IncludeNewItemsGovernsGrowth");
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

        if (includeNewItemsInFilter is { } flag)
        {
            var pivot = sheet.PivotTables.Single(p => p.Name == "PivotTable1");
            var rowFieldIndex = pivot.RowFields.FindIndex(f => f.SourceFieldIndex == 0);
            rowFieldIndex.Should().BeGreaterThanOrEqualTo(0);
            pivot.RowFields[rowFieldIndex] = pivot.RowFields[rowFieldIndex] with { IncludeNewItemsInFilter = flag };
        }

        // Mirrors a slicer loaded from an existing file: the user's selection lives purely as
        // CacheItems[].IsSelected. "filtered" wires index 0 ("A") deselected -- a manual filter already
        // in effect; "unfiltered" leaves every existing item selected.
        var slicer = new SlicerModel
        {
            Name = "Category Slicer",
            CacheName = "Slicer_Category",
            SourcePivotTableName = "PivotTable1",
            SourceFieldName = "Category",
            CacheItems = filtered
                ? [new SlicerCacheItem(0, false), new SlicerCacheItem(1, true)]
                : [new SlicerCacheItem(0, true), new SlicerCacheItem(1, true)],
        };
        workbook.Slicers.Add(slicer);

        return (workbook, sheet, ctx, slicer);
    }

    /// <summary>
    /// THE anchor test: a FILTERED slicer ("A" already explicitly deselected) with no explicit
    /// includeNewItemsInFilter (absent => Excel default false) must NOT auto-select a brand-new item --
    /// the new item is appended DESELECTED, and the resolver's projected SelectedItems must not contain
    /// it either. Before the fix, ExtendBoundSlicerCacheItems unconditionally appended
    /// <c>IsSelected: true</c>, so this failed.
    /// </summary>
    [Fact]
    public void RefreshPivotTable_FilteredSlicer_NewValueDefaultAbsent_StaysDeselected()
    {
        var (workbook, sheet, ctx, slicer) = BuildPivotAndSlicer(filtered: true);

        sheet.SetCell(Addr(sheet, "A4"), new TextValue("C"));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));

        var refresh = new RefreshPivotTableCommand(sheet.Id, "PivotTable1");
        refresh.Apply(ctx).Success.Should().BeTrue();

        var newC = slicer.CacheItems.Single(i => i.Index == 2);
        newC.IsSelected.Should().BeFalse(
            "a new item must not widen an existing manual filter unless includeNewItemsInFilter is true");

        var available = SlicerItemResolver.ResolveAvailableItems(slicer, workbook);
        available.Should().BeEquivalentTo(["A", "B", "C"], "the new value is still visible as a tile");
        slicer.SelectedItems.Should().Contain("B").And.NotContain("C").And.NotContain("A");
    }

    /// <summary>
    /// Sibling test: an UNFILTERED slicer (no item currently deselected) still gets a brand-new item
    /// selected by default -- the r117/r118 behavior that is correct and must not regress, because
    /// there is no manual filter to preserve and a slicer with every existing item selected should not
    /// spontaneously start filtering data it never filtered before.
    /// </summary>
    [Fact]
    public void RefreshPivotTable_UnfilteredSlicer_NewValueBecomesSelectedByDefault()
    {
        var (workbook, sheet, ctx, slicer) = BuildPivotAndSlicer(filtered: false);

        sheet.SetCell(Addr(sheet, "A4"), new TextValue("C"));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));

        var refresh = new RefreshPivotTableCommand(sheet.Id, "PivotTable1");
        refresh.Apply(ctx).Success.Should().BeTrue();

        var newC = slicer.CacheItems.Single(i => i.Index == 2);
        newC.IsSelected.Should().BeTrue(
            "an unfiltered slicer has no manual filter to preserve, so a new item is selected by default");

        var available = SlicerItemResolver.ResolveAvailableItems(slicer, workbook);
        available.Should().BeEquivalentTo(["A", "B", "C"]);
    }

    /// <summary>
    /// Test for includeNewItemsInFilter = true on a FILTERED slicer: the field explicitly opts in to
    /// widening its manual filter, so the brand-new item IS included/selected.
    /// </summary>
    [Fact]
    public void RefreshPivotTable_FilteredSlicer_IncludeNewItemsInFilterTrue_NewValueBecomesSelected()
    {
        var (workbook, sheet, ctx, slicer) = BuildPivotAndSlicer(filtered: true, includeNewItemsInFilter: true);

        sheet.SetCell(Addr(sheet, "A4"), new TextValue("C"));
        sheet.SetCell(Addr(sheet, "B4"), new NumberValue(30));

        var refresh = new RefreshPivotTableCommand(sheet.Id, "PivotTable1");
        refresh.Apply(ctx).Success.Should().BeTrue();

        var newC = slicer.CacheItems.Single(i => i.Index == 2);
        newC.IsSelected.Should().BeTrue(
            "includeNewItemsInFilter=true explicitly opts the field in to auto-including new items in its manual filter");

        var available = SlicerItemResolver.ResolveAvailableItems(slicer, workbook);
        available.Should().BeEquivalentTo(["A", "B", "C"]);
        slicer.SelectedItems.Should().Contain("B").And.Contain("C").And.NotContain("A");
    }
}
