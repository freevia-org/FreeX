using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class ChartCommandTests
{

    [Fact]
    public void SetChartStyleCommand_UpdatesStyleAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 3));
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = range,
            ChartStyleId = 4
        };
        sheet.Charts.Add(chart);

        var command = new SetChartStyleCommand(sheet.Id, chart.Id, 99);

        command.Apply(ctx).Success.Should().BeTrue();
        chart.ChartStyleId.Should().Be(48);

        command.Revert(ctx);
        chart.ChartStyleId.Should().Be(4);
    }

    [Fact]
    public void SetChartStyleCommand_AllowsClearingStyle()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 3));
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = range,
            ChartStyleId = 10
        };
        sheet.Charts.Add(chart);

        new SetChartStyleCommand(sheet.Id, chart.Id, null).Apply(ctx).Success.Should().BeTrue();

        chart.ChartStyleId.Should().BeNull();
    }

    [Fact]
    public void SetChartStyleCommand_RejectsProtectedSheetWithoutEditObjectsPermission()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var range = CreateChartRange(sheet);
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        sheet.IsProtected = true;

        var outcome = new SetChartStyleCommand(sheet.Id, chart.Id, 5).Apply(ctx);

        outcome.Success.Should().BeFalse();
        chart.ChartStyleId.Should().BeNull();
    }

    [Fact]
    public void SetChartLayoutCommand_RejectsProtectedSheetWithoutEditObjectsPermission()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var range = CreateChartRange(sheet);
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        sheet.IsProtected = true;

        var outcome = new SetChartLayoutCommand(
            sheet.Id,
            chart.Id,
            new ChartLayoutOptions(Title: "Blocked")).Apply(ctx);

        outcome.Success.Should().BeFalse();
        chart.Title.Should().Be("Sales");
    }

    [Fact]
    public void SetChartLayoutCommand_UpdatesTitleAxesLegendAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 4));
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Old").Apply(ctx);
        var chartId = sheet.Charts[0].Id;

        var command = new SetChartLayoutCommand(
            sheet.Id,
            chartId,
            new ChartLayoutOptions(
                Title: "Revenue",
                XAxisTitle: "Quarter",
                YAxisTitle: "Amount",
                ChartTitleTextColor: new CellColor(31, 78, 121),
                ChartTitleFontSize: 18,
                AxisTitleTextColor: new CellColor(89, 89, 89),
                AxisTitleFontSize: 12,
                ChartAreaFillColor: new CellColor(245, 245, 245),
                PlotAreaFillColor: new CellColor(250, 252, 255),
                PlotAreaBorderColor: new CellColor(120, 120, 120),
                PlotAreaBorderThickness: 2.25,
                LegendTextColor: new CellColor(40, 40, 40),
                LegendFillColor: new CellColor(248, 248, 248),
                LegendBorderColor: new CellColor(180, 180, 180),
                LegendBorderThickness: 1.25,
                LegendFontSize: 11,
                DoughnutHoleSize: 0.72,
                FirstSliceAngle: 135,
                ExplodedSliceIndex: 1,
                ExplodedSliceDistance: 0.18,
                XAxisMinimum: 0,
                XAxisMaximum: 10,
                XAxisMajorUnit: 2,
                XAxisMinorUnit: 1,
                XAxisLogScale: true,
                XAxisNumberFormat: ChartDataLabelNumberFormat.Number,
                ShowXAxisMajorGridlines: true,
                ShowXAxisMinorGridlines: true,
                XAxisMajorGridlineColor: new CellColor(200, 200, 200),
                XAxisMinorGridlineColor: new CellColor(230, 230, 230),
                XAxisGridlineThickness: 1.5,
                XAxisMajorTickStyle: ChartAxisTickStyle.Outside,
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
                YAxisNumberFormat: ChartDataLabelNumberFormat.Currency,
                ShowYAxisMajorGridlines: true,
                ShowYAxisMinorGridlines: true,
                YAxisMajorGridlineColor: new CellColor(190, 190, 190),
                YAxisMinorGridlineColor: new CellColor(225, 225, 225),
                YAxisGridlineThickness: 2,
                YAxisMajorTickStyle: ChartAxisTickStyle.Cross,
                YAxisMinorTickStyle: ChartAxisTickStyle.None,
                ShowYAxisLabels: false,
                YAxisLabelTextColor: new CellColor(80, 80, 80),
                YAxisLabelFontSize: 11,
                YAxisLabelAngle: 90,
                YAxisLineColor: new CellColor(40, 50, 60),
                YAxisLineThickness: 3.5,
                LegendPosition: ChartLegendPosition.Bottom,
                LegendOverlay: true,
                ShowLegend: true,
                ShowDataLabels: true,
                DataLabelPosition: ChartDataLabelPosition.OutsideEnd,
                ShowDataLabelCategoryName: true,
                ShowDataLabelSeriesName: true,
                ShowDataLabelPercentage: true,
                DataLabelSeparator: ChartDataLabelSeparator.NewLine,
                DataLabelNumberFormat: ChartDataLabelNumberFormat.Currency,
                ShowDataLabelCallouts: true,
                DataLabelFillColor: new CellColor(255, 255, 225),
                DataLabelBorderColor: new CellColor(128, 128, 128),
                DataLabelTextColor: new CellColor(30, 30, 30),
                DataLabelBorderThickness: 1.5,
                DataLabelFontSize: 13,
                DataLabelAngle: -35,
                ShowLinearTrendline: true,
                TrendlineType: ChartTrendlineType.Power,
                TrendlinePeriod: 3,
                TrendlineOrder: 4,
                ShowTrendlineEquation: true,
                ShowTrendlineRSquared: true,
                TrendlineColor: new CellColor(217, 83, 25),
                TrendlineThickness: 2.5,
                TrendlineDashStyle: ChartLineDashStyle.Solid,
                ShowSecondaryAxis: true,
                SecondaryAxisSeriesIndexes: [1],
                ComboLineSeriesIndexes: [2],
                SeriesFormats:
                [
                    new ChartSeriesFormat(
                        0,
                        FillColor: new CellColor(0, 114, 178),
                        StrokeColor: new CellColor(0, 0, 0),
                        StrokeThickness: 2.5,
                        DashStyle: ChartLineDashStyle.Dot,
                        MarkerStyle: ChartMarkerStyle.Diamond,
                        MarkerSize: 7)
                ],
                UseComboLineForSecondarySeries: true));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].Title.Should().Be("Revenue");
        sheet.Charts[0].XAxisTitle.Should().Be("Quarter");
        sheet.Charts[0].YAxisTitle.Should().Be("Amount");
        sheet.Charts[0].ChartTitleTextColor.Should().Be(new CellColor(31, 78, 121));
        sheet.Charts[0].ChartTitleFontSize.Should().Be(18);
        sheet.Charts[0].AxisTitleTextColor.Should().Be(new CellColor(89, 89, 89));
        sheet.Charts[0].AxisTitleFontSize.Should().Be(12);
        sheet.Charts[0].ChartAreaFillColor.Should().Be(new CellColor(245, 245, 245));
        sheet.Charts[0].PlotAreaFillColor.Should().Be(new CellColor(250, 252, 255));
        sheet.Charts[0].PlotAreaBorderColor.Should().Be(new CellColor(120, 120, 120));
        sheet.Charts[0].PlotAreaBorderThickness.Should().Be(2.25);
        sheet.Charts[0].LegendTextColor.Should().Be(new CellColor(40, 40, 40));
        sheet.Charts[0].LegendFillColor.Should().Be(new CellColor(248, 248, 248));
        sheet.Charts[0].LegendBorderColor.Should().Be(new CellColor(180, 180, 180));
        sheet.Charts[0].LegendBorderThickness.Should().Be(1.25);
        sheet.Charts[0].LegendFontSize.Should().Be(11);
        sheet.Charts[0].DoughnutHoleSize.Should().Be(0.55);
        sheet.Charts[0].FirstSliceAngle.Should().Be(0);
        sheet.Charts[0].ExplodedSliceIndex.Should().Be(-1);
        sheet.Charts[0].ExplodedSliceDistance.Should().Be(0.1);
        sheet.Charts[0].XAxisMinimum.Should().BeNull();
        sheet.Charts[0].XAxisMaximum.Should().BeNull();
        sheet.Charts[0].XAxisMajorUnit.Should().BeNull();
        sheet.Charts[0].XAxisMinorUnit.Should().BeNull();
        sheet.Charts[0].XAxisLogScale.Should().BeFalse();
        sheet.Charts[0].XAxisNumberFormat.Should().Be(ChartDataLabelNumberFormat.Number);
        sheet.Charts[0].ShowXAxisMajorGridlines.Should().BeTrue();
        sheet.Charts[0].ShowXAxisMinorGridlines.Should().BeTrue();
        sheet.Charts[0].XAxisMajorGridlineColor.Should().Be(new CellColor(200, 200, 200));
        sheet.Charts[0].XAxisMinorGridlineColor.Should().Be(new CellColor(230, 230, 230));
        sheet.Charts[0].XAxisGridlineThickness.Should().Be(1.5);
        sheet.Charts[0].XAxisMajorTickStyle.Should().Be(ChartAxisTickStyle.Outside);
        sheet.Charts[0].XAxisMinorTickStyle.Should().Be(ChartAxisTickStyle.Inside);
        sheet.Charts[0].ShowXAxisLabels.Should().BeFalse();
        sheet.Charts[0].XAxisLabelTextColor.Should().Be(new CellColor(70, 70, 70));
        sheet.Charts[0].XAxisLabelFontSize.Should().Be(10);
        sheet.Charts[0].XAxisLabelAngle.Should().Be(-45);
        sheet.Charts[0].XAxisLineColor.Should().Be(new CellColor(10, 20, 30));
        sheet.Charts[0].XAxisLineThickness.Should().Be(2.5);
        sheet.Charts[0].YAxisMinimum.Should().Be(-5);
        sheet.Charts[0].YAxisMaximum.Should().Be(25);
        sheet.Charts[0].YAxisMajorUnit.Should().Be(5);
        sheet.Charts[0].YAxisMinorUnit.Should().Be(2.5);
        sheet.Charts[0].YAxisLogScale.Should().BeTrue();
        sheet.Charts[0].YAxisNumberFormat.Should().Be(ChartDataLabelNumberFormat.Currency);
        sheet.Charts[0].ShowYAxisMajorGridlines.Should().BeTrue();
        sheet.Charts[0].ShowYAxisMinorGridlines.Should().BeTrue();
        sheet.Charts[0].YAxisMajorGridlineColor.Should().Be(new CellColor(190, 190, 190));
        sheet.Charts[0].YAxisMinorGridlineColor.Should().Be(new CellColor(225, 225, 225));
        sheet.Charts[0].YAxisGridlineThickness.Should().Be(2);
        sheet.Charts[0].YAxisMajorTickStyle.Should().Be(ChartAxisTickStyle.Cross);
        sheet.Charts[0].YAxisMinorTickStyle.Should().Be(ChartAxisTickStyle.None);
        sheet.Charts[0].ShowYAxisLabels.Should().BeFalse();
        sheet.Charts[0].YAxisLabelTextColor.Should().Be(new CellColor(80, 80, 80));
        sheet.Charts[0].YAxisLabelFontSize.Should().Be(11);
        sheet.Charts[0].YAxisLabelAngle.Should().Be(90);
        sheet.Charts[0].YAxisLineColor.Should().Be(new CellColor(40, 50, 60));
        sheet.Charts[0].YAxisLineThickness.Should().Be(3.5);
        sheet.Charts[0].LegendPosition.Should().Be(ChartLegendPosition.Bottom);
        sheet.Charts[0].LegendOverlay.Should().BeTrue();
        sheet.Charts[0].ShowLegend.Should().BeTrue();
        sheet.Charts[0].ShowDataLabels.Should().BeTrue();
        sheet.Charts[0].DataLabelPosition.Should().Be(ChartDataLabelPosition.OutsideEnd);
        sheet.Charts[0].ShowDataLabelCategoryName.Should().BeTrue();
        sheet.Charts[0].ShowDataLabelSeriesName.Should().BeTrue();
        sheet.Charts[0].ShowDataLabelPercentage.Should().BeFalse();
        sheet.Charts[0].DataLabelSeparator.Should().Be(ChartDataLabelSeparator.NewLine);
        sheet.Charts[0].DataLabelNumberFormat.Should().Be(ChartDataLabelNumberFormat.Currency);
        sheet.Charts[0].ShowDataLabelCallouts.Should().BeTrue();
        sheet.Charts[0].DataLabelFillColor.Should().Be(new CellColor(255, 255, 225));
        sheet.Charts[0].DataLabelBorderColor.Should().Be(new CellColor(128, 128, 128));
        sheet.Charts[0].DataLabelTextColor.Should().Be(new CellColor(30, 30, 30));
        sheet.Charts[0].DataLabelBorderThickness.Should().Be(1.5);
        sheet.Charts[0].DataLabelFontSize.Should().Be(13);
        sheet.Charts[0].DataLabelAngle.Should().Be(-35);
        sheet.Charts[0].ShowLinearTrendline.Should().BeTrue();
        sheet.Charts[0].TrendlineType.Should().Be(ChartTrendlineType.Power);
        sheet.Charts[0].TrendlinePeriod.Should().Be(3);
        sheet.Charts[0].TrendlineOrder.Should().Be(4);
        sheet.Charts[0].ShowTrendlineEquation.Should().BeTrue();
        sheet.Charts[0].ShowTrendlineRSquared.Should().BeTrue();
        sheet.Charts[0].TrendlineColor.Should().Be(new CellColor(217, 83, 25));
        sheet.Charts[0].TrendlineThickness.Should().Be(2.5);
        sheet.Charts[0].TrendlineDashStyle.Should().Be(ChartLineDashStyle.Solid);
        sheet.Charts[0].ShowSecondaryAxis.Should().BeTrue();
        sheet.Charts[0].SecondaryAxisSeriesIndexes.Should().Equal(1);
        sheet.Charts[0].ComboLineSeriesIndexes.Should().Equal(2);
        sheet.Charts[0].SeriesFormats.Should().ContainSingle().Which.Should().Be(
            new ChartSeriesFormat(
                0,
                FillColor: new CellColor(0, 114, 178),
                StrokeColor: new CellColor(0, 0, 0),
                StrokeThickness: 2.5,
                DashStyle: ChartLineDashStyle.Dot));
        sheet.Charts[0].UseComboLineForSecondarySeries.Should().BeTrue();

        command.Revert(ctx);

        sheet.Charts[0].Title.Should().Be("Old");
        sheet.Charts[0].XAxisTitle.Should().BeNull();
        sheet.Charts[0].YAxisTitle.Should().BeNull();
        sheet.Charts[0].ChartTitleTextColor.Should().BeNull();
        sheet.Charts[0].ChartTitleFontSize.Should().Be(16);
        sheet.Charts[0].AxisTitleTextColor.Should().BeNull();
        sheet.Charts[0].AxisTitleFontSize.Should().Be(12);
        sheet.Charts[0].ChartAreaFillColor.Should().BeNull();
        sheet.Charts[0].PlotAreaFillColor.Should().BeNull();
        sheet.Charts[0].PlotAreaBorderColor.Should().BeNull();
        sheet.Charts[0].PlotAreaBorderThickness.Should().Be(1);
        sheet.Charts[0].LegendTextColor.Should().BeNull();
        sheet.Charts[0].LegendFillColor.Should().BeNull();
        sheet.Charts[0].LegendBorderColor.Should().BeNull();
        sheet.Charts[0].LegendBorderThickness.Should().Be(0);
        sheet.Charts[0].LegendFontSize.Should().Be(12);
        sheet.Charts[0].DoughnutHoleSize.Should().Be(0.55);
        sheet.Charts[0].FirstSliceAngle.Should().Be(0);
        sheet.Charts[0].ExplodedSliceIndex.Should().Be(-1);
        sheet.Charts[0].ExplodedSliceDistance.Should().Be(0.1);
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
        sheet.Charts[0].LegendPosition.Should().Be(ChartLegendPosition.Right);
        sheet.Charts[0].LegendOverlay.Should().BeFalse();
        sheet.Charts[0].ShowLegend.Should().BeTrue();
        sheet.Charts[0].ShowDataLabels.Should().BeFalse();
        sheet.Charts[0].DataLabelPosition.Should().Be(ChartDataLabelPosition.BestFit);
        sheet.Charts[0].ShowDataLabelCategoryName.Should().BeFalse();
        sheet.Charts[0].ShowDataLabelSeriesName.Should().BeFalse();
        sheet.Charts[0].ShowDataLabelPercentage.Should().BeFalse();
        sheet.Charts[0].DataLabelSeparator.Should().Be(ChartDataLabelSeparator.Comma);
        sheet.Charts[0].DataLabelNumberFormat.Should().Be(ChartDataLabelNumberFormat.General);
        sheet.Charts[0].ShowDataLabelCallouts.Should().BeFalse();
        sheet.Charts[0].DataLabelFillColor.Should().BeNull();
        sheet.Charts[0].DataLabelBorderColor.Should().BeNull();
        sheet.Charts[0].DataLabelTextColor.Should().BeNull();
        sheet.Charts[0].DataLabelBorderThickness.Should().Be(0);
        sheet.Charts[0].DataLabelFontSize.Should().Be(11);
        sheet.Charts[0].DataLabelAngle.Should().Be(0);
        sheet.Charts[0].ShowLinearTrendline.Should().BeFalse();
        sheet.Charts[0].TrendlineType.Should().Be(ChartTrendlineType.Linear);
        sheet.Charts[0].TrendlinePeriod.Should().Be(2);
        sheet.Charts[0].TrendlineOrder.Should().Be(2);
        sheet.Charts[0].ShowTrendlineEquation.Should().BeFalse();
        sheet.Charts[0].ShowTrendlineRSquared.Should().BeFalse();
        sheet.Charts[0].TrendlineColor.Should().BeNull();
        sheet.Charts[0].TrendlineThickness.Should().Be(1.5);
        sheet.Charts[0].TrendlineDashStyle.Should().Be(ChartLineDashStyle.Dash);
        sheet.Charts[0].ShowSecondaryAxis.Should().BeFalse();
        sheet.Charts[0].SecondaryAxisSeriesIndexes.Should().BeEmpty();
        sheet.Charts[0].ComboLineSeriesIndexes.Should().BeEmpty();
        sheet.Charts[0].SeriesFormats.Should().BeEmpty();
        sheet.Charts[0].UseComboLineForSecondarySeries.Should().BeFalse();
    }

    [Fact]
    public void SetChartLayoutCommand_ExplicitLegendPosition_SetsProvenanceFlagAndUndoRestores()
    {
        // io-chart-legend-6-3 (round 67): the command is the only place that can distinguish a
        // freshly-authored chart left at the ChartLegendPosition.Right C# default from a user who
        // explicitly picked Right through the Legend Position command -- it must set
        // LegendPositionExplicit whenever the caller supplies a LegendPosition, and undo must put
        // the flag back the way it was.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var range = CreateChartRange(sheet);
        new AddChartCommand(sheet.Id, range, ChartType.StackedColumn, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];
        chart.LegendPositionExplicit.Should().BeNull("a freshly-added chart was never round-tripped or explicitly edited");

        var command = new SetChartLayoutCommand(
            sheet.Id,
            chart.Id,
            new ChartLayoutOptions(LegendPosition: ChartLegendPosition.Right));

        command.Apply(ctx).Success.Should().BeTrue();

        chart.LegendPosition.Should().Be(ChartLegendPosition.Right);
        chart.LegendPositionExplicit.Should().BeTrue(
            "the user explicitly chose Right through the layout command");

        command.Revert(ctx);

        chart.LegendPosition.Should().Be(ChartLegendPosition.Right);
        chart.LegendPositionExplicit.Should().BeNull(
            "undo must restore the pre-command provenance flag, not just the position value");
    }

    [Fact]
    public void SetChartLayoutCommand_RgbColorsClearThemeRefsAndUndoRestoresThem()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 3));
        new AddChartCommand(sheet.Id, range, ChartType.Column).Apply(ctx);
        var chart = sheet.Charts[0];
        chart.ChartAreaFillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1);
        chart.LegendTextThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark1);
        chart.DataLabelTextThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark2);
        chart.TrendlineThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            chart.Id,
            new ChartLayoutOptions(
                ChartAreaFillColor: new CellColor(245, 245, 245),
                LegendTextColor: new CellColor(40, 40, 40),
                DataLabelTextColor: new CellColor(30, 30, 30),
                TrendlineColor: new CellColor(217, 83, 25)));

        command.Apply(ctx).Success.Should().BeTrue();

        chart.ChartAreaFillThemeColor.Should().BeNull();
        chart.LegendTextThemeColor.Should().BeNull();
        chart.DataLabelTextThemeColor.Should().BeNull();
        chart.TrendlineThemeColor.Should().BeNull();

        command.Revert(ctx);

        chart.ChartAreaFillThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1));
        chart.LegendTextThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark1));
        chart.DataLabelTextThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Dark2));
        chart.TrendlineThemeColor.Should().Be(new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2));
    }

    [Fact]
    public void SetChartLayoutCommand_RejectsMissingChart()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            Guid.NewGuid(),
            new ChartLayoutOptions(Title: "Revenue"));

        command.Apply(ctx).Success.Should().BeFalse();
    }

    [Fact]
    public void SetChartLayoutCommand_ClearsPieAndDoughnutStateWhenUnsupported()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                DoughnutHoleSize: 0.72,
                FirstSliceAngle: 135,
                ExplodedSliceIndex: 1,
                ExplodedSliceDistance: 0.18));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].DoughnutHoleSize.Should().Be(0.55);
        sheet.Charts[0].FirstSliceAngle.Should().Be(0);
        sheet.Charts[0].ExplodedSliceIndex.Should().Be(-1);
        sheet.Charts[0].ExplodedSliceDistance.Should().Be(0.1);
    }

    [Fact]
    public void SetChartLayoutCommand_ReplacesInvalidChartChoicesWithDefaults()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Line, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                XAxisNumberFormat: (ChartDataLabelNumberFormat)99,
                XAxisMajorTickStyle: (ChartAxisTickStyle)99,
                XAxisMinorTickStyle: (ChartAxisTickStyle)99,
                YAxisNumberFormat: (ChartDataLabelNumberFormat)99,
                YAxisMajorTickStyle: (ChartAxisTickStyle)99,
                YAxisMinorTickStyle: (ChartAxisTickStyle)99,
                LegendPosition: (ChartLegendPosition)99,
                DataLabelPosition: (ChartDataLabelPosition)99,
                DataLabelSeparator: (ChartDataLabelSeparator)99,
                DataLabelNumberFormat: (ChartDataLabelNumberFormat)99,
                TrendlineType: (ChartTrendlineType)99,
                TrendlineDashStyle: (ChartLineDashStyle)99,
                SeriesFormats:
                [
                    new ChartSeriesFormat(
                        0,
                        DashStyle: (ChartLineDashStyle)99,
                        MarkerStyle: (ChartMarkerStyle)99)
                ]));

        command.Apply(ctx).Success.Should().BeTrue();

        var chart = sheet.Charts[0];
        chart.XAxisNumberFormat.Should().Be(ChartDataLabelNumberFormat.General);
        chart.XAxisMajorTickStyle.Should().Be(ChartAxisTickStyle.Outside);
        chart.XAxisMinorTickStyle.Should().Be(ChartAxisTickStyle.None);
        chart.YAxisNumberFormat.Should().Be(ChartDataLabelNumberFormat.General);
        chart.YAxisMajorTickStyle.Should().Be(ChartAxisTickStyle.Outside);
        chart.YAxisMinorTickStyle.Should().Be(ChartAxisTickStyle.None);
        chart.LegendPosition.Should().Be(ChartLegendPosition.Right);
        chart.DataLabelPosition.Should().Be(ChartDataLabelPosition.BestFit);
        chart.DataLabelSeparator.Should().Be(ChartDataLabelSeparator.Comma);
        chart.DataLabelNumberFormat.Should().Be(ChartDataLabelNumberFormat.General);
        chart.TrendlineType.Should().Be(ChartTrendlineType.Linear);
        chart.TrendlineDashStyle.Should().Be(ChartLineDashStyle.Dash);
        chart.SeriesFormats.Should().BeEmpty();
    }

    [Fact]
    public void SetChartLayoutCommand_SanitizesNonFiniteNumericOptions()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 3));
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                ChartTitleFontSize: double.NaN,
                XAxisMinimum: double.NaN,
                XAxisMaximum: double.PositiveInfinity,
                XAxisMajorUnit: double.NaN,
                XAxisGridlineThickness: double.NaN,
                DataLabelFontSize: double.NaN,
                TrendlineThickness: double.NaN,
                SeriesFormats:
                [
                    new ChartSeriesFormat(0, StrokeThickness: double.NaN, MarkerSize: double.PositiveInfinity)
                ],
                PointDataLabelFormats:
                [
                    new ChartPointDataLabelFormat(0, 0, BorderThickness: double.NaN, FontSize: double.NegativeInfinity)
                ]));

        command.Apply(ctx).Success.Should().BeTrue();

        var chart = sheet.Charts[0];
        chart.ChartTitleFontSize.Should().Be(6);
        chart.XAxisMinimum.Should().BeNull();
        chart.XAxisMaximum.Should().BeNull();
        chart.XAxisMajorUnit.Should().BeNull();
        chart.XAxisGridlineThickness.Should().Be(0.25);
        chart.DataLabelFontSize.Should().Be(6);
        chart.TrendlineThickness.Should().Be(0.5);
        chart.SeriesFormats.Should().ContainSingle().Which.Should().Be(
            new ChartSeriesFormat(0, StrokeThickness: 0.5));
        chart.PointDataLabelFormats.Should().ContainSingle().Which.Should().Be(
            new ChartPointDataLabelFormat(0, 0, BorderThickness: 0, FontSize: 6));
    }

    [Fact]
    public void SetChartLayoutCommand_SanitizesExplodedSliceIndexToExistingDataPoints()
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
            new ChartLayoutOptions(ExplodedSliceIndex: 5, ExplodedSliceDistance: 0.2));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].ExplodedSliceIndex.Should().Be(-1);
        sheet.Charts[0].ExplodedSliceDistance.Should().Be(0.2);
    }

    [Fact]
    public void SetChartLayoutCommand_AppliesBarGapWidthToBarChart()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var chart = new ChartModel { Type = ChartType.Column, DataRange = CreateChartRange(sheet) };
        sheet.Charts.Add(chart);

        var command = new SetChartLayoutCommand(sheet.Id, chart.Id, new ChartLayoutOptions(BarGapWidth: 200));
        command.Apply(ctx).Success.Should().BeTrue();

        chart.BarGapWidth.Should().Be(200);
    }

    [Fact]
    public void SetChartLayoutCommand_ClampsBarGapWidthTo0To500()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var chart = new ChartModel { Type = ChartType.Column, DataRange = CreateChartRange(sheet) };
        sheet.Charts.Add(chart);

        new SetChartLayoutCommand(sheet.Id, chart.Id, new ChartLayoutOptions(BarGapWidth: -10)).Apply(ctx);
        chart.BarGapWidth.Should().Be(0);

        new SetChartLayoutCommand(sheet.Id, chart.Id, new ChartLayoutOptions(BarGapWidth: 600)).Apply(ctx);
        chart.BarGapWidth.Should().Be(500);
    }

    [Fact]
    public void SetChartLayoutCommand_AppliesBarOverlapToBarChart()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var chart = new ChartModel { Type = ChartType.Bar, DataRange = CreateChartRange(sheet) };
        sheet.Charts.Add(chart);

        new SetChartLayoutCommand(sheet.Id, chart.Id, new ChartLayoutOptions(BarOverlap: -30)).Apply(ctx);
        chart.BarOverlap.Should().Be(-30);

        new SetChartLayoutCommand(sheet.Id, chart.Id, new ChartLayoutOptions(BarOverlap: -200)).Apply(ctx);
        chart.BarOverlap.Should().Be(-100);

        new SetChartLayoutCommand(sheet.Id, chart.Id, new ChartLayoutOptions(BarOverlap: 200)).Apply(ctx);
        chart.BarOverlap.Should().Be(100);
    }

    [Fact]
    public void SetChartLayoutCommand_AppliesBubbleScaleAndOptions()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var chart = new ChartModel { Type = ChartType.Bubble, DataRange = CreateChartRange(sheet) };
        sheet.Charts.Add(chart);

        new SetChartLayoutCommand(sheet.Id, chart.Id, new ChartLayoutOptions(BubbleScale: 150, ShowNegativeBubbles: true, BubbleSizeRepresents: ChartBubbleSizeRepresents.Width)).Apply(ctx);

        chart.BubbleScale.Should().Be(150);
        chart.ShowNegativeBubbles.Should().BeTrue();
        chart.BubbleSizeRepresents.Should().Be(ChartBubbleSizeRepresents.Width);
    }

    [Fact]
    public void SetChartLayoutCommand_ClampsBubbleScaleTo1To300()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var chart = new ChartModel { Type = ChartType.Bubble, DataRange = CreateChartRange(sheet) };
        sheet.Charts.Add(chart);

        new SetChartLayoutCommand(sheet.Id, chart.Id, new ChartLayoutOptions(BubbleScale: 0)).Apply(ctx);
        chart.BubbleScale.Should().Be(1);

        new SetChartLayoutCommand(sheet.Id, chart.Id, new ChartLayoutOptions(BubbleScale: 400)).Apply(ctx);
        chart.BubbleScale.Should().Be(300);
    }

    [Fact]
    public void SetChartLayoutCommand_UndoRestoresBarGapWidthAndOverlap()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var chart = new ChartModel { Type = ChartType.Column, DataRange = CreateChartRange(sheet), BarGapWidth = 100, BarOverlap = 10 };
        sheet.Charts.Add(chart);

        var command = new SetChartLayoutCommand(sheet.Id, chart.Id, new ChartLayoutOptions(BarGapWidth: 250, BarOverlap: 50));
        command.Apply(ctx);
        chart.BarGapWidth.Should().Be(250);
        chart.BarOverlap.Should().Be(50);

        command.Revert(ctx);
        chart.BarGapWidth.Should().Be(100);
        chart.BarOverlap.Should().Be(10);
    }
}
