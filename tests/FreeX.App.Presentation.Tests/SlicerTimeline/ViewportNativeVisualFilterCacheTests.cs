using FluentAssertions;
using FreeX.App.Presentation.SlicerTimeline;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.SlicerTimeline;

public sealed class ViewportNativeVisualFilterCacheTests
{
    [Fact]
    public void SameIdentityAndRevision_ReusesProjectionAndHydratedItems()
    {
        var (workbook, sheet, slicer, _) = CreatePivotSlicer();
        var cache = new ViewportNativeVisualFilterCache();

        var first = cache.GetOrCreate(workbook, sheet, revision: 4);
        var firstItems = slicer.AvailableItems;
        var second = cache.GetOrCreate(workbook, sheet, revision: 4);

        second.Should().BeSameAs(first);
        second.Slicers.Should().BeSameAs(first.Slicers);
        slicer.AvailableItems.Should().BeSameAs(firstItems);
        slicer.AvailableItems.Should().Equal("West", "East");
        slicer.AvailableItems.Should().NotBeAssignableTo<List<string>>();
        slicer.AvailableItems.Should().NotBeAssignableTo<string[]>();
    }

    [Fact]
    public void RevisionChange_RehydratesMutatedPivotItems()
    {
        var (workbook, sheet, slicer, sharedItems) = CreatePivotSlicer();
        var cache = new ViewportNativeVisualFilterCache();
        var first = cache.GetOrCreate(workbook, sheet, revision: 1);
        var firstItems = slicer.AvailableItems;
        sharedItems.Add("North");

        cache.GetOrCreate(workbook, sheet, revision: 1);
        slicer.AvailableItems.Should().BeSameAs(firstItems);
        slicer.AvailableItems.Should().NotContain("North");

        var refreshed = cache.GetOrCreate(workbook, sheet, revision: 2);
        refreshed.Should().NotBeSameAs(first);
        slicer.AvailableItems.Should().Equal("West", "East", "North");
    }

    [Fact]
    public void ExplicitlyClearedSelection_IsNotReimportedFromNativeCacheFlags()
    {
        var (workbook, sheet, slicer, _) = CreatePivotSlicer();
        slicer.CacheItems.Add(new SlicerCacheItem(0, IsSelected: true));
        slicer.CacheItems.Add(new SlicerCacheItem(1, IsSelected: false));
        slicer.SelectionCaptured = true;

        new ViewportNativeVisualFilterCache().GetOrCreate(workbook, sheet, revision: 0);

        slicer.SelectedItems.Should().BeEmpty();
    }

    [Fact]
    public void InitialUncapturedSelection_IsStillImportedFromNativeCacheFlags()
    {
        var (workbook, sheet, slicer, _) = CreatePivotSlicer();
        slicer.CacheItems.Add(new SlicerCacheItem(0, IsSelected: true));
        slicer.CacheItems.Add(new SlicerCacheItem(1, IsSelected: false));

        new ViewportNativeVisualFilterCache().GetOrCreate(workbook, sheet, revision: 0);

        slicer.SelectedItems.Should().Equal("West");
    }

    private static (Workbook Workbook, Sheet Sheet, SlicerModel Slicer, List<string> SharedItems)
        CreatePivotSlicer()
    {
        var workbook = new Workbook("SlicerBook");
        var sheet = workbook.AddSheet("Pivot");
        var sharedItems = new List<string> { "West", "East" };
        var pivotCache = new PivotCacheModel { CacheId = 7 };
        pivotCache.Fields.Add(new PivotCacheFieldModel("Region", SharedItems: sharedItems));
        workbook.PivotCaches.Add(pivotCache);
        sheet.PivotTables.Add(new PivotTableModel { Name = "Pivot1", CacheId = 7 });
        var slicer = new SlicerModel
        {
            Name = "Region Slicer",
            SourcePivotTableName = "Pivot1",
            SourceFieldName = "Region",
            DrawingAnchor = new DrawingAnchorRange(
                new DrawingAnchorPoint(1, 0, 1, 0),
                new DrawingAnchorPoint(3, 0, 6, 0)),
        };
        workbook.Slicers.Add(slicer);
        return (workbook, sheet, slicer, sharedItems);
    }
}
