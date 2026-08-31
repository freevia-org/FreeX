using System.Reflection;
using FluentAssertions;
using FreeX.App.UI;
using FreeX.Core.Model;
using OxyPlot;
using OxyPlot.Series;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R91-render-chart-series-format-5-2: Excel's "Invert if negative" (<c>&lt;c:invertIfNegative&gt;</c>,
/// modeled as <see cref="ChartSeriesFormat.InvertIfNegative"/>) was parsed, stored, and re-serialized
/// but never consumed when actually building the series the renderer draws -- every negative-valued
/// bar/column rendered with the exact same fill as a positive one. Covers both series shapes the WPF
/// renderer uses: <see cref="RectangleBarSeries"/> (Column/ThreeDColumn, via per-item <c>Color</c>) and
/// <see cref="BarSeries"/> (Bar/ThreeDBar, via the OxyPlot-native <c>NegativeFillColor</c>).
/// </summary>
public sealed class R91_ChartInvertIfNegativeTests
{
    private static DisplayCell Cell(uint row, uint col, string text) =>
        new(row, col, null, text, null, StyleId.Default, null);

    private static PlotModel BuildPlotModel(ChartModel chart, ViewportModel viewport)
    {
        return ChartRenderer.BuildPlotModel(chart, viewport).Should().BeOfType<PlotModel>().Subject;
    }

    private static ViewportModel BuildColumnViewport(SheetId sheetId) => new(
        [
            Cell(1, 1, "Quarter"),
            Cell(1, 2, "Revenue"),
            Cell(2, 1, "Q1"),
            Cell(2, 2, "-10"),
            Cell(3, 1, "Q2"),
            Cell(3, 2, "20")
        ],
        [],
        []);

    [Fact]
    public void ColumnRenderer_InvertIfNegative_PaintsNegativeItemWhite()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            SeriesFormats = [new ChartSeriesFormat(0, InvertIfNegative: true)]
        };

        var model = BuildPlotModel(chart, BuildColumnViewport(sheetId));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<RectangleBarSeries>().Subject;
        series.Items.Should().HaveCount(2);
        // Q1 = -10 (negative) must be painted with the invert fill (Excel's default alternate: white)
        // -- before the fix this stayed OxyColors.Automatic (falls back to the series' normal fill).
        series.Items[0].Color.Should().Be(OxyColors.White);
        // Q2 = 20 (non-negative) must keep the normal (unset/automatic) per-item color.
        series.Items[1].Color.Should().Be(OxyColors.Automatic);
    }

    [Fact]
    public void ColumnRenderer_WithoutInvertIfNegative_NegativeItemKeepsNormalFill_NoRegression()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2))
            // No SeriesFormats at all -- InvertIfNegative defaults to null/unset.
        };

        var model = BuildPlotModel(chart, BuildColumnViewport(sheetId));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<RectangleBarSeries>().Subject;
        series.Items.Should().HaveCount(2);
        series.Items[0].Color.Should().Be(OxyColors.Automatic,
            "without 'Invert if negative' a negative bar must render with the series' own normal fill, same as before this fix");
        series.Items[1].Color.Should().Be(OxyColors.Automatic);
    }

    [Fact]
    public void BarRenderer_InvertIfNegative_SetsOxyPlotNegativeFillColor()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bar,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            SeriesFormats = [new ChartSeriesFormat(0, InvertIfNegative: true)]
        };

        var model = BuildPlotModel(chart, BuildColumnViewport(sheetId));

        // BarSeries (the horizontal Bar/ThreeDBar renderer) has a built-in NegativeFillColor OxyPlot
        // itself honors per-item against BaseValue at render time -- this IS the object the OxyPlot
        // rendering pipeline consumes, not a reimplementation of it.
        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<BarSeries>().Subject;
        series.NegativeFillColor.Should().Be(OxyColors.White,
            "before the fix NegativeFillColor was never touched, so it stayed Automatic and negative bars rendered identically to positive ones");
    }

    [Fact]
    public void BarRenderer_WithoutInvertIfNegative_NegativeFillColorStaysAutomatic_NoRegression()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Bar,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 2)),
            // A non-null format WITHOUT InvertIfNegative, so ApplyBarFormat's body actually runs
            // (rather than short-circuiting on a null format) and exercises the "else" branch that
            // must still explicitly resolve to Automatic.
            SeriesFormats = [new ChartSeriesFormat(0, FillColor: new CellColor(68, 114, 196))]
        };

        var model = BuildPlotModel(chart, BuildColumnViewport(sheetId));

        var series = model.Series.Should().ContainSingle().Which.Should().BeOfType<BarSeries>().Subject;
        series.NegativeFillColor.Should().Be(OxyColors.Automatic,
            "without 'Invert if negative' a negative bar must render with the series' own normal fill, same as before this fix");
    }
}
