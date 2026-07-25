using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.Core.Model;
using static FreeX.App.Presentation.Tests.Charts.ChartLayoutTestData;

namespace FreeX.App.Presentation.Tests.Charts;

/// <summary>
/// R87-render-chart-plot-5-3: per-point pie/doughnut slice explosion (<c:dPt>/<c:explosion>
/// overrides, captured in <see cref="ChartModel.ExplodedSlices"/>) used to be silently dropped by
/// the portable layout (Avalonia + PDF) -- LayoutPie only ever checked the single legacy scalar
/// <see cref="ChartModel.ExplodedSliceIndex"/>, so a chart with several individually-exploded slices
/// rendered at most one of them exploded, unlike the WPF renderer's IsPieSliceExploded which honors
/// both the scalar AND every entry in ExplodedSlices.
/// </summary>
public sealed class R87_PieMultiSliceExplosionTests
{
    [Fact]
    public void Multiple_slices_in_ExplodedSlices_all_explode_even_with_no_scalar_index_set()
    {
        // ExplodedSliceIndex left at its default -1 (no scalar explosion); slices 1 and 3 are
        // exploded purely via the per-point list, mirroring a round-tripped <c:dPt> explosion.
        var request = Request(Chart(ChartType.Pie, c =>
        {
            c.ExplodedSliceDistance = 0.2;
            c.ExplodedSlices.Add(new ChartPointExplosion(SeriesIndex: 0, PointIndex: 1, Distance: 0.2));
            c.ExplodedSlices.Add(new ChartPointExplosion(SeriesIndex: 0, PointIndex: 3, Distance: 0.2));
        }), ["A", "B", "C", "D"], [Series(0, "S1", 1, 1, 1, 1)]);
        var layout = ChartLayoutEngine.Layout(request);

        var plotCenter = layout.PlotArea.Center;
        var slices = layout.Series[0].Slices;

        bool IsOffsetFromCenter(int index) =>
            Math.Sqrt(Math.Pow(slices[index].Arc.Center.X - plotCenter.X, 2) + Math.Pow(slices[index].Arc.Center.Y - plotCenter.Y, 2)) > 1;

        IsOffsetFromCenter(0).Should().BeFalse("slice 0 was not marked exploded");
        IsOffsetFromCenter(1).Should().BeTrue("slice 1 is in ExplodedSlices and must explode");
        IsOffsetFromCenter(2).Should().BeFalse("slice 2 was not marked exploded");
        IsOffsetFromCenter(3).Should().BeTrue("slice 3 is in ExplodedSlices and must explode too, not just the first match");
    }

    // ---- No-regression sibling: the pre-existing legacy scalar single-slice explosion path ----

    [Fact]
    public void Legacy_scalar_ExplodedSliceIndex_still_explodes_that_one_slice()
    {
        var request = Request(Chart(ChartType.Pie, c =>
        {
            c.ExplodedSliceIndex = 1;
            c.ExplodedSliceDistance = 0.2;
        }), ["A", "B"], [Series(0, "S1", 1, 1)]);
        var layout = ChartLayoutEngine.Layout(request);

        var plotCenter = layout.PlotArea.Center;
        var notExploded = layout.Series[0].Slices[0].Arc.Center;
        var exploded = layout.Series[0].Slices[1].Arc.Center;

        notExploded.Should().Be(new LayoutPoint(plotCenter.X, plotCenter.Y));
        var dx = exploded.X - plotCenter.X;
        var dy = exploded.Y - plotCenter.Y;
        Math.Sqrt(dx * dx + dy * dy).Should().BeGreaterThan(1);
    }
}
