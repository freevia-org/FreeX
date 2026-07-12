using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;
using static FreeX.App.Presentation.Tests.Charts.ChartLayoutTestData;

namespace FreeX.App.Presentation.Tests.Charts;

/// <summary>
/// Round-29 finding R29-chart-render-pixel-deep-2: LayoutLineSeries used to silently omit a blank
/// (Gap-mode) category index from a Line/Area series' Points, which let a polyline renderer connect
/// straight across the gap instead of breaking (see LineAreaScatterLayoutTests for the Line/Area
/// coverage). These tests cover the sibling combo-scatter overlay path (LayoutComboScatterSeries),
/// which must keep omitting blanks entirely — a marker-only overlay has no connecting line to break,
/// and the WPF reference renderer's combo-scatter path never plots a point for a blank cell
/// regardless of BlankDisplayMode.
/// </summary>
public sealed class ChartGapBreakLayoutTests
{
    [Fact]
    public void Combo_scatter_overlay_still_omits_blank_points_under_gap_mode()
    {
        var chart = Chart(ChartType.Column, c =>
        {
            c.ComboScatterSeriesIndexes = [1];
            c.BlankDisplayMode = ChartBlankDisplayMode.Gap;
        });
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B", "C"],
            [Series(0, "Bars", 10, 20, 30), Series(1, "Scatter", 5, null, 8)]));

        layout.Series[1].Kind.Should().Be(SeriesGeometryKind.ScatterPoints);
        var points = layout.Series[1].Points;
        points.Should().HaveCount(2, "the blank middle point is a marker overlay and must be omitted, not given a NaN placeholder");
        points.Select(p => p.PointIndex).Should().Equal(0, 2);
        points.Should().OnlyContain(p => !double.IsNaN(p.DataY));
    }

    [Fact]
    public void Combo_line_overlay_gets_the_same_gap_break_point_as_a_plain_line_series()
    {
        // A combo LINE overlay (as opposed to combo scatter) is a real connected line, so it must get
        // the same NaN break-marker treatment as a plain Line series.
        var chart = Chart(ChartType.Column, c =>
        {
            c.UseComboLineForSecondarySeries = true;
            c.ComboLineSeriesIndexes = [1];
            c.BlankDisplayMode = ChartBlankDisplayMode.Gap;
        });
        var layout = ChartLayoutEngine.Layout(Request(chart, ["A", "B", "C"],
            [Series(0, "Bars", 10, 20, 30), Series(1, "Line", 5, null, 8)]));

        layout.Series[1].Kind.Should().Be(SeriesGeometryKind.Line);
        var points = layout.Series[1].Points;
        points.Should().HaveCount(3);
        points.Select(p => p.PointIndex).Should().Equal(0, 1, 2);
        points[1].DataY.Should().Be(double.NaN);
    }
}
