using FreeX.App.Avalonia.Charts;
using FreeX.Core.Commands;
using FreeX.Core.Model;

using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// Unit tests for the UI-free <see cref="InsertChartCommandFactory"/>: the ribbon-id → <see cref="ChartType"/>
/// mapping and that the built <see cref="AddChartCommand"/>, when applied to an in-memory workbook, charts the
/// selection with the requested type. No running shell required.
/// </summary>
public sealed class InsertChartCommandFactoryTests
{
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
        InsertChartCommandFactory.ChartTypeForRibbonCommand(commandId).Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData("home.bold")]
    [InlineData("insert.table")]
    [InlineData("unknown")]
    public void ChartTypeForRibbonCommand_ReturnsNullForUnmappedIds(string commandId) =>
        InsertChartCommandFactory.ChartTypeForRibbonCommand(commandId).Should().BeNull();

    [Fact]
    public void Build_PlacesChartAtSharedDefaults()
    {
        var (workbook, sheet) = BuildPopulatedWorkbook();
        var selection = Range(sheet.Id, 1, 1, 4, 2);

        var command = InsertChartCommandFactory.Build(sheet.Id, selection, ChartType.Column);
        var outcome = command.Apply(new TestCommandContext(workbook));

        outcome.Success.Should().BeTrue();
        var chart = sheet.Charts.Should().ContainSingle().Subject;
        chart.Type.Should().Be(ChartType.Column);
        chart.DataRange.Should().Be(selection);
        chart.Left.Should().Be(InsertChartCommandFactory.DefaultLeft);
        chart.Top.Should().Be(InsertChartCommandFactory.DefaultTop);
        chart.Width.Should().Be(InsertChartCommandFactory.DefaultWidth);
        chart.Height.Should().Be(InsertChartCommandFactory.DefaultHeight);
    }

    [Fact]
    public void Build_WithSheetExpandsSingleCellSelectionToCurrentRegion()
    {
        var (workbook, sheet) = BuildPopulatedWorkbook();
        var selection = Range(sheet.Id, 2, 1, 2, 1);
        var expectedRange = Range(sheet.Id, 1, 1, 4, 2);

        var command = InsertChartCommandFactory.Build(sheet, selection, ChartType.Column);
        var outcome = command.Apply(new TestCommandContext(workbook));

        outcome.Success.Should().BeTrue();
        var chart = sheet.Charts.Should().ContainSingle().Subject;
        chart.DataRange.Should().Be(expectedRange);
        chart.Left.Should().Be(InsertChartCommandFactory.DefaultLeft);
        chart.Top.Should().Be(InsertChartCommandFactory.DefaultTop);
    }

    [Fact]
    public void Build_TargetsRequestedRangeAndType_ForScatter()
    {
        var (workbook, sheet) = BuildPopulatedWorkbook();
        var selection = Range(sheet.Id, 1, 1, 4, 2);

        var command = InsertChartCommandFactory.Build(sheet.Id, selection, ChartType.Scatter);
        var outcome = command.Apply(new TestCommandContext(workbook));

        outcome.Success.Should().BeTrue();
        var chart = sheet.Charts.Should().ContainSingle().Subject;
        chart.Type.Should().Be(ChartType.Scatter);
        // Scatter charts treat the first column as X values, not categories.
        chart.FirstColIsCategories.Should().BeFalse();
        chart.DataRange.Should().Be(selection);
    }

    [Fact]
    public void Build_RevertRemovesInsertedChart()
    {
        var (workbook, sheet) = BuildPopulatedWorkbook();
        var selection = Range(sheet.Id, 1, 1, 4, 2);
        var context = new TestCommandContext(workbook);

        var command = InsertChartCommandFactory.Build(sheet.Id, selection, ChartType.Column);
        command.Apply(context).Success.Should().BeTrue();
        sheet.Charts.Should().HaveCount(1);

        command.Revert(context);
        sheet.Charts.Should().BeEmpty();
    }

    private static (Workbook Workbook, Sheet Sheet) BuildPopulatedWorkbook()
    {
        var workbook = new Workbook("Charts");
        var sheet = workbook.AddSheet("Data");

        // A1:B4 — header row + a category column and one numeric series.
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

    private static GridRange Range(SheetId sheetId, uint startRow, uint startCol, uint endRow, uint endCol) =>
        new(new CellAddress(sheetId, startRow, startCol), new CellAddress(sheetId, endRow, endCol));
}
