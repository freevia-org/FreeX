using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Text;
using FreeX.Core.Model;

using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// r552: a scatter/bubble series' X values come from its own CACHED CATEGORY TEXT, which is
/// file-controlled -- unlike the live-cell path beside it, where a cell can never hold a
/// non-finite value because the evaluator returns #NUM! instead. Since .NET Core stopped
/// throwing on overflow, double.TryParse returns TRUE with Infinity for an ordinary literal,
/// so "1e400" in a chart part parsed SUCCESSFULLY and did not take the unparseable fallback
/// the surrounding code already provides. An infinite X then reaches ChartLayoutEngine's axis
/// range and tick generation, which is r485's unbounded-scale class rather than one wrong dot.
///
/// <para>The remedy is the site's own existing contract: a point whose cached text cannot be
/// used falls back to its positional index, which is exactly what this line already did for
/// text that failed to parse at all.</para>
/// </summary>
public sealed class R552_ScatterCachedXValuesAreFiniteTests
{
    private sealed class FakeTextMeasurer : ITextMeasurer
    {
        public TextSize Measure(string? text, string? fontFamily, double fontSize, bool bold, bool italic) =>
            string.IsNullOrEmpty(text) ? TextSize.Empty : new TextSize(text.Length * fontSize * 0.5, fontSize);
    }

    private static readonly PlotRect Plot = new(8, 12, 360, 240);

    private static bool NeverCalled(uint row, uint col, out double value, out string displayText)
    {
        value = 0;
        displayText = "";
        return false;
    }

    private static IReadOnlyList<double>? XValuesFor(params string[] cachedCategoryText)
    {
        var chart = new ChartModel
        {
            Type = ChartType.Scatter,
            FirstRowIsHeader = true,
            FirstColIsCategories = true,
            DataRange = new GridRange(new CellAddress(default, 1, 1), new CellAddress(default, 1, 1)),
            EmbeddedSeriesData =
            [
                new ChartEmbeddedSeriesData(
                    0,
                    "Points",
                    cachedCategoryText,
                    Enumerable.Range(1, cachedCategoryText.Length).Select(v => (double?)v).ToArray()),
            ],
        };

        return ChartLayoutRequestBuilder
            .TryBuild(chart, Plot, NeverCalled, new FakeTextMeasurer())!
            .Series[0]
            .XValues;
    }

    [Theory]
    [InlineData("1e400")]
    [InlineData("-1e400")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("NaN")]
    public void A_cached_x_value_that_is_not_finite_falls_back_to_its_index(string hostile)
    {
        var xs = XValuesFor("10", hostile, "30");

        xs.Should().NotBeNull();
        xs!.Should().OnlyContain(x => double.IsFinite(x));

        // The site's existing contract for unusable text: fall back to the positional index.
        xs[1].Should().Be(1);
    }

    [Fact]
    public void Ordinary_cached_x_values_are_still_read_verbatim()
    {
        // Non-vacuity: a guard that rejected everything would satisfy the finiteness assertion
        // above while silently replacing every real X value with its index.
        var xs = XValuesFor("10", "20.5", "-30");

        xs.Should().NotBeNull();
        xs!.Should().Equal(10d, 20.5d, -30d);
    }

    [Fact]
    public void Unparseable_text_still_falls_back_to_its_index()
    {
        // Pins the pre-existing behaviour the new guard reuses, so a later change cannot
        // "simplify" the two paths apart.
        var xs = XValuesFor("10", "not a number", "30");

        xs.Should().NotBeNull();
        xs![1].Should().Be(1);
    }
}
