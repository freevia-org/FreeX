using FluentAssertions;
using FreeP.Core.IO;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r432: a chart's legend and manual plot-area layout must survive a .pptx round trip.
///
/// <para>Completes the chart surface after data (r429), axes (r430) and labels (r431). These two are
/// the LAYOUT half, and their failure is quieter than the others: a legend that returns in the wrong
/// position, or a plot area that loses a manual layout and reverts to automatic, produces a chart
/// that is still correct and still readable -- just not the one the author arranged, usually because
/// they moved it to stop it covering the data.</para>
///
/// <para>The two manual layouts are separate objects of the SAME type, which is the specific hazard
/// here: a writer that emitted the plot area's layout for the legend, or read one back onto the
/// other, produces a chart where both elements sit in one place. They are therefore given different
/// coordinates and asserted separately.</para>
/// </summary>
public sealed class R432_ChartLegendAndPlotAreaReachTheFileTests
{
    private static ChartShape RoundTrip(Action<ChartShape> configure)
    {
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Categories.AddRange(["Alpha", "Beta"]);

        var series = new ChartSeries { Name = "Revenue" };
        series.Values.AddRange([1.5, 2.5]);
        chart.Series.Add(series);

        configure(chart);

        var presentation = new Presentation();
        var slide = new Slide();
        slide.Shapes.Add(new SlideShape
        {
            Id = 2,
            Name = "Chart",
            Kind = SlideShapeKind.Chart,
            OffsetXEmu = 100000,
            OffsetYEmu = 200000,
            ExtentCxEmu = 4000000,
            ExtentCyEmu = 3000000,
            Chart = chart,
        });
        presentation.Slides.Add(slide);

        using var stream = new MemoryStream();
        PptxPackageWriter.Write(presentation, stream);
        stream.Position = 0;

        var shape = PptxPackageReader.Read(stream).Slides[0].Shapes.FirstOrDefault();
        shape?.Chart.Should().NotBeNull("the chart must survive before its layout can be judged");
        return shape!.Chart!;
    }

    [Theory]
    [InlineData(LegendPosition.Right)]
    [InlineData(LegendPosition.Left)]
    [InlineData(LegendPosition.Top)]
    [InlineData(LegendPosition.Bottom)]
    public void TheLegendPositionSurvives(LegendPosition position)
    {
        // Each position asserted separately: a writer that emitted one token for all of them passes
        // a single-value test, and a legend on the wrong side is a layout the author did not choose.
        RoundTrip(chart => chart.Legend = position).Legend
            .Should().Be(position, "the legend position is a layout decision, not a default");
    }

    [Fact]
    public void TheLegendOverlayFlagSurvives()
    {
        // Overlay decides whether the legend sits over the plot or beside it -- the difference
        // between a legend that covers data and one that does not.
        RoundTrip(chart =>
        {
            chart.Legend = LegendPosition.Right;
            chart.LegendOverlay = true;
        }).LegendOverlay.Should().BeTrue("overlay is why the author placed it where they did");
    }

    [Fact]
    public void AManualPlotAreaLayoutSurvives()
    {
        var chart = RoundTrip(configured => configured.PlotAreaManualLayout = new ChartManualLayout
        {
            X = 0.1,
            Y = 0.2,
            Width = 0.7,
            Height = 0.6,
        });

        chart.PlotAreaManualLayout.Should().NotBeNull(
            "a manual layout that reverts to automatic undoes the arrangement the author made");
        chart.PlotAreaManualLayout!.X.Should().BeApproximately(0.1, 1e-6);
        chart.PlotAreaManualLayout.Y.Should().BeApproximately(0.2, 1e-6);
        chart.PlotAreaManualLayout.Width.Should().BeApproximately(0.7, 1e-6);
        chart.PlotAreaManualLayout.Height.Should().BeApproximately(0.6, 1e-6);
    }

    [Fact]
    public void ThePlotAreaAndLegendLayoutsAreNotConfused()
    {
        // The hazard specific to this pair: two separate objects of the SAME type. A writer that
        // emitted one for both, or a reader that assigned one to the other, leaves both elements
        // sitting in the same place -- and each single-layout test above would still pass.
        var chart = RoundTrip(configured =>
        {
            // Legend position is a COMPANION, not decoration: without it the writer emits no
            // <c:legend> element, so a legend layout has nowhere to live and is correctly dropped.
            // The first version of this test omitted it and read the empty result as a defect --
            // the same interdependence trap as r419's list fields and r428's effect flags. Pinned
            // by LegendLayoutNeedsALegendToLiveIn below rather than left as a bare workaround.
            configured.Legend = LegendPosition.Right;
            configured.PlotAreaManualLayout = new ChartManualLayout { X = 0.1, Y = 0.2, Width = 0.7, Height = 0.6 };
            configured.LegendManualLayout = new ChartManualLayout { X = 0.8, Y = 0.3, Width = 0.15, Height = 0.4 };
        });

        chart.PlotAreaManualLayout!.X.Should().BeApproximately(0.1, 1e-6, "the plot area keeps its own position");
        chart.LegendManualLayout.Should().NotBeNull("the legend layout must survive as its own object");
        chart.LegendManualLayout!.X.Should().BeApproximately(0.8, 1e-6, "and the legend keeps its own");
        chart.LegendManualLayout.Width.Should().BeApproximately(0.15, 1e-6);
    }

    /// <summary>
    /// Pins the companion requirement above as understood behaviour rather than a workaround.
    /// </summary>
    /// <remarks>
    /// A legend layout describes where a legend sits, so with no legend the writer correctly emits
    /// nothing -- measured, not assumed. If a future writer starts persisting an orphaned legend
    /// layout, this fails and forces the companion in the test above to be revisited instead of
    /// sitting there forever as an unexplained special case that might be masking a defect.
    /// </remarks>
    [Fact]
    public void ALegendLayoutNeedsALegendToLiveIn()
    {
        var withoutLegend = RoundTrip(configured =>
            configured.LegendManualLayout = new ChartManualLayout { X = 0.8, Y = 0.3, Width = 0.15, Height = 0.4 });

        withoutLegend.LegendManualLayout.Should().BeNull(
            "with no legend there is no element for the layout to describe, so writing nothing is correct");

        var withLegend = RoundTrip(configured =>
        {
            configured.Legend = LegendPosition.Right;
            configured.LegendManualLayout = new ChartManualLayout { X = 0.8, Y = 0.3, Width = 0.15, Height = 0.4 };
        });

        withLegend.LegendManualLayout!.X.Should().BeApproximately(
            0.8, 1e-6, "and with a legend present the layout must survive");
    }

    [Fact]
    public void AChartWithNoManualLayoutGainsNone()
    {
        // Every assertion above checks that something set survives, so a reader that invented a
        // layout would satisfy them all -- and an invented manual layout PINS a chart that was
        // meant to size itself to its frame.
        var chart = RoundTrip(_ => { });

        chart.PlotAreaManualLayout.Should().BeNull("an auto-laid-out chart must not acquire a fixed plot area");
        chart.LegendManualLayout.Should().BeNull();
    }
}
