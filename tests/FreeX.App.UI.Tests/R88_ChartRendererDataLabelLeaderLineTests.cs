using FluentAssertions;
using FreeX.Core.Model;
using OxyPlot;
using OxyPlot.Annotations;

namespace FreeX.App.UI.Tests;

/// <summary>
/// R88-render-chart-labels-legend-5-3: <c>XlsxChartDataLabelReader.ApplyDataLabelLeaderLineProperties</c>
/// parses the leader-line color/thickness from XLSX into <see cref="ChartModel.DataLabelLeaderLineColor"/>
/// / <see cref="ChartModel.DataLabelLeaderLineThickness"/>, but the renderer used to ignore them entirely
/// and always fell back to a hardcoded gray/1pt for the moved-label callout affordance. These values must
/// now flow through to the annotation's stroke.
/// </summary>
public sealed partial class ChartRendererTests
{
    [Fact]
    public void PieRenderer_UsesLeaderLineColorAndThicknessForCalloutStroke_WhenNoExplicitBorderSet()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Pie,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2)),
            ShowDataLabels = true,
            ShowDataLabelCallouts = true,
            ShowLegend = false,
            DataLabelLeaderLineColor = new CellColor(0, 112, 192),
            DataLabelLeaderLineThickness = 2.5
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10")
            ],
            [],
            []));

        var annotation = model.Annotations.OfType<TextAnnotation>().Should().ContainSingle().Subject;
        annotation.Stroke.Should().Be(OxyColor.FromRgb(0, 112, 192));
        annotation.StrokeThickness.Should().Be(2.5);
    }

    // No-regression sibling: an explicit data-label border color/thickness must still win over the
    // leader-line fallback, exactly as it won over the old hardcoded gray/1pt.
    [Fact]
    public void PieRenderer_PrefersExplicitDataLabelBorderOverLeaderLineColor()
    {
        var sheetId = SheetId.New();
        var chart = new ChartModel
        {
            Type = ChartType.Pie,
            DataRange = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2)),
            ShowDataLabels = true,
            ShowDataLabelCallouts = true,
            ShowLegend = false,
            DataLabelBorderColor = new CellColor(191, 144, 0),
            DataLabelBorderThickness = 1.5,
            DataLabelLeaderLineColor = new CellColor(0, 112, 192),
            DataLabelLeaderLineThickness = 2.5
        };

        var model = BuildPlotModel(chart, new ViewportModel(
            [
                Cell(1, 1, "Quarter"),
                Cell(1, 2, "Revenue"),
                Cell(2, 1, "Q1"),
                Cell(2, 2, "10")
            ],
            [],
            []));

        var annotation = model.Annotations.OfType<TextAnnotation>().Should().ContainSingle().Subject;
        annotation.Stroke.Should().Be(OxyColor.FromRgb(191, 144, 0));
        annotation.StrokeThickness.Should().Be(1.5);
    }
}
