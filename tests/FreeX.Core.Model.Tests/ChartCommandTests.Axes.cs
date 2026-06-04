using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class ChartCommandTests
{

    [Fact]
    public void SetChartLayoutCommand_ClearsAxisBounds()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Scatter, "Sales").Apply(ctx);

        new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                XAxisMinimum: 0,
                XAxisMaximum: 10,
                XAxisMajorUnit: 2,
                XAxisMinorUnit: 1,
                XAxisLogScale: true,
                YAxisMinimum: -5,
                YAxisMaximum: 25,
                YAxisMajorUnit: 5,
                YAxisMinorUnit: 2.5,
                YAxisLogScale: true)).Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(ClearXAxisBounds: true, ClearYAxisBounds: true));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].XAxisMinimum.Should().BeNull();
        sheet.Charts[0].XAxisMaximum.Should().BeNull();
        sheet.Charts[0].XAxisMajorUnit.Should().BeNull();
        sheet.Charts[0].XAxisMinorUnit.Should().BeNull();
        sheet.Charts[0].XAxisLogScale.Should().BeFalse();
        sheet.Charts[0].YAxisMinimum.Should().BeNull();
        sheet.Charts[0].YAxisMaximum.Should().BeNull();
        sheet.Charts[0].YAxisMajorUnit.Should().BeNull();
        sheet.Charts[0].YAxisMinorUnit.Should().BeNull();
        sheet.Charts[0].YAxisLogScale.Should().BeFalse();

        command.Revert(ctx);

        sheet.Charts[0].XAxisMinimum.Should().Be(0);
        sheet.Charts[0].XAxisMaximum.Should().Be(10);
        sheet.Charts[0].XAxisMajorUnit.Should().Be(2);
        sheet.Charts[0].XAxisMinorUnit.Should().Be(1);
        sheet.Charts[0].XAxisLogScale.Should().BeTrue();
        sheet.Charts[0].YAxisMinimum.Should().Be(-5);
        sheet.Charts[0].YAxisMaximum.Should().Be(25);
        sheet.Charts[0].YAxisMajorUnit.Should().Be(5);
        sheet.Charts[0].YAxisMinorUnit.Should().Be(2.5);
        sheet.Charts[0].YAxisLogScale.Should().BeTrue();
    }

    [Fact]
    public void SetChartLayoutCommand_ClearsUnsupportedAxisBounds()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Pie, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                XAxisMinimum: 0,
                XAxisMaximum: 10,
                XAxisMajorUnit: 2,
                XAxisMinorUnit: 1,
                XAxisLogScale: true,
                XAxisNumberFormat: ChartDataLabelNumberFormat.Currency,
                ShowXAxisMajorGridlines: true,
                ShowXAxisMinorGridlines: true,
                XAxisMajorGridlineColor: new CellColor(200, 200, 200),
                XAxisMinorGridlineColor: new CellColor(230, 230, 230),
                XAxisGridlineThickness: 1.5,
                XAxisMajorTickStyle: ChartAxisTickStyle.Cross,
                XAxisMinorTickStyle: ChartAxisTickStyle.Inside,
                ShowXAxisLabels: false,
                XAxisLabelTextColor: new CellColor(70, 70, 70),
                XAxisLabelFontSize: 10,
                XAxisLabelAngle: -45,
                XAxisLineColor: new CellColor(10, 20, 30),
                XAxisLineThickness: 2.5,
                YAxisMinimum: -5,
                YAxisMaximum: 25,
                YAxisMajorUnit: 5,
                YAxisMinorUnit: 2.5,
                YAxisLogScale: true,
                YAxisNumberFormat: ChartDataLabelNumberFormat.Percent,
                ShowYAxisMajorGridlines: true,
                ShowYAxisMinorGridlines: true,
                YAxisMajorGridlineColor: new CellColor(190, 190, 190),
                YAxisMinorGridlineColor: new CellColor(225, 225, 225),
                YAxisGridlineThickness: 2,
                YAxisMajorTickStyle: ChartAxisTickStyle.Cross,
                YAxisMinorTickStyle: ChartAxisTickStyle.Inside,
                ShowYAxisLabels: false,
                YAxisLabelTextColor: new CellColor(80, 80, 80),
                YAxisLabelFontSize: 12,
                YAxisLabelAngle: 90,
                YAxisLineColor: new CellColor(40, 50, 60),
                YAxisLineThickness: 3.5));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].XAxisMinimum.Should().BeNull();
        sheet.Charts[0].XAxisMaximum.Should().BeNull();
        sheet.Charts[0].XAxisMajorUnit.Should().BeNull();
        sheet.Charts[0].XAxisMinorUnit.Should().BeNull();
        sheet.Charts[0].XAxisLogScale.Should().BeFalse();
        sheet.Charts[0].XAxisNumberFormat.Should().Be(ChartDataLabelNumberFormat.General);
        sheet.Charts[0].ShowXAxisMajorGridlines.Should().BeFalse();
        sheet.Charts[0].ShowXAxisMinorGridlines.Should().BeFalse();
        sheet.Charts[0].XAxisMajorGridlineColor.Should().BeNull();
        sheet.Charts[0].XAxisMinorGridlineColor.Should().BeNull();
        sheet.Charts[0].XAxisGridlineThickness.Should().Be(1);
        sheet.Charts[0].XAxisMajorTickStyle.Should().Be(ChartAxisTickStyle.Outside);
        sheet.Charts[0].XAxisMinorTickStyle.Should().Be(ChartAxisTickStyle.None);
        sheet.Charts[0].ShowXAxisLabels.Should().BeTrue();
        sheet.Charts[0].XAxisLabelTextColor.Should().BeNull();
        sheet.Charts[0].XAxisLabelFontSize.Should().Be(11);
        sheet.Charts[0].XAxisLabelAngle.Should().Be(0);
        sheet.Charts[0].XAxisLineColor.Should().BeNull();
        sheet.Charts[0].XAxisLineThickness.Should().Be(1);
        sheet.Charts[0].YAxisMinimum.Should().BeNull();
        sheet.Charts[0].YAxisMaximum.Should().BeNull();
        sheet.Charts[0].YAxisMajorUnit.Should().BeNull();
        sheet.Charts[0].YAxisMinorUnit.Should().BeNull();
        sheet.Charts[0].YAxisLogScale.Should().BeFalse();
        sheet.Charts[0].YAxisNumberFormat.Should().Be(ChartDataLabelNumberFormat.General);
        sheet.Charts[0].ShowYAxisMajorGridlines.Should().BeFalse();
        sheet.Charts[0].ShowYAxisMinorGridlines.Should().BeFalse();
        sheet.Charts[0].YAxisMajorGridlineColor.Should().BeNull();
        sheet.Charts[0].YAxisMinorGridlineColor.Should().BeNull();
        sheet.Charts[0].YAxisGridlineThickness.Should().Be(1);
        sheet.Charts[0].YAxisMajorTickStyle.Should().Be(ChartAxisTickStyle.Outside);
        sheet.Charts[0].YAxisMinorTickStyle.Should().Be(ChartAxisTickStyle.None);
        sheet.Charts[0].ShowYAxisLabels.Should().BeTrue();
        sheet.Charts[0].YAxisLabelTextColor.Should().BeNull();
        sheet.Charts[0].YAxisLabelFontSize.Should().Be(11);
        sheet.Charts[0].YAxisLabelAngle.Should().Be(0);
        sheet.Charts[0].YAxisLineColor.Should().BeNull();
        sheet.Charts[0].YAxisLineThickness.Should().Be(1);
    }

    [Fact]
    public void SetChartLayoutCommand_ClearsAxisTitlesWhenChartHasNoAxes()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Pie, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                XAxisTitle: "Quarter",
                YAxisTitle: "Amount",
                AxisTitleTextColor: new CellColor(89, 89, 89),
                AxisTitleFontSize: 18));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].XAxisTitle.Should().BeNull();
        sheet.Charts[0].YAxisTitle.Should().BeNull();
        sheet.Charts[0].AxisTitleTextColor.Should().BeNull();
        sheet.Charts[0].AxisTitleFontSize.Should().Be(12);
    }
}
