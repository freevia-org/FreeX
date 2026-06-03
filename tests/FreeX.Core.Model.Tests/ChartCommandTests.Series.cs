using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

public sealed partial class ChartCommandTests
{

    [Theory]
    [InlineData(ChartTrendlineType.Linear)]
    [InlineData(ChartTrendlineType.Exponential)]
    [InlineData(ChartTrendlineType.Logarithmic)]
    [InlineData(ChartTrendlineType.Power)]
    [InlineData(ChartTrendlineType.MovingAverage)]
    [InlineData(ChartTrendlineType.Polynomial)]
    public void SetChartLayoutCommand_UpdatesSupportedTrendlineTypes(ChartTrendlineType type)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Line, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(ShowLinearTrendline: true, TrendlineType: type));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].ShowLinearTrendline.Should().BeTrue();
        sheet.Charts[0].TrendlineType.Should().Be(type);
    }

    [Fact]
    public void SetChartLayoutCommand_UpdatesMovingAveragePeriod()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 6, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Line, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                ShowLinearTrendline: true,
                TrendlineType: ChartTrendlineType.MovingAverage,
                TrendlinePeriod: 4));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].TrendlineType.Should().Be(ChartTrendlineType.MovingAverage);
        sheet.Charts[0].TrendlinePeriod.Should().Be(4);
    }

    [Fact]
    public void SetChartLayoutCommand_UpdatesPolynomialOrder()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 6, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Line, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                ShowLinearTrendline: true,
                TrendlineType: ChartTrendlineType.Polynomial,
                TrendlineOrder: 5));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].TrendlineType.Should().Be(ChartTrendlineType.Polynomial);
        sheet.Charts[0].TrendlineOrder.Should().Be(5);
    }

    [Fact]
    public void SetChartLayoutCommand_UpdatesErrorBarsAndUndoRestores()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Line, "Sales").Apply(ctx);
        var chart = sheet.Charts[0];

        var command = new SetChartLayoutCommand(
            sheet.Id,
            chart.Id,
            new ChartLayoutOptions(
                ShowErrorBars: true,
                ErrorBarKind: ChartErrorBarKind.Percentage,
                ErrorBarDirection: ChartErrorBarDirection.Plus,
                ErrorBarValue: 12.5,
                ErrorBarEndCaps: false));

        command.Apply(ctx).Success.Should().BeTrue();

        chart.ShowErrorBars.Should().BeTrue();
        chart.ErrorBarKind.Should().Be(ChartErrorBarKind.Percentage);
        chart.ErrorBarDirection.Should().Be(ChartErrorBarDirection.Plus);
        chart.ErrorBarValue.Should().Be(12.5);
        chart.ErrorBarEndCaps.Should().BeFalse();

        command.Revert(ctx);

        chart.ShowErrorBars.Should().BeFalse();
        chart.ErrorBarKind.Should().Be(ChartErrorBarKind.StandardError);
        chart.ErrorBarDirection.Should().Be(ChartErrorBarDirection.Both);
        chart.ErrorBarValue.Should().Be(5);
        chart.ErrorBarEndCaps.Should().BeTrue();
    }

    [Fact]
    public void SetChartLayoutCommand_ClampsErrorBarValueAndDefaultsInvalidEnums()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Line, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                ErrorBarKind: (ChartErrorBarKind)999,
                ErrorBarDirection: (ChartErrorBarDirection)999,
                ErrorBarValue: double.NaN));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].ErrorBarKind.Should().Be(ChartErrorBarKind.StandardError);
        sheet.Charts[0].ErrorBarDirection.Should().Be(ChartErrorBarDirection.Both);
        sheet.Charts[0].ErrorBarValue.Should().Be(0);
    }

    [Fact]
    public void SetChartLayoutCommand_SanitizesSecondaryAxisSeriesIndexes()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 3));
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                ShowSecondaryAxis: true,
                SecondaryAxisSeriesIndexes: [-1, 0, 1, 1, 2]));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].SecondaryAxisSeriesIndexes.Should().Equal(1);
    }

    [Fact]
    public void SetChartLayoutCommand_ClearsSecondaryAxisWhenNoSeriesTargetsRemain()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 3));
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                ShowSecondaryAxis: true,
                SecondaryAxisSeriesIndexes: [-1, 0, 2]));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].ShowSecondaryAxis.Should().BeFalse();
        sheet.Charts[0].SecondaryAxisSeriesIndexes.Should().BeEmpty();
    }

    [Fact]
    public void SetChartLayoutCommand_ClearsSecondaryAxisStateWhenUnsupported()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Pie, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                ShowSecondaryAxis: true,
                SecondaryAxisSeriesIndexes: [1]));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].ShowSecondaryAxis.Should().BeFalse();
        sheet.Charts[0].SecondaryAxisSeriesIndexes.Should().BeEmpty();
    }

    [Fact]
    public void SetChartLayoutCommand_ClearsTrendlineStateWhenUnsupported()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Pie, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                ShowLinearTrendline: true,
                TrendlineType: ChartTrendlineType.Polynomial,
                TrendlinePeriod: 5,
                TrendlineOrder: 4,
                ShowTrendlineEquation: true,
                ShowTrendlineRSquared: true,
                TrendlineColor: new CellColor(217, 83, 25),
                TrendlineThickness: 2.5,
                TrendlineDashStyle: ChartLineDashStyle.Dot));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].ShowLinearTrendline.Should().BeFalse();
        sheet.Charts[0].TrendlineType.Should().Be(ChartTrendlineType.Linear);
        sheet.Charts[0].TrendlinePeriod.Should().Be(2);
        sheet.Charts[0].TrendlineOrder.Should().Be(2);
        sheet.Charts[0].ShowTrendlineEquation.Should().BeFalse();
        sheet.Charts[0].ShowTrendlineRSquared.Should().BeFalse();
        sheet.Charts[0].TrendlineColor.Should().BeNull();
        sheet.Charts[0].TrendlineThemeColor.Should().BeNull();
        sheet.Charts[0].TrendlineThickness.Should().Be(1.5);
        sheet.Charts[0].TrendlineDashStyle.Should().Be(ChartLineDashStyle.Dash);
    }

    [Fact]
    public void SetChartLayoutCommand_ClearsComboLineOverlayStateWhenUnsupported()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 3));
        new AddChartCommand(sheet.Id, range, ChartType.Line, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                UseComboLineForSecondarySeries: true,
                ComboLineSeriesIndexes: [1]));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].UseComboLineForSecondarySeries.Should().BeFalse();
        sheet.Charts[0].ComboLineSeriesIndexes.Should().BeEmpty();
    }

    [Fact]
    public void SetChartLayoutCommand_ClearsComboLineOverlayWhenNoSeriesTargetsRemain()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 3));
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                UseComboLineForSecondarySeries: true,
                ComboLineSeriesIndexes: [-1, 0, 2]));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].UseComboLineForSecondarySeries.Should().BeFalse();
        sheet.Charts[0].ComboLineSeriesIndexes.Should().BeEmpty();
    }

    [Fact]
    public void SetChartLayoutCommand_SanitizesSeriesFormatsToExistingSeries()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                SeriesFormats:
                [
                    new ChartSeriesFormat(-1, FillColor: new CellColor(255, 0, 0)),
                    new ChartSeriesFormat(0, FillColor: new CellColor(0, 114, 178)),
                    new ChartSeriesFormat(2, FillColor: new CellColor(255, 192, 0))
                ]));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].SeriesFormats.Should().ContainSingle().Which.SeriesIndex.Should().Be(0);
    }

    [Fact]
    public void SetChartLayoutCommand_ClearsSeriesMarkerFormattingWhenUnsupported()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Column, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                SeriesFormats:
                [
                    new ChartSeriesFormat(
                        0,
                        FillColor: new CellColor(68, 114, 196),
                        StrokeColor: new CellColor(47, 82, 143),
                        MarkerStyle: ChartMarkerStyle.Diamond,
                        MarkerSize: 8)
                ]));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].SeriesFormats.Should().Equal(
            new ChartSeriesFormat(
                0,
                FillColor: new CellColor(68, 114, 196),
                StrokeColor: new CellColor(47, 82, 143)));
    }

    [Fact]
    public void SetChartLayoutCommand_PreservesBubbleSeriesFormatsForEveryYAndSizePair()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 4, 5));
        new AddChartCommand(sheet.Id, range, ChartType.Bubble, "Bubble").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                SeriesFormats:
                [
                    new ChartSeriesFormat(0, FillColor: new CellColor(68, 114, 196)),
                    new ChartSeriesFormat(1, FillColor: new CellColor(112, 173, 71)),
                    new ChartSeriesFormat(2, FillColor: new CellColor(255, 192, 0))
                ]));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].SeriesFormats.Should().Equal(
            new ChartSeriesFormat(0, FillColor: new CellColor(68, 114, 196)),
            new ChartSeriesFormat(1, FillColor: new CellColor(112, 173, 71)));
    }

    [Fact]
    public void SetChartLayoutCommand_ClampsSeriesFormatStrokeAndMarkerSizes()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new SimpleCtx(wb);
        var range = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 3, 2));
        new AddChartCommand(sheet.Id, range, ChartType.Line, "Sales").Apply(ctx);

        var command = new SetChartLayoutCommand(
            sheet.Id,
            sheet.Charts[0].Id,
            new ChartLayoutOptions(
                SeriesFormats:
                [
                    new ChartSeriesFormat(0, StrokeThickness: -1, MarkerSize: 99)
                ]));

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Charts[0].SeriesFormats.Should().ContainSingle().Which.Should().Be(
            new ChartSeriesFormat(0, StrokeThickness: 0.5, MarkerSize: 30));
    }
}
