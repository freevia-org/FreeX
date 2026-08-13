using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed class SlicerItemResolverTests
{
    [Fact]
    public void ResolveAvailableItems_TableSlicer_ReturnsDistinctColumnValuesByColumnId()
    {
        var workbook = new Workbook("TableSlicer");
        var sheet = workbook.AddSheet("Tasks");

        // Table A3:C6 — columns ID(id1), Task(id2), Category(id5). Category is the 3rd positional column.
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            Range = new GridRange(
                new CellAddress(sheet.Id, 3, 1),
                new CellAddress(sheet.Id, 6, 3)),
        };
        table.Columns.Add(new StructuredTableColumnModel(1, "ID"));
        table.Columns.Add(new StructuredTableColumnModel(2, "Task"));
        table.Columns.Add(new StructuredTableColumnModel(5, "Category"));
        sheet.StructuredTables.Add(table);

        // Header row 3, data rows 4..6, Category in column C (3). Repeat "Admin" to prove distinctness.
        sheet.SetCell(new CellAddress(sheet.Id, 3, 3), new TextValue("Category"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new TextValue("Admin"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 3), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 6, 3), new TextValue("Admin"));

        var slicer = new SlicerModel
        {
            Name = "Category",
            SourceTableId = 1,
            SourceTableColumnId = 5,
        };

        var items = SlicerItemResolver.ResolveAvailableItems(slicer, workbook);

        items.Should().Equal("Admin", "Sales");
    }

    [Fact]
    public void ResolveAvailableItems_PivotSlicer_ResolvesCaptionsFromSharedItemsAndSelection()
    {
        var workbook = new Workbook("PivotSlicer");
        workbook.AddSheet("Pivots");

        // Pivot cache field "Market" with shared items East/North/South/West.
        var cache = new PivotCacheModel { CacheId = 1 };
        cache.Fields.Add(new PivotCacheFieldModel(
            "Market",
            SharedItems: ["East", "North", "South", "West"]));
        workbook.PivotCaches.Add(cache);

        var slicer = new SlicerModel
        {
            Name = "Market",
            SourceFieldName = "Market",
            // Cache items reference field-item indices; index 1 (North) is selected.
            CacheItems =
            [
                new SlicerCacheItem(0, false),
                new SlicerCacheItem(2, true),  // out of order in the file => South? No: x maps to shared index
                new SlicerCacheItem(3, false),
                new SlicerCacheItem(1, false),
            ],
        };

        var items = SlicerItemResolver.ResolveAvailableItems(slicer, workbook);

        // Captions come from the shared items by the x index, preserving the file's item order.
        items.Should().Equal("East", "South", "West", "North");
        // The s="1" flag on index 2 (South) projects onto SelectedItems (a real subset).
        slicer.SelectedItems.Should().Equal("South");
    }

    [Fact]
    public void ResolveAvailableItems_PivotSlicer_AllSelected_DoesNotProjectSelection()
    {
        var workbook = new Workbook("PivotAllSelected");
        workbook.AddSheet("Pivots");
        var cache = new PivotCacheModel { CacheId = 1 };
        cache.Fields.Add(new PivotCacheFieldModel("Market", SharedItems: ["East", "West"]));
        workbook.PivotCaches.Add(cache);

        var slicer = new SlicerModel
        {
            Name = "Market",
            SourceFieldName = "Market",
            CacheItems = [new SlicerCacheItem(0, true), new SlicerCacheItem(1, true)],
        };

        var items = SlicerItemResolver.ResolveAvailableItems(slicer, workbook);

        items.Should().Equal("East", "West");
        // Everything selected == unfiltered/cleared state, so no SelectedItems projection.
        slicer.SelectedItems.Should().BeEmpty();
    }

    [Fact]
    public void ResolveAvailableItems_BoundPivotCacheWithoutCacheItems_ReturnsSharedItems()
    {
        var workbook = new Workbook("BoundPivotCache");
        workbook.AddSheet("Pivots");
        var decoy = new PivotCacheModel { CacheId = 1 };
        decoy.Fields.Add(new PivotCacheFieldModel("Market", SharedItems: ["Wrong"]));
        workbook.PivotCaches.Add(decoy);
        var bound = new PivotCacheModel { CacheId = 2 };
        bound.Fields.Add(new PivotCacheFieldModel("Market", SharedItems: ["North", "South"]));
        workbook.PivotCaches.Add(bound);
        var pivotTable = new PivotTableModel { Name = "Pivot1", CacheId = 2 };
        var slicer = new SlicerModel { Name = "Market", SourceFieldName = "Market" };

        SlicerItemResolver.ResolveAvailableItems(slicer, workbook, pivotTable)
            .Should().Equal("North", "South");
    }

    [Fact]
    public void ResolveAvailableItems_ReturnsEmptyWhenNeitherSourcePathApplies()
    {
        var workbook = new Workbook("None");
        workbook.AddSheet("Sheet1");
        var slicer = new SlicerModel { Name = "Orphan" };

        SlicerItemResolver.ResolveAvailableItems(slicer, workbook).Should().BeEmpty();
    }

    [Fact]
    public void PopulateAvailableItems_ProjectsOntoEverySlicer()
    {
        var workbook = new Workbook("Populate");
        var sheet = workbook.AddSheet("Tasks");
        var table = new StructuredTableModel
        {
            Id = 1,
            Name = "Table1",
            Range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
        };
        table.Columns.Add(new StructuredTableColumnModel(7, "Who"));
        sheet.StructuredTables.Add(table);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Who"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("Ann"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("Bob"));
        workbook.Slicers.Add(new SlicerModel { Name = "Who", SourceTableId = 1, SourceTableColumnId = 7 });

        SlicerItemResolver.PopulateAvailableItems(workbook);

        workbook.Slicers[0].AvailableItems.Should().Equal("Ann", "Bob");
    }
}
