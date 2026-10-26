using System.Xml.Linq;

using FluentAssertions;

using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r565: a chart series' embedded numeric cache (<c>c:val/c:numRef/c:numCache</c>) holds the values
/// Excel last plotted, and FreeX reads it whenever the series formula is a named range it cannot
/// resolve to a rectangle. Every <c>c:v</c> in it is file-controlled.
///
/// <para>This is the direct sibling of r554, which fixed the same shape in
/// <c>ChartRenderPolicyPlanner.ParseErrorBarRangeCache</c>. That round guarded the ERROR BAR cache
/// and left the SERIES cache -- the one holding the plotted values themselves -- unguarded in a
/// different reader. It was found by a repo-wide census rather than by following the r554 fix,
/// which is the point: the sibling lived in another file, under another name.</para>
/// </summary>
public sealed class R565_ChartSeriesCacheValuesAreFiniteTests
{
    private static readonly XNamespace C = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    // The reader only consults the embedded cache when the series formulas are named ranges it
    // cannot resolve to a rectangle, so the fixture uses a defined name rather than "Sheet1!$A$1".
    private static XElement Series(params string[] cachedValues)
    {
        var points = cachedValues.Select((v, i) =>
            new XElement(C + "pt", new XAttribute("idx", i), new XElement(C + "v", v)));

        return new XElement(
            C + "ser",
            new XElement(C + "idx", new XAttribute("val", 0)),
            new XElement(C + "order", new XAttribute("val", 0)),
            new XElement(
                C + "val",
                new XElement(
                    C + "numRef",
                    new XElement(C + "f", "MyDynamicRange"),
                    new XElement(
                        C + "numCache",
                        new XElement(C + "ptCount", new XAttribute("val", cachedValues.Length)),
                        points))),
            new XElement(
                C + "cat",
                new XElement(
                    C + "strRef",
                    new XElement(C + "f", "MyCategoryRange"),
                    new XElement(
                        C + "strCache",
                        new XElement(C + "ptCount", new XAttribute("val", cachedValues.Length))))));
    }

    private static IReadOnlyList<double?>? ValuesFor(params string[] cachedValues)
    {
        var data = XlsxChartSeriesRangeReader.TryReadEmbeddedSeriesData(
            [Series(cachedValues)],
            default);

        return data is null || data.Count == 0 ? null : data[0].Values;
    }

    [Theory]
    [InlineData("1e400")]
    [InlineData("-1e400")]
    [InlineData("Infinity")]
    [InlineData("NaN")]
    public void A_cached_series_value_that_is_not_finite_is_dropped(string hostile)
    {
        var values = ValuesFor("10", hostile, "30");

        values.Should().NotBeNull();
        values!.Where(v => v.HasValue).Select(v => v!.Value)
            .Should().OnlyContain(v => double.IsFinite(v));
    }

    [Fact]
    public void An_unusable_point_does_not_discard_its_neighbours()
    {
        // The array is sparse by design -- a point the reader cannot use becomes null and the rest
        // of the series survives, which is what it already does for text that fails to parse.
        var values = ValuesFor("10", "1e400", "30");

        values.Should().NotBeNull();
        values![0].Should().Be(10);
        values[2].Should().Be(30);
    }

    [Fact]
    public void Ordinary_cached_values_are_still_read()
    {
        // Non-vacuity: a guard that dropped every point would satisfy the assertions above while
        // silently emptying the cache every chart with a named-range formula depends on.
        ValuesFor("10", "20.5", "-30").Should().Equal(10d, 20.5d, -30d);
    }
}
