using FluentAssertions;
using FreeX.Core.Commands;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// r259: the PivotChart Options dialog. Every control is pre-filled from the chart, so OK without
/// changing anything writes all ten properties back as they were.
///
/// <para>The data-table case is the one worth its own tests: <see cref="ChartDataTableModel"/> is a
/// CLASS captured with <c>Clone</c>, so a decision using <c>==</c> would compare references and never
/// fire -- the same failure r231 predicted for the save records, one level worse, since a class has
/// no value semantics at all to fall back on.</para>
/// </summary>
public sealed class R259_PivotChartOptionsNoOpTests
{
    private static (Sheet Sheet, TestCommandContext Ctx, ChartModel Chart) SetUpPivotChart()
    {
        var workbook = new Workbook("PivotChartOptionsTest");
        var sheet = workbook.AddSheet("Data");
        var chart = new ChartModel
        {
            IsPivotChart = true,
            PivotTableName = "PivotTable1",
            DataRange = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 2)),
            ChartStyleId = 2,
            ShowPivotChartFieldButtons = true,
            RoundedCorners = false,
        };
        sheet.Charts.Add(chart);

        return (sheet, new TestCommandContext(workbook), chart);
    }

    private static ConfigurePivotChartOptionsCommand Reapply(
        Sheet sheet,
        ChartModel chart,
        int? chartStyleId = null,
        bool? showFieldButtons = null,
        bool? showDataTable = null,
        bool? showDataTableLegendKeys = null) =>
        new(
            sheet.Id,
            chart.Id,
            chartStyleId ?? chart.ChartStyleId,
            showFieldButtons ?? chart.ShowPivotChartFieldButtons,
            showReportFilterButtons: chart.ShowPivotChartReportFilterButtons,
            showAxisFieldButtons: chart.ShowPivotChartAxisFieldButtons,
            showValueFieldButtons: chart.ShowPivotChartValueFieldButtons,
            showDataTable: showDataTable,
            showDataTableLegendKeys: showDataTableLegendKeys,
            roundedCorners: chart.RoundedCorners,
            showHiddenData: chart.ShowDataInHiddenRowsAndColumns,
            blankDisplayMode: chart.BlankDisplayMode);

    [Fact]
    public void ReapplyingTheCurrentOptionsIsANoOp()
    {
        var (sheet, ctx, chart) = SetUpPivotChart();

        Reapply(sheet, chart).Apply(ctx)
            .IsNoOp.Should().BeTrue("every property is handed back exactly as the chart holds it");
    }

    [Fact]
    public void ChangingTheChartStyleIsNotANoOp()
    {
        var (sheet, ctx, chart) = SetUpPivotChart();

        Reapply(sheet, chart, chartStyleId: 5).Apply(ctx)
            .IsNoOp.Should().BeFalse();
        chart.ChartStyleId.Should().Be(5);
    }

    [Fact]
    public void TurningFieldButtonsOffIsNotANoOp()
    {
        var (sheet, ctx, chart) = SetUpPivotChart();

        Reapply(sheet, chart, showFieldButtons: false).Apply(ctx)
            .IsNoOp.Should().BeFalse();
    }

    [Fact]
    public void AddingADataTableIsNotANoOp()
    {
        var (sheet, ctx, chart) = SetUpPivotChart();
        chart.DataTable.Should().BeNull();

        Reapply(sheet, chart, showDataTable: true).Apply(ctx)
            .IsNoOp.Should().BeFalse("a data table appears where there was none");
        chart.DataTable.Should().NotBeNull();
    }

    /// <summary>
    /// The case a reference comparison gets wrong: the chart already has a data table, and the
    /// command replaces it with an equal one. Capture is by Clone, so the captured instance and the
    /// current instance are never the same object.
    /// </summary>
    [Fact]
    public void ReapplyingAnExistingDataTableIsANoOp()
    {
        var (sheet, ctx, chart) = SetUpPivotChart();

        Reapply(sheet, chart, showDataTable: true, showDataTableLegendKeys: true).Apply(ctx)
            .IsNoOp.Should().BeFalse();

        Reapply(sheet, chart, showDataTable: true, showDataTableLegendKeys: true).Apply(ctx)
            .IsNoOp.Should().BeTrue("the data table already has exactly these settings");
    }

    [Fact]
    public void ChangingADataTableSettingIsNotANoOp()
    {
        var (sheet, ctx, chart) = SetUpPivotChart();

        Reapply(sheet, chart, showDataTable: true, showDataTableLegendKeys: false).Apply(ctx);

        Reapply(sheet, chart, showDataTable: true, showDataTableLegendKeys: true).Apply(ctx)
            .IsNoOp.Should().BeFalse("ShowLegendKeys flips on the existing data table");
    }
}
