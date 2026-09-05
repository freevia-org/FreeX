using FluentAssertions;
using FreeP.Core.IO;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r429: a chart's data must survive a .pptx round trip -- its type, its categories, its series and
/// their values.
///
/// <para>Charts are the one shape whose content is a DATASET rather than an appearance, which
/// changes what a loss means. A dropped shadow is a cosmetic regression; a dropped series is a chart
/// that draws confidently and states something different from what the author measured. Nobody looks
/// at a chart and wonders whether a series is missing -- they read the bars that are there.</para>
///
/// <para>Values are <c>double?</c>, so a gap in a series is meaningful data, not padding. The tests
/// keep a null in the middle of a series for that reason: a writer that collapsed nulls would shift
/// every later point one category to the left while leaving a chart that still looks plausible.</para>
/// </summary>
public sealed class R429_ChartDataReachesTheFileTests
{
    private static ChartShape RoundTrip(Action<ChartShape> configure)
    {
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Categories.AddRange(["Alpha", "Beta", "Gamma"]);

        var series = new ChartSeries { Name = "Revenue" };
        series.Values.AddRange([1.5, null, 3.5]);
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
        shape.Should().NotBeNull("the chart shape must survive before its data can be judged");
        shape!.Chart.Should().NotBeNull("a chart shape without its chart is an empty frame");
        return shape.Chart!;
    }

    [Fact]
    public void TheChartTypeSurvives()
    {
        // A chart that comes back as the default type still renders -- as the wrong kind of chart,
        // which reads as someone's design decision rather than a load fault.
        RoundTrip(chart => chart.ChartType = ChartType.BarClustered).ChartType
            .Should().Be(ChartType.BarClustered, "the type is what the reader interprets the data through");
    }

    [Fact]
    public void CategoriesSurviveInOrder()
    {
        // Order matters as much as membership: categories reordered against their values relabels
        // every point without changing any number.
        RoundTrip(_ => { }).Categories.Should().Equal(
            ["Alpha", "Beta", "Gamma"], "categories label the points, so their order is part of the data");
    }

    [Fact]
    public void SeriesValuesSurviveIncludingTheGap()
    {
        var chart = RoundTrip(_ => { });

        chart.Series.Should().HaveCount(1);
        chart.Series[0].Values.Should().Equal(
            [1.5, null, 3.5],
            "the null is a missing measurement, not padding -- collapsing it shifts every later " +
            "point one category left while leaving a chart that still looks plausible");
    }

    [Fact]
    public void TheSeriesNameSurvives()
    {
        // The series name is the legend entry. Losing it does not remove the data; it removes the
        // reader's means of telling two series apart.
        RoundTrip(_ => { }).Series[0].Name
            .Should().Be("Revenue", "the series name is what the legend shows");
    }

    [Fact]
    public void ASecondSeriesIsNotMergedIntoTheFirst()
    {
        // One series is not enough to catch a writer that emits the first series for all of them, or
        // concatenates their points into one.
        var chart = RoundTrip(configured =>
        {
            var second = new ChartSeries { Name = "Costs" };
            second.Values.AddRange([0.5, 1.0, 1.5]);
            configured.Series.Add(second);
        });

        chart.Series.Should().HaveCount(2, "both series must survive as separate series");
        chart.Series[0].Values.Should().Equal([1.5, null, 3.5], "the first series keeps its own points");
        chart.Series[1].Values.Should().Equal([0.5, 1.0, 1.5], "and the second keeps its own");
        chart.Series[1].Name.Should().Be("Costs");
    }
}
