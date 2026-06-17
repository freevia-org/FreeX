using FluentAssertions;
using FreeX.App.Avalonia;
using FreeX.App.Presentation.SlicerTimeline;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Tests for the non-UI slicer/timeline source glue: reading the distinct, ordered field items for a
/// slicer's connected PivotTable field, and resolving a timeline's display granularity from its date
/// bounds. No running UI.
/// </summary>
public sealed class SlicerTimelineSourceReaderTests
{
    [Fact]
    public void ReadFieldItems_ReturnsDistinctOrderedItems_ExcludingHeaderRow()
    {
        var (_, sheet) = BuildSheet();
        // A1:B5 — header row + a Region column with a duplicate and a blank.
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("West"));
        // Row 5 Region is blank → maps to the "(blank)" item.

        var pivot = new PivotTableModel
        {
            Name = "Pivot1",
            SourceRange = Range(sheet.Id, 1, 1, 5, 2),
        };

        var items = SlicerTimelineSourceReader.ReadFieldItems(sheet, pivot, "Region");

        items.Should().Equal("(blank)", "East", "West");
    }

    [Fact]
    public void ReadFieldItems_UnknownField_ReturnsEmpty()
    {
        var (_, sheet) = BuildSheet();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        var pivot = new PivotTableModel { Name = "Pivot1", SourceRange = Range(sheet.Id, 1, 1, 2, 1) };

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
        var sheet = workbook.AddSheet("Data");
        return (workbook, sheet);
    }

    private static GridRange Range(SheetId sheetId, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheetId, startRow, startCol), new CellAddress(sheetId, endRow, endCol));
}
