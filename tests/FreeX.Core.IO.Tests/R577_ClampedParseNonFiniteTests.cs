using FluentAssertions;
using System.Xml.Linq;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for the two sites round 577's new
/// <c>R486_ParsedDoubleGuardsRejectInfinityTests.NoParsedDoubleIsBoundedOnlyByAClamp</c> rule found
/// on its first run. Both bounded a value parsed out of a FILE with
/// <see cref="Math.Clamp(double, double, double)"/> alone, which bounds Infinity but PROPAGATES NaN
/// -- and <c>double.TryParse</c> accepts the literal spelling "NaN".
/// <para>
/// The error-bar site is a sibling r554 missed: r554 guarded this same feature's error-bar range
/// CACHE, which lives in another file, so following the fix by file never reached the fixed VALUE
/// read here.
/// </para>
/// </summary>
public sealed class R577_ClampedParseNonFiniteTests
{
    [Theory]
    [InlineData("NaN")]
    [InlineData("-NaN")]
    public void SourceRectangleRatio_NonFinitePercentage_IsReadAsNoCrop(string percentage)
    {
        // A NaN crop ratio propagates into the picture's layout rectangle; 0 (no crop) is the
        // result this method already gives for a percentage it cannot read at all.
        var ratio = XlsxSourceRectangleRatioCodec.Parse(percentage);

        double.IsFinite(ratio).Should().BeTrue($"\"{percentage}\" produced the ratio {ratio}");
        ratio.Should().Be(0);
    }

    [Theory]
    [InlineData("25000", 0.25)]
    [InlineData("-25000", -0.25)]
    [InlineData("100000", 1)]
    // Non-vacuity: the finite test must not have turned every crop into no-crop, and the clamp's
    // own outward-crop range must still work.
    public void SourceRectangleRatio_FinitePercentages_StillDecode(string percentage, double expected)
        => XlsxSourceRectangleRatioCodec.Parse(percentage).Should().Be(expected);

    [Fact]
    public void SourceRectangleRatio_OverflowingPercentage_ClampsRatherThanGoingInfinite()
    {
        // Infinity is the case Math.Clamp really did bound -- it produced a full crop of 1. It now
        // takes the same unreadable-value result as NaN, which is the aligned answer: a srcRect
        // percentage is an integer in OOXML, so "1e400" is not a crop the file can even express,
        // and silently cropping the picture to nothing is worse than not cropping it.
        XlsxSourceRectangleRatioCodec.Parse("1e400").Should().Be(0);
    }

    private static ChartModel ReadErrorBarValue(string rawValue)
    {
        XNamespace c = "http://schemas.openxmlformats.org/drawingml/2006/chart";
        var series = new XElement(c + "ser",
            new XElement(c + "idx", new XAttribute("val", 0)),
            new XElement(c + "errBars",
                new XElement(c + "errValType", new XAttribute("val", "fixedVal")),
                new XElement(c + "val", new XAttribute("val", rawValue))));

        var chart = new ChartModel();
        XlsxChartTrendlineErrorBarReader.ApplyErrorBars(series, chart);
        return chart;
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("1e400")]
    public void ErrorBarValue_NonFiniteFixedValue_IsIgnored(string rawValue)
    {
        var chart = ReadErrorBarValue(rawValue);

        double.IsFinite(chart.ErrorBarValue).Should().BeTrue(
            $"errBars/val/@val \"{rawValue}\" produced ErrorBarValue {chart.ErrorBarValue}");
        chart.ErrorBarValue.Should().Be(5, "the model's default must survive an unreadable value");
    }

    [Fact]
    public void ErrorBarValue_FiniteFixedValue_StillReadsThrough()
    {
        // Non-vacuity: the guard must not have stopped the attribute being read at all -- without
        // this the theory above would pass on a reader that ignored every value.
        ReadErrorBarValue("12.5").ErrorBarValue.Should().Be(12.5);
    }

    [Fact]
    public void ErrorBarValue_OutOfRangeFiniteValue_StillClamps()
    {
        // The clamp itself is unchanged: it is only no longer the ONLY thing standing between the
        // file and the model.
        ReadErrorBarValue("99999").ErrorBarValue.Should().Be(1000);
    }
}
