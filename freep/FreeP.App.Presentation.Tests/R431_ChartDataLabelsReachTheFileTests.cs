using FluentAssertions;
using FreeP.Core.IO;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r431: chart data labels must survive a .pptx round trip, at the chart level and at the series
/// level, without one being mistaken for the other.
///
/// <para>Data labels print NUMBERS onto the chart, and a reader takes a printed number as fact --
/// more readily than a bar height, which they know is approximate. That makes a label defect the
/// most direct form of the pattern these rounds keep finding: a chart that shows percentages where
/// the author asked for values is not obviously wrong, it is just wrong.</para>
///
/// <para>The model carries labels at three levels -- chart, series and point -- so the override
/// relationship is as much a part of the data as the flags. A series whose own settings are lost
/// falls back to the chart's and still shows labels, which is precisely the failure that looks
/// deliberate.</para>
/// </summary>
public sealed class R431_ChartDataLabelsReachTheFileTests
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
        shape?.Chart.Should().NotBeNull("the chart must survive before its labels can be judged");
        return shape!.Chart!;
    }

    [Fact]
    public void ChartLevelLabelFlagsSurviveIndividually()
    {
        // Asserted flag by flag, and with a deliberate MIX of true and false. A writer that emitted
        // one flag for all of them would pass a test that set them all true.
        var chart = RoundTrip(configured => configured.DataLabels = new ChartDataLabels
        {
            ShowValue = true,
            ShowPercent = false,
            ShowCategoryName = true,
            ShowSeriesName = false,
            ShowLegendKey = true,
        });

        chart.DataLabels.Should().NotBeNull("labels the author switched on must not vanish");
        chart.DataLabels!.ShowValue.Should().BeTrue("a chart showing percentages where the author asked for values is just wrong");
        chart.DataLabels.ShowPercent.Should().BeFalse();
        chart.DataLabels.ShowCategoryName.Should().BeTrue();
        chart.DataLabels.ShowSeriesName.Should().BeFalse();
        chart.DataLabels.ShowLegendKey.Should().BeTrue();
    }

    [Fact]
    public void TheLabelNumberFormatSurvives()
    {
        // The number format is what turns 1.5 into "1.50" or "150%". Losing it changes the printed
        // number without changing the data behind it.
        var chart = RoundTrip(configured => configured.DataLabels = new ChartDataLabels
        {
            ShowValue = true,
            NumberFormat = "#,##0.00",
        });

        chart.DataLabels!.NumberFormat.Should().Be("#,##0.00", "the format decides the number the reader sees");
    }

    [Fact]
    public void TheLabelPositionSurvives()
    {
        var chart = RoundTrip(configured => configured.DataLabels = new ChartDataLabels
        {
            ShowValue = true,
            Position = DataLabelPosition.OutsideEnd,
        });

        chart.DataLabels!.Position.Should().Be(DataLabelPosition.OutsideEnd);
    }

    [Fact]
    public void ASeriesKeepsItsOwnLabelsRatherThanTheChartS()
    {
        // The override relationship. A series whose own settings are lost falls back to the chart's
        // and STILL SHOWS LABELS -- the failure that looks deliberate, and the one no single-level
        // test can catch.
        var chart = RoundTrip(configured =>
        {
            configured.DataLabels = new ChartDataLabels { ShowValue = true, ShowPercent = false };
            configured.Series[0].DataLabels = new ChartDataLabels { ShowValue = false, ShowPercent = true };
        });

        chart.Series[0].DataLabels.Should().NotBeNull("the series override must survive as its own object");
        chart.Series[0].DataLabels!.ShowPercent.Should().BeTrue("the series asked for percentages");
        chart.Series[0].DataLabels!.ShowValue.Should().BeFalse("and explicitly not for values");

        chart.DataLabels!.ShowValue.Should().BeTrue("while the chart-level setting keeps its own answer");
        chart.DataLabels.ShowPercent.Should().BeFalse();
    }

    [Fact]
    public void AChartWithNoLabelsGainsNone()
    {
        // Every assertion above checks that something switched ON survives, so a reader that
        // invented labels would satisfy them all -- and invented labels print numbers onto a chart
        // that the author chose to leave clean.
        var chart = RoundTrip(_ => { });

        (chart.DataLabels?.HasAny ?? false).Should().BeFalse("a chart with no labels must not acquire any");
        (chart.Series[0].DataLabels?.HasAny ?? false).Should().BeFalse();
    }
}
