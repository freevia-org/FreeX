using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed class ChartInsertionPlacementPlannerTests
{
    [Fact]
    public void CreatePlacement_UsesSharedColumnWidthPixelMapperForWorksheetCoordinates()
    {
        var source = LocalizedXamlTestSupport.ReadHostSourceFile("ChartInsertionPlacementPlanner.cs");

        source.Should().Contain("ColumnWidthPixelMapper.ColumnWidthToPixels(width)");
        source.Should().NotContain("private static double ColumnWidthToPixels(double width)");
    }

    [Fact]
    public void CreatePlacement_PlacesChartNextToSourceRangeInCurrentViewport()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var range = Range(sheet.Id, startRow: 50, startCol: 10, endRow: 53, endCol: 12);
        var viewport = CreateViewport(firstRow: 50, firstCol: 10, rowCount: 20, columnCount: 10);

        var placement = ChartInsertionPlacementPlanner.CreatePlacement(
            sheet,
            range,
            viewport,
            viewportWidth: 900,
            viewportHeight: 500);

        placement.Left.Should().BeApproximately(784, 0.0001);
        placement.Top.Should().BeApproximately(1000, 0.0001);
        placement.Width.Should().Be(ChartInsertionPlacementPlanner.DefaultChartWidth);
        placement.Height.Should().Be(ChartInsertionPlacementPlanner.DefaultChartHeight);
    }

    [Fact]
    public void CreatePlacement_ClampsChartIntoCurrentViewportWhenSourceIsNearRightEdge()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var range = Range(sheet.Id, startRow: 50, startCol: 18, endRow: 53, endCol: 20);
        var viewport = CreateViewport(firstRow: 50, firstCol: 10, rowCount: 20, columnCount: 10);

        var placement = ChartInsertionPlacementPlanner.CreatePlacement(
            sheet,
            range,
            viewport,
            viewportWidth: 800,
            viewportHeight: 500);

        placement.Left.Should().BeApproximately(956, 0.0001);
        placement.Top.Should().BeApproximately(1000, 0.0001);
    }

    [Fact]
    public void CreatePlacement_RemovesHiddenRowAndColumnExtentsFromWorksheetCoordinates()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.ColumnWidths[2] = 20;
        sheet.RowHeights[3] = 50;
        sheet.HiddenCols.Add(2);
        sheet.HiddenRows.Add(3);
        var range = Range(sheet.Id, startRow: 4, startCol: 4, endRow: 4, endCol: 4);

        var placement = ChartInsertionPlacementPlanner.CreatePlacement(
            sheet,
            range,
            viewport: null,
            viewportWidth: 0,
            viewportHeight: 0);

        placement.Left.Should().BeApproximately(208, 0.0001);
        placement.Top.Should().BeApproximately(40, 0.0001);
    }

    private static ViewportModel CreateViewport(uint firstRow, uint firstCol, int rowCount, int columnCount)
    {
        var rows = Enumerable.Range(0, rowCount)
            .Select(index => new RowMetric(firstRow + (uint)index, 20, index * 20))
            .ToArray();
        var columns = Enumerable.Range(0, columnCount)
            .Select(index => new ColMetric(firstCol + (uint)index, 64, index * 64))
            .ToArray();
        return new ViewportModel([], rows, columns);
    }

    private static GridRange Range(SheetId sheetId, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheetId, startRow, startCol), new CellAddress(sheetId, endRow, endCol));
}
