using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Charts;

public sealed class ChartInsertionPlannerTests
{
    private sealed class TestCommandContext(Workbook workbook) : ICommandContext
    {
        public Workbook Workbook { get; } = workbook;

        public Sheet GetSheet(SheetId sheetId) =>
            Workbook.GetSheet(sheetId) ?? throw new KeyNotFoundException($"Sheet {sheetId} not found");
    }

    [Theory]
    [InlineData("insert.column", ChartType.Column)]
    [InlineData("insert.colClustered", ChartType.Column)]
    [InlineData("insert.colStacked", ChartType.StackedColumn)]
    [InlineData("insert.col100", ChartType.PercentStackedColumn)]
    [InlineData("insert.bar", ChartType.Bar)]
    [InlineData("insert.line", ChartType.Line)]
    [InlineData("insert.area", ChartType.Area)]
    [InlineData("insert.pie", ChartType.Pie)]
    [InlineData("insert.doughnut", ChartType.Doughnut)]
    [InlineData("insert.scatter", ChartType.Scatter)]
    [InlineData("insert.recommended", ChartType.Column)]
    [InlineData("Recommended Charts", ChartType.Column)]
    [InlineData("Line Chart", ChartType.Line)]
    [InlineData("Stock Chart", ChartType.Stock)]
    [InlineData("Bubble Chart", ChartType.Bubble)]
    [InlineData("Radar Chart", ChartType.Radar)]
    public void ChartTypeForRibbonCommand_MapsKnownIdsToType(string commandId, ChartType expected) =>
        ChartInsertionPlanner.ChartTypeForRibbonCommand(commandId).Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData("home.bold")]
    [InlineData("insert.table")]
    [InlineData("unknown")]
    public void ChartTypeForRibbonCommand_ReturnsNullForUnmappedIds(string commandId) =>
        ChartInsertionPlanner.ChartTypeForRibbonCommand(commandId).Should().BeNull();

    [Fact]
    public void CreateEmbeddedChartPlan_WithDefaultPlacement_ExpandsSingleCellToCurrentRegion()
    {
        var (workbook, sheet) = BuildPopulatedWorkbook();
        var selection = Range(sheet.Id, 2, 1, 2, 1);
        var expectedRange = Range(sheet.Id, 1, 1, 4, 2);

        var plan = ChartInsertionPlanner.CreateEmbeddedChartPlan(sheet, selection, ChartType.Column);
        var outcome = plan.Command.Apply(new TestCommandContext(workbook));

        outcome.Success.Should().BeTrue();
        plan.DataRange.Should().Be(expectedRange);
        plan.Placement.Should().Be(ChartInsertionPlanner.DefaultPlacement);
        var chart = sheet.Charts.Should().ContainSingle().Subject;
        chart.DataRange.Should().Be(expectedRange);
        chart.Left.Should().Be(ChartInsertionPlanner.DefaultLeft);
        chart.Top.Should().Be(ChartInsertionPlanner.DefaultTop);
        chart.Width.Should().Be(ChartInsertionPlanner.DefaultChartWidth);
        chart.Height.Should().Be(ChartInsertionPlanner.DefaultChartHeight);
    }

    [Fact]
    public void CreateEmbeddedChartPlan_WithViewport_UsesResolvedRangeForPlacementAndCommand()
    {
        var (workbook, sheet) = BuildPopulatedWorkbook();
        var selection = Range(sheet.Id, 2, 1, 2, 1);
        var expectedRange = Range(sheet.Id, 1, 1, 4, 2);
        var viewport = new ChartInsertionViewport(
            CreateViewport(firstRow: 1, firstCol: 1, rowCount: 20, columnCount: 10),
            AvailableWidth: 900,
            AvailableHeight: 500);

        var plan = ChartInsertionPlanner.CreateEmbeddedChartPlan(sheet, selection, ChartType.Column, viewport, "Chart");
        var outcome = plan.Command.Apply(new TestCommandContext(workbook));

        outcome.Success.Should().BeTrue();
        plan.DataRange.Should().Be(expectedRange);
        plan.Placement.Left.Should().BeApproximately(144, 0.0001);
        plan.Placement.Top.Should().BeApproximately(20, 0.0001);
        var chart = sheet.Charts.Should().ContainSingle().Subject;
        chart.DataRange.Should().Be(expectedRange);
        chart.Left.Should().Be(plan.Placement.Left);
        chart.Top.Should().Be(plan.Placement.Top);
        chart.Title.Should().Be("Chart");
    }

    [Fact]
    public void CreateEmbeddedChartPlan_UsesStructuredTableRangeForSingleCellInsideTable()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var tableRange = Range(sheet.Id, 3, 2, 8, 4);
        sheet.StructuredTables.Add(new StructuredTableModel
        {
            Id = 1,
            Name = "SalesTable",
            DisplayName = "SalesTable",
            Range = tableRange
        });

        var plan = ChartInsertionPlanner.CreateEmbeddedChartPlan(
            sheet,
            Range(sheet.Id, 5, 3, 5, 3),
            ChartType.Column);

        plan.DataRange.Should().Be(tableRange);
    }

    [Fact]
    public void BuildChartSheetCommand_UsesResolvedRangeForSingleCellSelection()
    {
        var (workbook, sheet) = BuildPopulatedWorkbook();
        var selection = Range(sheet.Id, 2, 1, 2, 1);
        var expectedRange = Range(sheet.Id, 1, 1, 4, 2);

        var command = ChartInsertionPlanner.BuildChartSheetCommand(
            sheet,
            sheet.Id,
            selection,
            ChartType.Column,
            "Chart");
        var outcome = command.Apply(new TestCommandContext(workbook));

        outcome.Success.Should().BeTrue();
        command.CreatedSheetId.Should().NotBeNull();
        var chartSheet = workbook.GetSheet(command.CreatedSheetId!.Value);
        chartSheet.Should().NotBeNull();
        chartSheet!.Charts.Should().ContainSingle().Which.DataRange.Should().Be(expectedRange);
    }

    [Fact]
    public void CreateEmbeddedChartPlan_ForScatter_PreservesCoreCategorySemantics()
    {
        var (workbook, sheet) = BuildPopulatedWorkbook();
        var selection = Range(sheet.Id, 1, 1, 4, 2);

        var plan = ChartInsertionPlanner.CreateEmbeddedChartPlan(sheet, selection, ChartType.Scatter);
        var outcome = plan.Command.Apply(new TestCommandContext(workbook));

        outcome.Success.Should().BeTrue();
        var chart = sheet.Charts.Should().ContainSingle().Subject;
        chart.Type.Should().Be(ChartType.Scatter);
        chart.FirstColIsCategories.Should().BeFalse();
        chart.DataRange.Should().Be(selection);
    }

    [Fact]
    public void CreatePlacement_PlacesChartNextToSourceRangeInCurrentViewport()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var range = Range(sheet.Id, startRow: 50, startCol: 10, endRow: 53, endCol: 12);
        var viewport = CreateViewport(firstRow: 50, firstCol: 10, rowCount: 20, columnCount: 10);

        var placement = ChartInsertionPlanner.CreatePlacement(
            sheet,
            range,
            viewport,
            viewportWidth: 900,
            viewportHeight: 500);

        placement.Left.Should().BeApproximately(784, 0.0001);
        placement.Top.Should().BeApproximately(1000, 0.0001);
        placement.Width.Should().Be(ChartInsertionPlanner.DefaultChartWidth);
        placement.Height.Should().Be(ChartInsertionPlanner.DefaultChartHeight);
    }

    [Fact]
    public void CreatePlacement_ClampsChartIntoCurrentViewportWhenSourceIsNearRightEdge()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var range = Range(sheet.Id, startRow: 50, startCol: 18, endRow: 53, endCol: 20);
        var viewport = CreateViewport(firstRow: 50, firstCol: 10, rowCount: 20, columnCount: 10);

        var placement = ChartInsertionPlanner.CreatePlacement(
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

        var placement = ChartInsertionPlanner.CreatePlacement(
            sheet,
            range,
            viewport: null,
            viewportWidth: 0,
            viewportHeight: 0);

        placement.Left.Should().BeApproximately(208, 0.0001);
        placement.Top.Should().BeApproximately(40, 0.0001);
    }

    [Fact]
    public void CreatePlacement_DeduplicatesHiddenIndexesAcrossSourcesWithViewport()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.ColumnWidths[2] = 20;
        sheet.RowHeights[3] = 50;
        sheet.HiddenCols.Add(2);
        sheet.GroupHiddenCols.Add(2);
        sheet.HiddenRows.Add(3);
        sheet.FilterHiddenRows.Add(3);
        sheet.GroupHiddenRows.Add(3);
        var range = Range(sheet.Id, startRow: 4, startCol: 4, endRow: 4, endCol: 4);
        var viewport = CreateViewport(firstRow: 1, firstCol: 1, rowCount: 20, columnCount: 20);

        var placement = ChartInsertionPlanner.CreatePlacement(
            sheet,
            range,
            viewport,
            viewportWidth: 1200,
            viewportHeight: 800);

        placement.Left.Should().BeApproximately(208, 0.0001);
        placement.Top.Should().BeApproximately(40, 0.0001);
    }

    [Fact]
    public void CreateEmbeddedChartPlan_PreservesSourceRangeWhenPlacementSkipsHiddenExtents()
    {
        var workbook = new Workbook("test");
        var sheet = workbook.AddSheet("Sheet1");
        var range = Range(sheet.Id, startRow: 1, startCol: 1, endRow: 4, endCol: 4);
        sheet.HiddenRows.Add(2);
        sheet.FilterHiddenRows.Add(3);
        sheet.HiddenCols.Add(2);
        sheet.GroupHiddenCols.Add(3);

        var plan = ChartInsertionPlanner.CreateEmbeddedChartPlan(
            sheet,
            range,
            ChartType.Column,
            new ChartInsertionViewport(null, AvailableWidth: 0, AvailableHeight: 0),
            "Chart");

        plan.DataRange.Should().Be(range);
        plan.Placement.Left.Should().BeLessThan(4 * 64);
        plan.Placement.Top.Should().BeLessThan(4 * 20);
    }

    private static (Workbook Workbook, Sheet Sheet) BuildPopulatedWorkbook()
    {
        var workbook = new Workbook("Charts");
        var sheet = workbook.AddSheet("Data");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Sales"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("North"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new TextValue("South"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new NumberValue(30));

        return (workbook, sheet);
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
