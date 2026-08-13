using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ChartDialogTests
{
    [Fact]
    public void ChartTypeFormatDialogs_ReturnSharedEditingContracts()
    {
        var source = DialogSourceTestSupport.ReadHostSources("ChartTypeFormatDialogs.cs");

        source.Should().Contain("ChartBarFormatPlanner.Read(chart)");
        source.Should().Contain("public ChartBarFormatInput Result { get; private set; }");
        source.Should().NotContain("public static ChartBarFormatInput CreateResult(");
        source.Should().Contain("ChartBarFormatPlanner.TryParseDialogInput(");
        source.Should().Contain("ChartPieFormatPlanner.Read(chart)");
        source.Should().Contain("public ChartPieFormatInput Result { get; private set; }");
        source.Should().NotContain("public static ChartPieFormatInput CreateResult(");
        source.Should().Contain("ChartPieFormatPlanner.TryParseDialogInput(");
        source.Should().Contain("ChartPieFormatPlanner.ToDisplayPercent(");
        source.Should().Contain("ChartBubbleFormatPlanner.Read(chart)");
        source.Should().Contain("public ChartBubbleFormatInput Result { get; private set; }");
        source.Should().NotContain("public static ChartBubbleFormatInput CreateResult(");
        source.Should().Contain("ChartBubbleFormatPlanner.TryParseDialogInput(");
        source.Should().Contain("ChartStockFormatPlanner.Read(chart)");
        source.Should().Contain("public ChartStockFormatInput Result { get; private set; }");
        source.Should().NotContain("public static ChartStockFormatInput CreateResult(");
        source.Should().Contain("ChartStockFormatPlanner.TryParseDialogInput(");
        source.Should().Contain("ChartBubbleFormatPlanner.GetSizeRepresentsChoices()");
        source.Should().NotContain("FormatDialogResult");
        source.Should().NotContain("ToInput()");
        source.Should().NotContain("FromInput(");
        source.Should().NotContain("public static ChartBarFormatInput FromChart(");
        source.Should().NotContain("public static ChartPieFormatInput FromChart(");
        source.Should().NotContain("public static ChartBubbleFormatInput FromChart(");
        source.Should().NotContain("public static ChartStockFormatInput FromChart(");

        source.Should().NotContain("chart.BarGapWidth ?? 150");
        source.Should().NotContain("chart.UpDownBarGapWidth ?? 150");
        source.Should().NotContain("Enum.GetValues<ChartBubbleSizeRepresents>()");
        source.Should().NotContain("new(BarGapWidth: BarGapWidth");
        source.Should().NotContain("new(BubbleScale: BubbleScale");
        source.Should().NotContain("TryReadClampedInt");
        source.Should().NotContain("int.TryParse");
        source.Should().NotContain("double.TryParse");
        source.Should().NotContain("NumberStyles.Integer");
        source.Should().NotContain("NumberStyles.Float");
        source.Should().NotContain("private static int Percent");
    }

    [Fact]
    public void ChartBarFormatInput_ClampsGapWidthTo0To500()
    {
        ChartBarFormatPlanner.Normalize(new ChartBarFormatInput(-10, 0)).BarGapWidth.Should().Be(0);
        ChartBarFormatPlanner.Normalize(new ChartBarFormatInput(600, 0)).BarGapWidth.Should().Be(500);
        ChartBarFormatPlanner.Normalize(new ChartBarFormatInput(150, 0)).BarGapWidth.Should().Be(150);
        ChartBarFormatPlanner.Normalize(new ChartBarFormatInput(0, 0)).BarGapWidth.Should().Be(0);
    }

    [Fact]
    public void ChartBarFormatInput_ClampsOverlapToMinus100To100()
    {
        ChartBarFormatPlanner.Normalize(new ChartBarFormatInput(150, -200)).BarOverlap.Should().Be(-100);
        ChartBarFormatPlanner.Normalize(new ChartBarFormatInput(150, 200)).BarOverlap.Should().Be(100);
        ChartBarFormatPlanner.Normalize(new ChartBarFormatInput(150, 50)).BarOverlap.Should().Be(50);
        ChartBarFormatPlanner.Normalize(new ChartBarFormatInput(150, -50)).BarOverlap.Should().Be(-50);
    }

    [Fact]
    public void ChartBarFormatInput_LoadsFromChart()
    {
        var chart = new ChartModel { Type = ChartType.Column, BarGapWidth = 200, BarOverlap = 30 };
        var result = ChartBarFormatPlanner.Read(chart);
        result.BarGapWidth.Should().Be(200);
        result.BarOverlap.Should().Be(30);
    }

    [Fact]
    public void ChartBarFormatInput_UsesDefaultsWhenChartHasNoGapWidth()
    {
        var chart = new ChartModel { Type = ChartType.Column };
        var result = ChartBarFormatPlanner.Read(chart);
        result.BarGapWidth.Should().Be(150);
        result.BarOverlap.Should().Be(0);
    }

    [Fact]
    public void ChartBarFormatInput_MapsToLayoutOptions()
    {
        var result = ChartBarFormatPlanner.Normalize(new ChartBarFormatInput(200, 30));
        var options = ChartBarFormatPlanner.Plan(result);
        options.BarGapWidth.Should().Be(200);
        options.BarOverlap.Should().Be(30);
    }

    [Fact]
    public void ChartBubbleFormatInput_ClampsBubbleScaleTo1To300()
    {
        ChartBubbleFormatPlanner.Normalize(new ChartBubbleFormatInput(0, false, ChartBubbleSizeRepresents.Area)).BubbleScale.Should().Be(1);
        ChartBubbleFormatPlanner.Normalize(new ChartBubbleFormatInput(400, false, ChartBubbleSizeRepresents.Area)).BubbleScale.Should().Be(300);
        ChartBubbleFormatPlanner.Normalize(new ChartBubbleFormatInput(100, false, ChartBubbleSizeRepresents.Area)).BubbleScale.Should().Be(100);
    }

    [Fact]
    public void ChartBubbleFormatInput_LoadsFromChart()
    {
        var chart = new ChartModel { Type = ChartType.Bubble, BubbleScale = 150, ShowNegativeBubbles = true, BubbleSizeRepresents = ChartBubbleSizeRepresents.Width };
        var result = ChartBubbleFormatPlanner.Read(chart);
        result.BubbleScale.Should().Be(150);
        result.ShowNegativeBubbles.Should().BeTrue();
        result.BubbleSizeRepresents.Should().Be(ChartBubbleSizeRepresents.Width);
    }

    [Fact]
    public void ChartBubbleFormatInput_MapsToLayoutOptions()
    {
        var result = ChartBubbleFormatPlanner.Normalize(new ChartBubbleFormatInput(150, true, ChartBubbleSizeRepresents.Width));
        var options = ChartBubbleFormatPlanner.Plan(result);
        options.BubbleScale.Should().Be(150);
        options.ShowNegativeBubbles.Should().BeTrue();
        options.BubbleSizeRepresents.Should().Be(ChartBubbleSizeRepresents.Width);
    }

    [Fact]
    public void ChartPieFormatInput_ClampsFirstSliceAngleTo0To359()
    {
        ChartPieFormatPlanner.Normalize(new ChartPieFormatInput(-10, -1, 0.1, 0.55)).FirstSliceAngle.Should().Be(0);
        ChartPieFormatPlanner.Normalize(new ChartPieFormatInput(400, -1, 0.1, 0.55)).FirstSliceAngle.Should().Be(359);
        ChartPieFormatPlanner.Normalize(new ChartPieFormatInput(180, -1, 0.1, 0.55)).FirstSliceAngle.Should().Be(180);
    }

    [Fact]
    public void ChartPieFormatInput_ClampsExplodedSliceDistanceTo0To50Percent()
    {
        ChartPieFormatPlanner.Normalize(new ChartPieFormatInput(0, 0, -0.1, 0.55)).ExplodedSliceDistance.Should().Be(0);
        ChartPieFormatPlanner.Normalize(new ChartPieFormatInput(0, 0, 0.8, 0.55)).ExplodedSliceDistance.Should().Be(0.5);
        ChartPieFormatPlanner.Normalize(new ChartPieFormatInput(0, 0, 0.25, 0.55)).ExplodedSliceDistance.Should().BeApproximately(0.25, 0.0001);
    }

    [Fact]
    public void ChartPieFormatInput_ClampsDoughnutHoleSizeTo10To90Percent()
    {
        ChartPieFormatPlanner.Normalize(new ChartPieFormatInput(0, -1, 0.1, 0.05)).DoughnutHoleSize.Should().Be(0.1);
        ChartPieFormatPlanner.Normalize(new ChartPieFormatInput(0, -1, 0.1, 0.95)).DoughnutHoleSize.Should().Be(0.9);
        ChartPieFormatPlanner.Normalize(new ChartPieFormatInput(0, -1, 0.1, 0.75)).DoughnutHoleSize.Should().BeApproximately(0.75, 0.0001);
    }

    [Fact]
    public void ChartPieFormatInput_LoadsFromChart()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Doughnut,
            FirstSliceAngle = 45,
            ExplodedSliceIndex = 2,
            ExplodedSliceDistance = 0.2,
            DoughnutHoleSize = 0.6
        };
        var result = ChartPieFormatPlanner.Read(chart);
        result.FirstSliceAngle.Should().Be(45);
        result.ExplodedSliceIndex.Should().Be(2);
        result.ExplodedSliceDistance.Should().BeApproximately(0.2, 0.0001);
        result.DoughnutHoleSize.Should().BeApproximately(0.6, 0.0001);
    }

    [Fact]
    public void ChartPieFormatInput_MapsToLayoutOptions()
    {
        var result = ChartPieFormatPlanner.Normalize(new ChartPieFormatInput(90, 1, 0.3, 0.7));
        var options = ChartPieFormatPlanner.Plan(result);
        options.FirstSliceAngle.Should().Be(90);
        options.ExplodedSliceIndex.Should().Be(1);
        options.ExplodedSliceDistance.Should().BeApproximately(0.3, 0.0001);
        options.DoughnutHoleSize.Should().BeApproximately(0.7, 0.0001);
    }

    [Fact]
    public void ChartStockFormatInput_ClampsUpDownBarGapWidthTo0To500()
    {
        ChartStockFormatPlanner.Normalize(new ChartStockFormatInput(-5, null, null, null, null, null, 1.0)).UpDownBarGapWidth.Should().Be(0);
        ChartStockFormatPlanner.Normalize(new ChartStockFormatInput(600, null, null, null, null, null, 1.0)).UpDownBarGapWidth.Should().Be(500);
        ChartStockFormatPlanner.Normalize(new ChartStockFormatInput(150, null, null, null, null, null, 1.0)).UpDownBarGapWidth.Should().Be(150);
    }

    [Fact]
    public void ChartStockFormatInput_ClampsHighLowLineThicknessTo05To10()
    {
        ChartStockFormatPlanner.Normalize(new ChartStockFormatInput(150, null, null, null, null, null, 0.1)).HighLowLineThickness.Should().BeApproximately(0.5, 0.001);
        ChartStockFormatPlanner.Normalize(new ChartStockFormatInput(150, null, null, null, null, null, 20.0)).HighLowLineThickness.Should().BeApproximately(10.0, 0.001);
        ChartStockFormatPlanner.Normalize(new ChartStockFormatInput(150, null, null, null, null, null, 1.5)).HighLowLineThickness.Should().BeApproximately(1.5, 0.001);
    }

    [Fact]
    public void ChartStockFormatInput_LoadsFromChart()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Stock,
            UpDownBarGapWidth = 200,
            UpBarFillColor = new CellColor(0, 128, 0),
            DownBarFillColor = new CellColor(255, 0, 0),
            HighLowLineColor = new CellColor(100, 100, 100),
            HighLowLineThickness = 2.0
        };
        var result = ChartStockFormatPlanner.Read(chart);
        result.UpDownBarGapWidth.Should().Be(200);
        result.UpBarFillColor.Should().Be(new CellColor(0, 128, 0));
        result.DownBarFillColor.Should().Be(new CellColor(255, 0, 0));
        result.HighLowLineColor.Should().Be(new CellColor(100, 100, 100));
        result.HighLowLineThickness.Should().BeApproximately(2.0, 0.001);
    }

    [Fact]
    public void ChartStockFormatInput_MapsToLayoutOptions()
    {
        var result = ChartStockFormatPlanner.Normalize(new ChartStockFormatInput(
            150, new CellColor(0, 200, 0), new CellColor(0, 100, 0),
            new CellColor(200, 0, 0), new CellColor(100, 0, 0),
            new CellColor(80, 80, 80), 1.5));
        var options = ChartStockFormatPlanner.Plan(result);
        options.UpDownBarGapWidth.Should().Be(150);
        options.UpBarFillColor.Should().Be(new CellColor(0, 200, 0));
        options.UpBarBorderColor.Should().Be(new CellColor(0, 100, 0));
        options.DownBarFillColor.Should().Be(new CellColor(200, 0, 0));
        options.DownBarBorderColor.Should().Be(new CellColor(100, 0, 0));
        options.HighLowLineColor.Should().Be(new CellColor(80, 80, 80));
        options.HighLowLineThickness.Should().BeApproximately(1.5, 0.001);
    }

}
