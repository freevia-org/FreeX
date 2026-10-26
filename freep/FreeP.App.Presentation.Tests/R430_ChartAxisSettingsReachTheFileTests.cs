using FluentAssertions;
using FreeP.Core.IO;
using FreeP.Core.Model;
using Xunit;

namespace FreeP.App.Compositor.Tests;

/// <summary>
/// r430: a chart's axis settings must survive a .pptx round trip.
///
/// <para>r429 covered the chart's DATA. The axis decides how that data is read, and its losses have
/// the same signature: a chart whose value axis loses an explicit minimum silently rebases to zero,
/// which flattens differences the author scaled the axis to show. Nothing looks broken -- the bars
/// are simply less dramatic than the author saw, and the reader has no way to know the scale moved
/// under them.</para>
///
/// <para>An explicit minimum is the sharpest case, because auto-scaling produces a perfectly
/// plausible chart. That is why <c>Min</c>/<c>Max</c> are asserted as VALUES rather than merely as
/// non-null: a bound that comes back as 0 instead of 40 is not a missing setting, it is a different
/// claim about the data.</para>
/// </summary>
public sealed class R430_ChartAxisSettingsReachTheFileTests
{
    private static ChartShape RoundTrip(Action<ChartShape> configure)
    {
        var chart = new ChartShape { ChartType = ChartType.ColumnClustered };
        chart.Categories.AddRange(["Alpha", "Beta", "Gamma"]);

        var series = new ChartSeries { Name = "Revenue" };
        series.Values.AddRange([41.0, 42.0, 43.0]);
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
        shape?.Chart.Should().NotBeNull("the chart must survive before its axes can be judged");
        return shape!.Chart!;
    }

    [Fact]
    public void AnExplicitValueAxisRangeSurvives()
    {
        // The sharpest case in the file. These values sit at 41-43, so an axis rebased to 0 draws
        // three bars of nearly equal height -- a plausible chart making a different claim than the
        // author's 40-45 scale, which shows the differences.
        var chart = RoundTrip(configured =>
        {
            configured.ValueAxis.Min = 40;
            configured.ValueAxis.Max = 45;
        });

        chart.ValueAxis.Min.Should().Be(40, "an axis rebased to zero flattens the differences the author scaled to show");
        chart.ValueAxis.Max.Should().Be(45);
    }

    [Fact]
    public void ExplicitTickUnitsSurvive()
    {
        var chart = RoundTrip(configured =>
        {
            configured.ValueAxis.MajorUnit = 2.5;
            configured.ValueAxis.MinorUnit = 0.5;
        });

        chart.ValueAxis.MajorUnit.Should().Be(2.5, "the major unit sets the gridline spacing the reader counts by");
        chart.ValueAxis.MinorUnit.Should().Be(0.5, "major and minor are separate fields and both must survive");
    }

    [Fact]
    public void TheAxisTitleAndNumberFormatSurvive()
    {
        var chart = RoundTrip(configured =>
        {
            configured.ValueAxis.Title = "Millions";
            configured.ValueAxis.NumberFormatCode = "#,##0.00";
        });

        chart.ValueAxis.Title.Should().Be("Millions", "an axis title states the unit the numbers are in");
        chart.ValueAxis.NumberFormatCode.Should().Be("#,##0.00");
    }

    [Fact]
    public void GridlineVisibilitySurvivesInBothDirections()
    {
        // HasMajorGridlines defaults to TRUE and HasMinorGridlines to false, so each is set to the
        // opposite of its own default -- the r424 rule. Probing both as true would let a writer that
        // emitted nothing pass on the major one.
        var chart = RoundTrip(configured =>
        {
            configured.ValueAxis.HasMajorGridlines = false;
            configured.ValueAxis.HasMinorGridlines = true;
        });

        chart.ValueAxis.HasMajorGridlines.Should().BeFalse("this defaults to true, so losing it looks like nothing happened");
        chart.ValueAxis.HasMinorGridlines.Should().BeTrue();
    }

    [Fact]
    public void TheValueAxisIsNotConfusedWithTheCategoryAxis()
    {
        // The two axes are separate objects written into one chart part. A writer that emitted the
        // value axis settings for both, or read them back onto the wrong one, would pass every
        // single-axis assertion above.
        var chart = RoundTrip(configured =>
        {
            configured.ValueAxis.Title = "Value side";
            configured.CategoryAxis.Title = "Category side";
        });

        chart.ValueAxis.Title.Should().Be("Value side");
        chart.CategoryAxis.Title.Should().Be("Category side", "each axis must keep its OWN title");
    }

    [Fact]
    public void AnAxisWithNoExplicitRangeGainsNone()
    {
        // Every assertion above checks that something set survives, so a reader that invented bounds
        // would satisfy them all -- and invented bounds would CLIP data out of view.
        var chart = RoundTrip(_ => { });

        chart.ValueAxis.Min.Should().BeNull("an auto-scaled axis must not acquire a fixed minimum");
        chart.ValueAxis.Max.Should().BeNull("invented bounds would clip the author's data out of view");
    }
}
