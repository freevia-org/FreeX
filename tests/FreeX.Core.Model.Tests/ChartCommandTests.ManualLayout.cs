using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class ChartCommandTests
{
    private static (Workbook Wb, Sheet Sheet, TestCommandContext Ctx) ChartWithData()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);
        return (wb, sheet, ctx);
    }

    [Fact]
    public void SetChartLayoutCommand_AppliesManualLayouts_AndUndoRestoresPreviousOnes()
    {
        var (_, sheet, ctx) = ChartWithData();
        var chart = sheet.Charts[0];
        var legendBefore = new ChartManualLayoutModel { XMode = "edge", YMode = "edge", X = 0.1, Y = 0.2 };
        chart.LegendLayout = legendBefore;

        var title = new ChartManualLayoutModel { XMode = "edge", YMode = "edge", X = 0.3, Y = 0.05 };
        var legend = new ChartManualLayoutModel { XMode = "edge", YMode = "edge", X = 0.7, Y = 0.4, Width = 0.2, Height = 0.3 };
        var plot = new ChartManualLayoutModel { LayoutTarget = "inner", XMode = "edge", YMode = "edge", X = 0.1, Y = 0.2, Width = 0.7, Height = 0.6 };
        var command = new SetChartLayoutCommand(
            sheet.Id,
            chart.Id,
            new ChartLayoutOptions { TitleLayout = title, LegendLayout = legend, PlotAreaLayout = plot });

        command.Apply(ctx).Success.Should().BeTrue();
        chart.TitleLayout.Should().BeSameAs(title);
        chart.LegendLayout.Should().BeSameAs(legend);
        chart.PlotAreaLayout.Should().BeSameAs(plot);

        command.Revert(ctx);
        chart.TitleLayout.Should().BeNull();
        chart.LegendLayout.Should().BeSameAs(legendBefore);
        chart.PlotAreaLayout.Should().BeNull();
    }

    [Fact]
    public void SetChartLayoutCommand_ClearManualLayouts_ResetsToAutomatic_AndUndoRestores()
    {
        var (_, sheet, ctx) = ChartWithData();
        var chart = sheet.Charts[0];
        var axisTitle = new ChartManualLayoutModel { XMode = "edge", YMode = "edge", X = 0.4, Y = 0.9 };
        chart.XAxisTitleLayout = axisTitle;
        chart.TitleLayout = new ChartManualLayoutModel { XMode = "edge", YMode = "edge", X = 0.3, Y = 0.05 };

        var command = new SetChartLayoutCommand(
            sheet.Id,
            chart.Id,
            new ChartLayoutOptions { ClearManualLayouts = ChartManualLayoutTargets.XAxisTitle | ChartManualLayoutTargets.Title });

        command.Apply(ctx).Success.Should().BeTrue();
        chart.XAxisTitleLayout.Should().BeNull();
        chart.TitleLayout.Should().BeNull();

        command.Revert(ctx);
        chart.XAxisTitleLayout.Should().BeSameAs(axisTitle);
        chart.TitleLayout.Should().NotBeNull();
    }

    [Fact]
    public void SetChartLayoutCommand_KeepsLayoutOnlyPointDataLabelFormats()
    {
        var (_, sheet, ctx) = ChartWithData();
        var chart = sheet.Charts[0];
        var layout = new ChartManualLayoutModel { XMode = "factor", YMode = "factor", X = 0.05, Y = -0.02 };

        var command = new SetChartLayoutCommand(
            sheet.Id,
            chart.Id,
            new ChartLayoutOptions(PointDataLabelFormats: [new ChartPointDataLabelFormat(0, 1) { Layout = layout }]));

        command.Apply(ctx).Success.Should().BeTrue();
        chart.PointDataLabelFormats.Should().ContainSingle(f => f.PointIndex == 1 && f.Layout == layout);

        command.Revert(ctx);
        chart.PointDataLabelFormats.Should().BeEmpty();
    }
}
