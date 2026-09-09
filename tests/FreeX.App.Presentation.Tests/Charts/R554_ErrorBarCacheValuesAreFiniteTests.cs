using FluentAssertions;

using FreeX.App.Presentation.Charts;

namespace FreeX.App.Presentation.Tests.Charts;

/// <summary>
/// r554: <see cref="ChartRenderPolicyPlanner.ParseErrorBarRangeCache"/> reads the numeric cache
/// out of a chart part's error-bar XML, so every &lt;c:v&gt; is file-controlled. The parsed values
/// feed error-bar geometry in both ChartLayoutEngine and ChartRenderer, so an infinite one extends
/// a bar without bound and reaches axis range -- r485's unbounded-scale class rather than one
/// wrong whisker.
///
/// <para>Since .NET Core stopped throwing on overflow, "1e400" parses SUCCESSFULLY to Infinity, and
/// NumberStyles.Float accepts the literal "NaN" and "Infinity" as well. The remedy is the site's
/// own existing contract: a point whose value cannot be used is not added to the map, which is
/// exactly what this loop already did for a &lt;c:pt&gt; whose text failed to parse.</para>
/// </summary>
public sealed class R554_ErrorBarCacheValuesAreFiniteTests
{
    private static string Cache(string firstValue) =>
        "<numCache><pt idx=\"0\"><v>" + firstValue + "</v></pt>"
        + "<pt idx=\"1\"><v>2.5</v></pt></numCache>";

    [Theory]
    [InlineData("1e400")]
    [InlineData("-1e400")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("NaN")]
    public void A_cached_error_bar_value_that_is_not_finite_is_dropped(string hostile)
    {
        var parsed = ChartRenderPolicyPlanner.ParseErrorBarRangeCache(Cache(hostile));

        parsed.Should().NotBeNull();
        parsed!.Should().OnlyContain(v => double.IsFinite(v));

        // The site's existing contract for an unusable point: it is simply not collected, so the
        // sibling point survives on its own.
        parsed.Should().Equal(2.5);
    }

    [Fact]
    public void Ordinary_cached_values_are_still_read_in_index_order()
    {
        // Non-vacuity: a guard that dropped every point would satisfy the finiteness assertion
        // above while silently discarding real error bars.
        ChartRenderPolicyPlanner
            .ParseErrorBarRangeCache(
                "<numCache><pt idx=\"1\"><v>2.5</v></pt><pt idx=\"0\"><v>1.5</v></pt></numCache>")
            .Should().Equal(1.5, 2.5);
    }

    [Fact]
    public void Unparseable_text_is_still_dropped()
    {
        // Pins the pre-existing behaviour the new guard reuses, so a later change cannot separate
        // the two paths.
        ChartRenderPolicyPlanner
            .ParseErrorBarRangeCache(Cache("not a number"))
            .Should().Equal(2.5);
    }
}
