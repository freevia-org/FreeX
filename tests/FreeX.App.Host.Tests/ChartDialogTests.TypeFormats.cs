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
    public void ChartBarFormatDialogResult_ClampsGapWidthTo0To500()
    {
        ChartBarFormatDialogResult.CreateResult(-10, 0).BarGapWidth.Should().Be(0);
        ChartBarFormatDialogResult.CreateResult(600, 0).BarGapWidth.Should().Be(500);
        ChartBarFormatDialogResult.CreateResult(150, 0).BarGapWidth.Should().Be(150);
        ChartBarFormatDialogResult.CreateResult(0, 0).BarGapWidth.Should().Be(0);
    }

    [Fact]
    public void ChartBarFormatDialogResult_ClampsOverlapToMinus100To100()
    {
        ChartBarFormatDialogResult.CreateResult(150, -200).BarOverlap.Should().Be(-100);
        ChartBarFormatDialogResult.CreateResult(150, 200).BarOverlap.Should().Be(100);
        ChartBarFormatDialogResult.CreateResult(150, 50).BarOverlap.Should().Be(50);
        ChartBarFormatDialogResult.CreateResult(150, -50).BarOverlap.Should().Be(-50);
    }

    [Fact]
    public void ChartBarFormatDialogResult_LoadsFromChart()
    {
        var chart = new ChartModel { Type = ChartType.Column, BarGapWidth = 200, BarOverlap = 30 };
        var result = ChartBarFormatDialogResult.FromChart(chart);
        result.BarGapWidth.Should().Be(200);
        result.BarOverlap.Should().Be(30);
    }

    [Fact]
    public void ChartBarFormatDialogResult_UsesDefaultsWhenChartHasNoGapWidth()
    {
        var chart = new ChartModel { Type = ChartType.Column };
        var result = ChartBarFormatDialogResult.FromChart(chart);
        result.BarGapWidth.Should().Be(150);
        result.BarOverlap.Should().Be(0);
    }

    [Fact]
    public void ChartBarFormatDialogResult_MapsToLayoutOptions()
    {
        var result = ChartBarFormatDialogResult.CreateResult(200, 30);
        result.ToOptions().BarGapWidth.Should().Be(200);
        result.ToOptions().BarOverlap.Should().Be(30);
    }

    [Fact]
    public void ChartBubbleFormatDialogResult_ClampsBubbleScaleTo1To300()
    {
        ChartBubbleFormatDialogResult.CreateResult(0, false, ChartBubbleSizeRepresents.Area).BubbleScale.Should().Be(1);
        ChartBubbleFormatDialogResult.CreateResult(400, false, ChartBubbleSizeRepresents.Area).BubbleScale.Should().Be(300);
        ChartBubbleFormatDialogResult.CreateResult(100, false, ChartBubbleSizeRepresents.Area).BubbleScale.Should().Be(100);
    }

    [Fact]
    public void ChartBubbleFormatDialogResult_LoadsFromChart()
    {
        var chart = new ChartModel { Type = ChartType.Bubble, BubbleScale = 150, ShowNegativeBubbles = true, BubbleSizeRepresents = ChartBubbleSizeRepresents.Width };
        var result = ChartBubbleFormatDialogResult.FromChart(chart);
        result.BubbleScale.Should().Be(150);
        result.ShowNegativeBubbles.Should().BeTrue();
        result.BubbleSizeRepresents.Should().Be(ChartBubbleSizeRepresents.Width);
    }

    [Fact]
    public void ChartBubbleFormatDialogResult_MapsToLayoutOptions()
    {
        var result = ChartBubbleFormatDialogResult.CreateResult(150, true, ChartBubbleSizeRepresents.Width);
        result.ToOptions().BubbleScale.Should().Be(150);
        result.ToOptions().ShowNegativeBubbles.Should().BeTrue();
        result.ToOptions().BubbleSizeRepresents.Should().Be(ChartBubbleSizeRepresents.Width);
    }

    [Fact]
    public void ChartPieFormatDialogResult_ClampsFirstSliceAngleTo0To359()
    {
        ChartPieFormatDialogResult.CreateResult(-10, -1, 0.1, 0.55).FirstSliceAngle.Should().Be(0);
        ChartPieFormatDialogResult.CreateResult(400, -1, 0.1, 0.55).FirstSliceAngle.Should().Be(359);
        ChartPieFormatDialogResult.CreateResult(180, -1, 0.1, 0.55).FirstSliceAngle.Should().Be(180);
    }

    [Fact]
    public void ChartPieFormatDialogResult_ClampsExplodedSliceDistanceTo0To50Percent()
    {
        ChartPieFormatDialogResult.CreateResult(0, 0, -0.1, 0.55).ExplodedSliceDistance.Should().Be(0);
        ChartPieFormatDialogResult.CreateResult(0, 0, 0.8, 0.55).ExplodedSliceDistance.Should().Be(0.5);
        ChartPieFormatDialogResult.CreateResult(0, 0, 0.25, 0.55).ExplodedSliceDistance.Should().BeApproximately(0.25, 0.0001);
    }

    [Fact]
    public void ChartPieFormatDialogResult_ClampsDoughnutHoleSizeTo10To90Percent()
    {
        ChartPieFormatDialogResult.CreateResult(0, -1, 0.1, 0.05).DoughnutHoleSize.Should().Be(0.1);
        ChartPieFormatDialogResult.CreateResult(0, -1, 0.1, 0.95).DoughnutHoleSize.Should().Be(0.9);
        ChartPieFormatDialogResult.CreateResult(0, -1, 0.1, 0.75).DoughnutHoleSize.Should().BeApproximately(0.75, 0.0001);
    }

    [Fact]
    public void ChartPieFormatDialogResult_LoadsFromChart()
    {
        var chart = new ChartModel
        {
            Type = ChartType.Doughnut,
            FirstSliceAngle = 45,
            ExplodedSliceIndex = 2,
            ExplodedSliceDistance = 0.2,
            DoughnutHoleSize = 0.6
        };
        var result = ChartPieFormatDialogResult.FromChart(chart);
        result.FirstSliceAngle.Should().Be(45);
        result.ExplodedSliceIndex.Should().Be(2);
        result.ExplodedSliceDistance.Should().BeApproximately(0.2, 0.0001);
        result.DoughnutHoleSize.Should().BeApproximately(0.6, 0.0001);
    }

    [Fact]
    public void ChartPieFormatDialogResult_MapsToLayoutOptions()
    {
        var result = ChartPieFormatDialogResult.CreateResult(90, 1, 0.3, 0.7);
        result.ToOptions().FirstSliceAngle.Should().Be(90);
        result.ToOptions().ExplodedSliceIndex.Should().Be(1);
        result.ToOptions().ExplodedSliceDistance.Should().BeApproximately(0.3, 0.0001);
        result.ToOptions().DoughnutHoleSize.Should().BeApproximately(0.7, 0.0001);
    }

    [Fact]
    public void ChartStockFormatDialogResult_ClampsUpDownBarGapWidthTo0To500()
    {
        ChartStockFormatDialogResult.CreateResult(-5, null, null, null, null, null, 1.0).UpDownBarGapWidth.Should().Be(0);
        ChartStockFormatDialogResult.CreateResult(600, null, null, null, null, null, 1.0).UpDownBarGapWidth.Should().Be(500);
        ChartStockFormatDialogResult.CreateResult(150, null, null, null, null, null, 1.0).UpDownBarGapWidth.Should().Be(150);
    }

    [Fact]
    public void ChartStockFormatDialogResult_ClampsHighLowLineThicknessTo05To10()
    {
        ChartStockFormatDialogResult.CreateResult(150, null, null, null, null, null, 0.1).HighLowLineThickness.Should().BeApproximately(0.5, 0.001);
        ChartStockFormatDialogResult.CreateResult(150, null, null, null, null, null, 20.0).HighLowLineThickness.Should().BeApproximately(10.0, 0.001);
        ChartStockFormatDialogResult.CreateResult(150, null, null, null, null, null, 1.5).HighLowLineThickness.Should().BeApproximately(1.5, 0.001);
    }

    [Fact]
    public void ChartStockFormatDialogResult_LoadsFromChart()
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
        var result = ChartStockFormatDialogResult.FromChart(chart);
        result.UpDownBarGapWidth.Should().Be(200);
        result.UpBarFillColor.Should().Be(new CellColor(0, 128, 0));
        result.DownBarFillColor.Should().Be(new CellColor(255, 0, 0));
        result.HighLowLineColor.Should().Be(new CellColor(100, 100, 100));
        result.HighLowLineThickness.Should().BeApproximately(2.0, 0.001);
    }

    [Fact]
    public void ChartStockFormatDialogResult_MapsToLayoutOptions()
    {
        var result = ChartStockFormatDialogResult.CreateResult(
            150, new CellColor(0, 200, 0), new CellColor(0, 100, 0),
            new CellColor(200, 0, 0), new CellColor(100, 0, 0),
            new CellColor(80, 80, 80), 1.5);
        result.ToOptions().UpDownBarGapWidth.Should().Be(150);
        result.ToOptions().UpBarFillColor.Should().Be(new CellColor(0, 200, 0));
        result.ToOptions().UpBarBorderColor.Should().Be(new CellColor(0, 100, 0));
        result.ToOptions().DownBarFillColor.Should().Be(new CellColor(200, 0, 0));
        result.ToOptions().DownBarBorderColor.Should().Be(new CellColor(100, 0, 0));
        result.ToOptions().HighLowLineColor.Should().Be(new CellColor(80, 80, 80));
        result.ToOptions().HighLowLineThickness.Should().BeApproximately(1.5, 0.001);
    }

}
