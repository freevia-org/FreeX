using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// R92-app-chart-data-edit-5-1: the Select Data dialog's "Remove Series" button used to only edit a
/// disconnected ListBox -- clicking OK never touched the chart. <see cref="RemoveChartSeriesCommand"/>
/// is the real, undoable fix: it excludes the removed series' worksheet column via an authoritative
/// <see cref="ChartModel.SeriesColumnMappings"/> entry (the same mapping the renderer's
/// ShouldRenderColumnAsSeries/GetSeriesIndex already honor) and remaps every SeriesIndex-keyed
/// override so nothing silently mis-applies to whichever series shifts into the removed slot.
/// </summary>
public sealed class R92_RemoveChartSeriesCommandTests
{
    // 4 columns (1-4), 4 rows (1-4); FirstColIsCategories defaults true for a Column chart, so
    // col 1 is categories and cols 2/3/4 are 3 series at SeriesIndex 0/1/2 respectively.
    private static GridRange ThreeSeriesRange(Sheet sheet) =>
        new(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 4));

    private static (Workbook Workbook, Sheet Sheet, TestCommandContext Ctx, ChartModel Chart) CreateThreeSeriesChart()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var range = ThreeSeriesRange(sheet);
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        return (wb, sheet, ctx, chart);
    }

    [Fact]
    public void RemoveChartSeriesCommand_RemovesMiddleSeriesAndReindexesRemainingColumns()
    {
        var (_, sheet, ctx, chart) = CreateThreeSeriesChart();

        // Remove the middle series (SeriesIndex 1, worksheet column 3).
        var outcome = new RemoveChartSeriesCommand(sheet.Id, chart.Id, 1).Apply(ctx);

        outcome.Success.Should().BeTrue();
        // Two series remain: the original col-2 series is now SeriesIndex 0, and the original
        // col-4 series (which used to be SeriesIndex 2) is now SeriesIndex 1 -- exactly what the
        // renderer's ShouldRenderColumnAsSeries/GetSeriesIndex trio will read.
        chart.SeriesColumnMappings.Should().BeEquivalentTo(
        [
            new ChartSeriesColumnMapping(0, 2u),
            new ChartSeriesColumnMapping(1, 4u)
        ]);
    }

    [Fact]
    public void RemoveChartSeriesCommand_RemapsOverridesAboveRemovedIndexAndDropsOverridesAtRemovedIndex()
    {
        var (_, sheet, ctx, chart) = CreateThreeSeriesChart();
        // SeriesIndex 0 (untouched, below the removed index), 1 (the one being removed), and 2
        // (above the removed index, must shift down to 1) each carry a SeriesIndex-keyed override.
        chart.SeriesOrderOverrides.Add(new ChartSeriesOrderOverride(0, 5));
        chart.SeriesOrderOverrides.Add(new ChartSeriesOrderOverride(1, 6));
        chart.SeriesOrderOverrides.Add(new ChartSeriesOrderOverride(2, 7));
        chart.PointMarkerFormats.Add(new ChartPointMarkerFormat(2, 0, ChartMarkerStyle.Diamond));
        chart.SecondaryAxisSeriesIndexes.Add(2);
        chart.TrendlineSeriesIndex = 2;
        chart.ShowLinearTrendline = true;

        var outcome = new RemoveChartSeriesCommand(sheet.Id, chart.Id, 1).Apply(ctx);

        outcome.Success.Should().BeTrue();
        chart.SeriesOrderOverrides.Should().BeEquivalentTo(
        [
            new ChartSeriesOrderOverride(0, 5),
            new ChartSeriesOrderOverride(1, 7) // was SeriesIndex 2, remapped down to 1
        ]);
        chart.PointMarkerFormats.Should().ContainSingle()
            .Which.SeriesIndex.Should().Be(1); // was 2
        chart.SecondaryAxisSeriesIndexes.Should().Equal(1); // was [2]
        chart.TrendlineSeriesIndex.Should().Be(1); // was 2
        chart.ShowLinearTrendline.Should().BeTrue(); // trendline pointed above the removed index, survives

        // Undo restores everything, including the mappings/overrides at their ORIGINAL indexes.
        new RemoveChartSeriesCommand(sheet.Id, chart.Id, 1).Revert(ctx);
    }

    [Fact]
    public void RemoveChartSeriesCommand_DropsTrendlineWhenItPointedAtTheRemovedSeries()
    {
        var (_, sheet, ctx, chart) = CreateThreeSeriesChart();
        chart.TrendlineSeriesIndex = 1;
        chart.ShowLinearTrendline = true;

        var outcome = new RemoveChartSeriesCommand(sheet.Id, chart.Id, 1).Apply(ctx);

        outcome.Success.Should().BeTrue();
        chart.ShowLinearTrendline.Should().BeFalse();
    }

    [Fact]
    public void RemoveChartSeriesCommand_IsUndoable()
    {
        var (_, sheet, ctx, chart) = CreateThreeSeriesChart();
        chart.SeriesOrderOverrides.Add(new ChartSeriesOrderOverride(2, 9));
        var command = new RemoveChartSeriesCommand(sheet.Id, chart.Id, 1);

        command.Apply(ctx).Success.Should().BeTrue();
        chart.SeriesColumnMappings.Should().HaveCount(2);

        command.Revert(ctx);

        chart.SeriesColumnMappings.Should().BeEmpty(); // back to "no authoritative mapping" (natural order)
        chart.SeriesOrderOverrides.Should().ContainSingle()
            .Which.SeriesIndex.Should().Be(2);
    }

    [Fact]
    public void RemoveChartSeriesCommand_RejectsWhenOnlyOneSeriesRemains()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        // 2 columns, FirstColIsCategories=true -> 1 series only.
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];

        var outcome = new RemoveChartSeriesCommand(sheet.Id, chart.Id, 0).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("at least one");
    }

    [Fact]
    public void RemoveChartSeriesCommand_RejectsSeriesInRows()
    {
        var (_, sheet, ctx, chart) = CreateThreeSeriesChart();
        chart.SeriesInRows = true;

        var outcome = new RemoveChartSeriesCommand(sheet.Id, chart.Id, 0).Apply(ctx);

        outcome.Success.Should().BeFalse();
        chart.SeriesColumnMappings.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ChartType.Scatter)]
    [InlineData(ChartType.Bubble)]
    public void RemoveChartSeriesCommand_RejectsPairedColumnChartTypes(ChartType chartType)
    {
        var (_, sheet, ctx, chart) = CreateThreeSeriesChart();
        chart.Type = chartType;

        var outcome = new RemoveChartSeriesCommand(sheet.Id, chart.Id, 0).Apply(ctx);

        outcome.Success.Should().BeFalse();
        chart.SeriesColumnMappings.Should().BeEmpty();
    }

    [Fact]
    public void RemoveChartSeriesCommand_RejectsOutOfRangeIndex()
    {
        var (_, sheet, ctx, chart) = CreateThreeSeriesChart();

        var outcome = new RemoveChartSeriesCommand(sheet.Id, chart.Id, 5).Apply(ctx);

        outcome.Success.Should().BeFalse();
    }

    [Fact]
    public void RemoveChartSeriesCommand_RejectsPivotCharts()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var range = ThreeSeriesRange(sheet);
        sheet.Charts.Add(new ChartModel
        {
            Type = ChartType.Column,
            DataRange = range,
            IsPivotChart = true,
            PivotTableName = "PivotTable1"
        });
        var chart = sheet.Charts[0];

        var outcome = new RemoveChartSeriesCommand(sheet.Id, chart.Id, 0).Apply(ctx);

        outcome.Success.Should().BeFalse();
        outcome.ErrorMessage.Should().Contain("PivotChart");
    }
}
