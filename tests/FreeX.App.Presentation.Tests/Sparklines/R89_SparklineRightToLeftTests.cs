using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Sparklines;

namespace FreeX.App.Presentation.Tests.Sparklines;

/// <summary>
/// R89-render-sparkline-remainder: the sparkline group's "Plot Data Right-to-Left" option is modeled
/// (<c>SparklineModel.RightToLeft</c>) but was never consumed by <see cref="SparklineLayoutEngine"/>,
/// so it never changed the plotted geometry. Excel's option mirrors the plotting order so the FIRST
/// data point lands at the RIGHT edge of the cell and the LAST at the left, for line, column, and
/// win/loss sparklines alike, with the vertical scale and axis placement unchanged.
/// </summary>
public sealed class R89_SparklineRightToLeftTests
{
    private static readonly LayoutRect Cell = new(10, 20, 100, 40);

    [Fact]
    public void Line_RightToLeft_PutsFirstPointAtRightEdgeAndLastAtLeft()
    {
        var values = new double[] { 0, 10, 0, 10 };

        var layout = SparklineLayoutEngine.CalculateLineLayout(
            values, Cell, overrideMin: null, overrideMax: null, datePositions: null, rightToLeft: true);

        layout.Segments.Should().HaveCount(3);
        // Even by-index spacing mirrored: index 0 (first value) now sits at the right edge (X=110),
        // index 3 (last value) sits at the left edge (X=10) -- the reverse of plain left-to-right.
        layout.Segments[0].Start.X.Should().Be(110); // first point, mirrored to the right edge
        layout.Segments[0].Start.Y.Should().Be(60); // value 0 (the min) -> still the bottom of the cell
        layout.Segments[2].End.X.Should().Be(10); // last point, mirrored to the left edge
        layout.Segments[2].End.Y.Should().Be(20); // value 10 (the max) -> the top of the cell, unaffected by mirroring

        // Cross-check against the non-mirrored (plain) layout: mirroring moves each point's X to the
        // opposite side of the cell but leaves its Y (i.e. its value) exactly as it was -- a pure
        // horizontal reflection, not a value re-sort.
        var plain = SparklineLayoutEngine.CalculateLineLayout(values, Cell);
        layout.Segments[0].Start.X.Should().Be(Cell.Left + Cell.Right - plain.Segments[0].Start.X);
        layout.Segments[0].Start.Y.Should().Be(plain.Segments[0].Start.Y);
        layout.Segments[2].End.X.Should().Be(Cell.Left + Cell.Right - plain.Segments[2].End.X);
        layout.Segments[2].End.Y.Should().Be(plain.Segments[2].End.Y);
    }

    [Fact]
    public void GetLinePoints_RightToLeft_MirrorsPositionsButKeepsOriginalIndex()
    {
        var values = new double[] { 1, 2, 3, 4 };

        var points = SparklineLayoutEngine.GetLinePoints(values, Cell, overrideMin: null, overrideMax: null, rightToLeft: true);

        points.Should().HaveCount(4);
        // Index 0 (the first data point) is still reported as Index 0 -- callers picking "first/last"
        // markers by index keep working -- but its geometry is now at the right edge.
        points[0].Index.Should().Be(0);
        points[0].Point.X.Should().Be(110);
        points[3].Index.Should().Be(3);
        points[3].Point.X.Should().Be(10);
    }

    [Fact]
    public void Column_RightToLeft_MirrorsBarSlots()
    {
        var values = new double[] { 1, 2, 3 };

        var layout = SparklineLayoutEngine.CalculateColumnLayout(values, Cell, winLoss: false, overrideMaxAbs: null, rightToLeft: true);
        var plain = SparklineLayoutEngine.CalculateColumnLayout(values, Cell, winLoss: false);

        layout.Bars.Should().HaveCount(3);
        // The first value's bar (plain index 0, leftmost slot) now occupies the rightmost slot, and
        // vice versa for the last value's bar -- heights/negativity are untouched by mirroring.
        layout.Bars[0].Rect.X.Should().Be(plain.Bars[2].Rect.X);
        layout.Bars[2].Rect.X.Should().Be(plain.Bars[0].Rect.X);
        layout.Bars[0].Rect.Width.Should().Be(plain.Bars[0].Rect.Width);
        layout.Bars[0].Rect.Height.Should().Be(plain.Bars[0].Rect.Height);
    }

    [Fact]
    public void WinLoss_RightToLeft_MirrorsBarSlots()
    {
        var values = new double[] { 5, -2, 3 };

        var layout = SparklineLayoutEngine.CalculateColumnLayout(values, Cell, winLoss: true, overrideMaxAbs: null, rightToLeft: true);
        var plain = SparklineLayoutEngine.CalculateColumnLayout(values, Cell, winLoss: true);

        layout.Bars.Should().HaveCount(3);
        layout.Bars[0].Rect.X.Should().Be(plain.Bars[2].Rect.X);
        layout.Bars[2].Rect.X.Should().Be(plain.Bars[0].Rect.X);
        // Sign / negativity of each bar is preserved by mirroring -- only slot position moves.
        layout.Bars[0].IsNegative.Should().Be(plain.Bars[0].IsNegative);
        layout.Bars[1].IsNegative.Should().Be(plain.Bars[1].IsNegative);
        layout.Bars[2].IsNegative.Should().Be(plain.Bars[2].IsNegative);
    }

    [Fact]
    public void RightToLeft_ComposedWithDateAxis_MirrorsTheDateProportionalSpacingRatherThanRecomputingEvenly()
    {
        var values = new double[] { 1, 1, 1, 1 }; // flat values isolate the X-spacing behavior.
        // Jan 1, Jan 2, Jan 20, Jan 21 as day-offsets from Jan 1: 0, 1, 19, 20 (same as R88's fixture).
        var unevenDatePositions = new double[] { 0, 1, 19, 20 };

        var dateOnly = SparklineLayoutEngine.CalculateLineLayout(
            values, Cell, overrideMin: null, overrideMax: null, unevenDatePositions);
        var dateAndRtl = SparklineLayoutEngine.CalculateLineLayout(
            values, Cell, overrideMin: null, overrideMax: null, unevenDatePositions, rightToLeft: true);

        dateAndRtl.Segments.Should().HaveCount(3);
        // Each endpoint's X is independently mirrored within the cell -- rightToLeft flips the
        // horizontal fraction (1 - frac) before scaling, so it composes with the date-proportional
        // fraction the R88 fix already computed, rather than recomputing even by-index spacing.
        for (var i = 0; i < dateAndRtl.Segments.Count; i++)
        {
            var mirroredStart = Cell.Left + Cell.Right - dateOnly.Segments[i].Start.X;
            var mirroredEnd = Cell.Left + Cell.Right - dateOnly.Segments[i].End.X;
            dateAndRtl.Segments[i].Start.X.Should().BeApproximately(mirroredStart, 1e-9);
            dateAndRtl.Segments[i].End.X.Should().BeApproximately(mirroredEnd, 1e-9);
        }

        // Expected absolute values, per R88's own gap math (5, 90, 5 out of 100px) mirrored:
        // fractions 1, 0.95, 0.05, 0 -> X = 110, 105, 15, 10.
        dateAndRtl.Segments[0].Start.X.Should().BeApproximately(110, 1e-9);
        dateAndRtl.Segments[0].End.X.Should().BeApproximately(105, 1e-9);
        dateAndRtl.Segments[1].End.X.Should().BeApproximately(15, 1e-9);
        dateAndRtl.Segments[2].End.X.Should().BeApproximately(10, 1e-9);

        // The gap between the first two mirrored points (5px) must differ from what plain even
        // by-index RTL spacing would have produced (~33.33px), proving the date proportion survived
        // the mirror instead of being recomputed evenly.
        var evenSpacingGap = Cell.Width / 3;
        var actualFirstGap = Math.Abs(dateAndRtl.Segments[0].Start.X - dateAndRtl.Segments[0].End.X);
        Math.Abs(actualFirstGap - evenSpacingGap).Should().BeGreaterThan(1);
    }

    // No-regression sibling: RightToLeft=false (the default, and every pre-round-89 call site) must
    // produce byte-identical output to before this fix, for both line and column/win-loss layouts.
    [Fact]
    public void RightToLeftOff_IsByteIdenticalToPreRound89Output()
    {
        var lineValues = new double[] { 3, 7, 2, 9, 5, 1, 8 };
        var columnValues = new double[] { 5, -2, 0, 3 };

        var lineDefaultOverload = SparklineLayoutEngine.CalculateLineLayout(lineValues, Cell);
        var lineExplicitFalse = SparklineLayoutEngine.CalculateLineLayout(
            lineValues, Cell, overrideMin: null, overrideMax: null, datePositions: null, rightToLeft: false);
        lineExplicitFalse.Segments.Should().Equal(lineDefaultOverload.Segments);
        lineExplicitFalse.SinglePoint.Should().Be(lineDefaultOverload.SinglePoint);

        var columnDefaultOverload = SparklineLayoutEngine.CalculateColumnLayout(columnValues, Cell, winLoss: true);
        var columnExplicitFalse = SparklineLayoutEngine.CalculateColumnLayout(
            columnValues, Cell, winLoss: true, overrideMaxAbs: null, rightToLeft: false);
        columnExplicitFalse.Bars.Should().Equal(columnDefaultOverload.Bars);

        var pointsDefaultOverload = SparklineLayoutEngine.GetLinePoints(lineValues, Cell, overrideMin: null, overrideMax: null);
        var pointsExplicitFalse = SparklineLayoutEngine.GetLinePoints(lineValues, Cell, overrideMin: null, overrideMax: null, rightToLeft: false);
        pointsExplicitFalse.Should().Equal(pointsDefaultOverload);
    }
}
