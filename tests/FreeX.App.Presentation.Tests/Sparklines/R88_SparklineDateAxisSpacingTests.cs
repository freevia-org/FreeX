using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Sparklines;

namespace FreeX.App.Presentation.Tests.Sparklines;

/// <summary>
/// R88-render-sparkline-5-4: a line sparkline whose group has a Date Axis Type configured must space
/// its points proportionally to elapsed time, not evenly by array index. Excel bunches unevenly-spaced
/// dates together and spreads widely-spaced ones apart; SparklineLayoutEngine previously always used
/// <c>i / (values.Count - 1)</c> regardless of any date value, so this never happened.
/// </summary>
public sealed class R88_SparklineDateAxisSpacingTests
{
    private static readonly LayoutRect Cell = new(10, 20, 100, 40);

    // Jan 1, Jan 2, Jan 20, Jan 21 as day-offsets from Jan 1: 0, 1, 19, 20.
    private static readonly double[] UnevenDatePositions = [0, 1, 19, 20];

    [Fact]
    public void DateAxis_spaces_points_proportionally_to_elapsed_time_not_index()
    {
        var values = new double[] { 1, 1, 1, 1 }; // flat values isolate the X-spacing behavior.

        var layout = SparklineLayoutEngine.CalculateLineLayout(values, Cell, overrideMin: null, overrideMax: null, UnevenDatePositions);

        layout.Segments.Should().HaveCount(3);
        // posSpan = 20 (max 20 - min 0); X = rect.Left + rect.Width * (position - min) / posSpan.
        layout.Segments[0].Start.X.Should().Be(10); // position 0 -> fraction 0
        layout.Segments[0].End.X.Should().Be(15); // position 1 -> fraction 0.05 -> +5
        layout.Segments[1].End.X.Should().Be(105); // position 19 -> fraction 0.95 -> +95
        layout.Segments[2].End.X.Should().Be(110); // position 20 -> fraction 1 -> +100

        // Without a date axis the same four points would be evenly spaced by index (10, 43.33, 76.67, 110)
        // -- assert the date-scaled points actually differ from that even-spacing baseline in the middle.
        var evenLayout = SparklineLayoutEngine.CalculateLineLayout(values, Cell);
        layout.Segments[0].End.X.Should().NotBe(evenLayout.Segments[0].End.X);
        layout.Segments[1].End.X.Should().NotBe(evenLayout.Segments[1].End.X);
    }

    // No-regression sibling: omitting the date positions (null) must keep the pre-existing even
    // by-index spacing exactly as before this fix.
    [Fact]
    public void No_date_positions_falls_back_to_even_index_spacing()
    {
        var values = new double[] { 0, 10, 0, 10 };

        var withoutDates = SparklineLayoutEngine.CalculateLineLayout(values, Cell, overrideMin: null, overrideMax: null, datePositions: null);
        var legacyOverload = SparklineLayoutEngine.CalculateLineLayout(values, Cell, overrideMin: null, overrideMax: null);

        withoutDates.Segments.Should().Equal(legacyOverload.Segments);
        withoutDates.Segments[0].Start.X.Should().Be(10);
        withoutDates.Segments[2].End.X.Should().Be(110);
    }

    // Defensive sibling: a mismatched-length date-positions array (e.g. stale data) must not throw or
    // corrupt the layout -- it silently falls back to even by-index spacing.
    [Fact]
    public void Mismatched_length_date_positions_falls_back_safely()
    {
        var values = new double[] { 1, 2, 3 };
        var wrongLength = new double[] { 0, 5 };

        var layout = SparklineLayoutEngine.CalculateLineLayout(values, Cell, overrideMin: null, overrideMax: null, wrongLength);

        layout.Segments.Should().HaveCount(2);
        layout.Segments[0].Start.X.Should().Be(10);
        layout.Segments[1].End.X.Should().Be(110);
    }
}
