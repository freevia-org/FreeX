using System.Globalization;

using FluentAssertions;
using FreeX.App.Presentation.SlicerTimeline;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.SlicerTimeline;

public sealed class SlicerTimelineSourceReaderTests
{
    [Fact]
    public void ReadFieldItems_ReturnsDistinctOrderedItems_ExcludingHeaderRow()
    {
        var (_, sheet) = BuildSheet();
        Set(sheet, 1, 1, "Region");
        Set(sheet, 1, 2, "Sales");
        Set(sheet, 2, 1, "West");
        Set(sheet, 3, 1, "East");
        Set(sheet, 4, 1, "West");

        var pivot = new PivotTableModel
        {
            Name = "Pivot1",
            SourceRange = Range(sheet, 1, 1, 5, 2),
        };

        SlicerTimelineSourceReader.ReadFieldItems(sheet, pivot, "Region")
            .Should().Equal("(blank)", "East", "West");
    }

    [Fact]
    public void ReadFieldItems_OrdersUsingCurrentCultureIgnoringCase()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("sv-SE");
            var (_, sheet) = BuildSheet();
            Set(sheet, 1, 1, "Place");
            Set(sheet, 2, 1, "\u00c4ngelholm");
            Set(sheet, 3, 1, "\u00c5land");
            Set(sheet, 4, 1, "\u00d6rebro");
            Set(sheet, 5, 1, "\u00e4ngelholm");
            var pivot = new PivotTableModel
            {
                Name = "Pivot1",
                SourceRange = Range(sheet, 1, 1, 5, 1),
            };
            var expected = new[] { "\u00c4ngelholm", "\u00c5land", "\u00d6rebro" }
                .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase);

            SlicerTimelineSourceReader.ReadFieldItems(sheet, pivot, "place")
                .Should().Equal(expected);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    [Fact]
    public void SourceSession_ResolvesConnectedPivotSourceSheet()
    {
        var workbook = new Workbook("CrossSheetPivot");
        var dataSheet = workbook.AddSheet("Data");
        var pivotSheet = workbook.AddSheet("Pivot");
        Set(dataSheet, 1, 1, "Region");
        Set(dataSheet, 2, 1, "West");
        Set(dataSheet, 3, 1, "East");
        pivotSheet.PivotTables.Add(new PivotTableModel
        {
            Name = "Pivot1",
            SourceRange = Range(dataSheet, 1, 1, 3, 1),
        });
        var slicer = new SlicerModel
        {
            Name = "Region Slicer",
            SourcePivotTableName = "Pivot1",
            SourceFieldName = "Region",
        };

        var session = new SlicerTimelineSourceSession(workbook);

        session.ResolvePivotSource(slicer)!.SourceSheet.Should().BeSameAs(dataSheet);
        session.ReadSlicerSourceItems(slicer).Should().Equal("East", "West");
    }

    [Fact]
    public void SourceSession_UsesBoundPivotCacheWhenCacheItemsAreMissing()
    {
        var workbook = new Workbook("CacheFallback");
        var sheet = workbook.AddSheet("Pivot");
        var decoy = new PivotCacheModel { CacheId = 4 };
        decoy.Fields.Add(new PivotCacheFieldModel("Region", SharedItems: ["Wrong"]));
        workbook.PivotCaches.Add(decoy);
        var bound = new PivotCacheModel { CacheId = 7 };
        bound.Fields.Add(new PivotCacheFieldModel("Region", SharedItems: ["West", "East"]));
        workbook.PivotCaches.Add(bound);
        sheet.PivotTables.Add(new PivotTableModel { Name = "Pivot1", CacheId = 7 });
        var slicer = new SlicerModel
        {
            Name = "Region Slicer",
            SourcePivotTableName = "Pivot1",
            SourceFieldName = "Region",
        };

        new SlicerTimelineSourceSession(workbook).ReadSlicerSourceItems(slicer)
            .Should().Equal("West", "East");
    }

    [Fact]
    public void SourceSession_ProjectsTableSlicerItemsIntoPaneTiles()
    {
        var workbook = new Workbook("TableSlicer");
        var sheet = workbook.AddSheet("Tasks");
        var table = new StructuredTableModel
        {
            Id = 3,
            Name = "Tasks",
            Range = Range(sheet, 1, 1, 4, 1),
        };
        table.Columns.Add(new StructuredTableColumnModel(8, "Team"));
        sheet.StructuredTables.Add(table);
        Set(sheet, 1, 1, "Team");
        Set(sheet, 2, 1, "Sales");
        Set(sheet, 3, 1, "Admin");
        Set(sheet, 4, 1, "Sales");
        var slicer = new SlicerModel
        {
            Name = "Team Slicer",
            SourceTableId = 3,
            SourceTableColumnId = 8,
        };

        var paneItem = new SlicerTimelineSourceSession(workbook).BuildSlicerPaneItem(slicer);

        paneItem.Tiles.Select(tile => tile.Caption).Should().Equal("Admin", "Sales");
        paneItem.Tiles.Should().OnlyContain(tile => tile.IsSelected);
    }

    [Fact]
    public void ReadFieldItems_UnknownField_ReturnsEmpty()
    {
        var (_, sheet) = BuildSheet();
        Set(sheet, 1, 1, "Region");
        var pivot = new PivotTableModel { Name = "Pivot1", SourceRange = Range(sheet, 1, 1, 2, 1) };

        SlicerTimelineSourceReader.ReadFieldItems(sheet, pivot, "Missing").Should().BeEmpty();
    }

    [Theory]
    [InlineData("2024-01-01", "2024-02-15", TimelineGranularity.Day)]
    [InlineData("2024-01-01", "2024-09-30", TimelineGranularity.Month)]
    [InlineData("2021-01-01", "2024-01-01", TimelineGranularity.Quarter)]
    [InlineData("2010-01-01", "2024-01-01", TimelineGranularity.Year)]
    public void ResolveGranularity_BucketsBySpan(string start, string end, TimelineGranularity expected)
    {
        var timeline = new TimelineModel { Name = "T", StartDate = start, EndDate = end };

        SlicerTimelineGranularity.Resolve(timeline).Should().Be(expected);
    }

    [Fact]
    public void ResolveGranularity_MissingBounds_DefaultsToMonth()
    {
        SlicerTimelineGranularity.Resolve(new TimelineModel { Name = "T" })
            .Should().Be(TimelineGranularity.Month);
    }

    private static (Workbook Workbook, Sheet Sheet) BuildSheet()
    {
        var workbook = new Workbook("Slicers");
        return (workbook, workbook.AddSheet("Data"));
    }

    private static void Set(Sheet sheet, uint row, uint col, string value) =>
        sheet.SetCell(new CellAddress(sheet.Id, row, col), new TextValue(value));

    private static GridRange Range(Sheet sheet, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheet.Id, startRow, startCol), new CellAddress(sheet.Id, endRow, endCol));
}
