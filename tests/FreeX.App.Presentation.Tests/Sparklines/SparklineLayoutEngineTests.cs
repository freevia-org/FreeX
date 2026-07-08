using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Sparklines;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Presentation.Tests.Sparklines;

public sealed class SparklineLayoutEngineTests
{
    private static readonly LayoutRect Cell = new(10, 20, 100, 40);

    [Fact]
    public void Line_EmptyValues_ProducesNothing()
    {
        var layout = SparklineLayoutEngine.CalculateLineLayout([], Cell);

        layout.SinglePoint.Should().BeNull();
        layout.Segments.Should().BeEmpty();
    }

    [Fact]
    public void Line_DegenerateRect_ProducesNothing()
    {
        var layout = SparklineLayoutEngine.CalculateLineLayout([1, 2, 3], new LayoutRect(0, 0, 0, 40));

        layout.SinglePoint.Should().BeNull();
        layout.Segments.Should().BeEmpty();
    }

    [Fact]
    public void Line_SingleValue_CentersPoint()
    {
        var layout = SparklineLayoutEngine.CalculateLineLayout([5], Cell);

        layout.Segments.Should().BeEmpty();
        layout.SinglePoint.Should().NotBeNull();
        layout.SinglePoint!.Value.X.Should().Be(60); // 10 + 100/2
        layout.SinglePoint!.Value.Y.Should().Be(40); // 20 + 40/2
    }

    [Fact]
    public void Line_TwoValues_MapsEndpointsToCorners()
    {
        // min=0 at index 0 -> bottom; max=10 at index 1 -> top.
        var layout = SparklineLayoutEngine.CalculateLineLayout([0, 10], Cell);

        layout.SinglePoint.Should().BeNull();
        layout.Segments.Should().ContainSingle();
        var seg = layout.Segments[0];
        seg.Start.X.Should().Be(10); // left
        seg.Start.Y.Should().Be(60); // bottom (rect.Bottom = 20+40)
        seg.End.X.Should().Be(110); // right
        seg.End.Y.Should().Be(20); // top
    }

    [Fact]
    public void Line_AllEqualValues_DrawsFlatLineAtBottom()
    {
        // span collapses to 1; every value maps to rect.Bottom.
        var layout = SparklineLayoutEngine.CalculateLineLayout([7, 7, 7], Cell);

        layout.Segments.Should().HaveCount(2);
        layout.Segments.Should().OnlyContain(s => s.Start.Y == 60 && s.End.Y == 60);
        layout.Segments[0].Start.X.Should().Be(10);
        layout.Segments[1].End.X.Should().Be(110);
    }

    [Fact]
    public void Line_Negatives_NormalizeAcrossSpan()
    {
        // values -5, 0, 5 -> min=-5, max=5, span=10. midpoint maps to vertical center.
        var layout = SparklineLayoutEngine.CalculateLineLayout([-5, 0, 5], Cell);

        layout.Segments.Should().HaveCount(2);
        layout.Segments[0].Start.Y.Should().Be(60); // -5 -> bottom
        layout.Segments[0].End.Y.Should().Be(40); // 0 -> center
        layout.Segments[1].End.Y.Should().Be(20); // 5 -> top
    }

    [Fact]
    public void Line_NonFiniteValue_BreaksLineIntoSeparateSegments()
    {
        var layout = SparklineLayoutEngine.CalculateLineLayout([1, double.NaN, 3, 4], Cell);

        // index0 then gap, then 3-4 connected -> only one segment (between idx2 and idx3).
        layout.Segments.Should().ContainSingle();
        layout.SinglePoint.Should().BeNull();
    }

    [Fact]
    public void Line_SingleFiniteAmongGaps_ReportsSinglePointAtThatColumn()
    {
        var layout = SparklineLayoutEngine.CalculateLineLayout([double.NaN, 5, double.NaN], Cell);

        layout.Segments.Should().BeEmpty();
        layout.SinglePoint.Should().NotBeNull();
        // index 1 of 3 -> x = left + width*1/2 = 60.
        layout.SinglePoint!.Value.X.Should().Be(60);
    }

    [Fact]
    public void Column_EmptyOrDegenerate_ProducesNoBars()
    {
        SparklineLayoutEngine.CalculateColumnLayout([], Cell, winLoss: false).Bars.Should().BeEmpty();
        SparklineLayoutEngine.CalculateColumnLayout([1, 2], new LayoutRect(0, 0, 0, 0), winLoss: false).Bars.Should().BeEmpty();
    }

    [Fact]
    public void Column_PositiveValue_GrowsUpwardFromAxis()
    {
        var layout = SparklineLayoutEngine.CalculateColumnLayout([10], Cell, winLoss: false);

        layout.Bars.Should().ContainSingle();
        var bar = layout.Bars[0];
        bar.IsNegative.Should().BeFalse();
        // single value, slot=100, barWidth=min(100,max(1,65))=65; all-positive data puts the zero
        // baseline at the cell bottom, so the max value fills the full cell height: 10/10 * 40 = 40.
        bar.Rect.Width.Should().Be(65);
        bar.Rect.Height.Should().Be(40);
        var axis = 60d; // rect.Bottom (20 + 40)
        bar.Rect.Top.Should().Be(axis - 40); // grows up from the bottom, filling the whole cell
        bar.Rect.Left.Should().Be(10 + (100 - 65) / 2.0);
    }

    [Fact]
    public void Column_NegativeValue_GrowsDownwardFromAxis()
    {
        var layout = SparklineLayoutEngine.CalculateColumnLayout([-10], Cell, winLoss: false);

        var bar = layout.Bars[0];
        bar.IsNegative.Should().BeTrue();
        // all-negative data puts the zero baseline at the cell top, so the largest-magnitude value
        // fills the full cell height: 10/10 * 40 = 40.
        var axis = 20d; // rect.Top
        bar.Rect.Top.Should().Be(axis); // grows down from the top, filling the whole cell
        bar.Rect.Height.Should().Be(40);
    }

    [Fact]
    public void Column_ZeroAndNonFiniteValues_AreSkipped()
    {
        var layout = SparklineLayoutEngine.CalculateColumnLayout([0, double.NaN, 5], Cell, winLoss: false);

        layout.Bars.Should().ContainSingle();
    }

    [Fact]
    public void Column_ScalesBarsByMaxMagnitude()
    {
        var layout = SparklineLayoutEngine.CalculateColumnLayout([5, 10], Cell, winLoss: false);

        layout.Bars.Should().HaveCount(2);
        // all-positive data: maxAbs=10, full cell height (40) available; first bar height =
        // 5/10 * 40 = 20; second (the max) fills the full 40.
        layout.Bars[0].Rect.Height.Should().Be(20);
        layout.Bars[1].Rect.Height.Should().Be(40);
    }

    [Fact]
    public void WinLoss_UsesFixedHeightKeyedOnSign()
    {
        var layout = SparklineLayoutEngine.CalculateColumnLayout([3, -8, 0, 1], Cell, winLoss: true);

        // zero skipped -> 3 bars, all half-height (20).
        layout.Bars.Should().HaveCount(3);
        layout.Bars.Should().OnlyContain(b => b.Rect.Height == 20);
        layout.Bars[0].IsNegative.Should().BeFalse(); // +3
        layout.Bars[1].IsNegative.Should().BeTrue(); // -8
        layout.Bars[2].IsNegative.Should().BeFalse(); // +1
    }

    [Fact]
    public void WinLoss_IgnoresMagnitudeAcrossWildlyDifferentValues()
    {
        var layout = SparklineLayoutEngine.CalculateColumnLayout([1000, -1], Cell, winLoss: true);

        layout.Bars.Should().HaveCount(2);
        layout.Bars[0].Rect.Height.Should().Be(layout.Bars[1].Rect.Height);
    }

    [Fact]
    public void KindOverload_WinLoss_MatchesBooleanOverload()
    {
        var byKind = SparklineLayoutEngine.CalculateColumnLayout([2, -3], Cell, SparklineKind.WinLoss);
        var byBool = SparklineLayoutEngine.CalculateColumnLayout([2, -3], Cell, winLoss: true);

        byKind.Bars.Should().Equal(byBool.Bars);
    }

    [Fact]
    public void KindOverload_Column_MatchesBooleanOverload()
    {
        var byKind = SparklineLayoutEngine.CalculateColumnLayout([2, -3], Cell, SparklineKind.Column);
        var byBool = SparklineLayoutEngine.CalculateColumnLayout([2, -3], Cell, winLoss: false);

        byKind.Bars.Should().Equal(byBool.Bars);
    }

    [Fact]
    public void Column_AllEqualValues_StillProducesVisibleBars()
    {
        var layout = SparklineLayoutEngine.CalculateColumnLayout([4, 4, 4], Cell, winLoss: false);

        // all-positive data: maxAbs=4, full cell height (40) available; each height = 4/4 * 40 = 40.
        layout.Bars.Should().HaveCount(3);
        layout.Bars.Should().OnlyContain(b => b.Rect.Height == 40);
    }

    [Fact]
    public void VisitLineLayout_StreamsSameGeometryAsCalculateLineLayout()
    {
        var collector = new LineCollector();
        SparklineLayoutEngine.VisitLineLayout([0, 5, 10], Cell, ref collector);

        var expected = SparklineLayoutEngine.CalculateLineLayout([0, 5, 10], Cell);

        collector.SinglePoint.Should().Be(expected.SinglePoint);
        collector.Segments.Should().Equal(expected.Segments);
    }

    [Fact]
    public void VisitLineLayout_SingleValue_StreamsSinglePoint()
    {
        var collector = new LineCollector();
        SparklineLayoutEngine.VisitLineLayout([42], Cell, ref collector);

        collector.Segments.Should().BeEmpty();
        collector.SinglePoint.Should().Be(new LayoutPoint(60, 40));
    }

    [Fact]
    public void VisitColumnLayout_StreamsSameGeometryAsCalculateColumnLayout()
    {
        var collector = new ColumnCollector();
        SparklineLayoutEngine.VisitColumnLayout([2, -4, 0, 6], Cell, winLoss: false, ref collector);

        var expected = SparklineLayoutEngine.CalculateColumnLayout([2, -4, 0, 6], Cell, winLoss: false);

        collector.Bars.Should().Equal(expected.Bars);
    }

    private struct LineCollector : ISparklineLineLayoutConsumer
    {
        public LayoutPoint? SinglePoint;
        public readonly List<SparklineSegment> Segments = [];

        public LineCollector()
        {
        }

        public void AcceptSinglePoint(LayoutPoint point) => SinglePoint = point;

        public readonly void AcceptSegment(LayoutPoint start, LayoutPoint end) =>
            Segments.Add(new SparklineSegment(start, end));
    }

    private struct ColumnCollector : ISparklineColumnLayoutConsumer
    {
        public readonly List<SparklineColumnBar> Bars = [];

        public ColumnCollector()
        {
        }

        public readonly void AcceptBar(LayoutRect rect, bool isNegative) =>
            Bars.Add(new SparklineColumnBar(rect, isNegative));
    }
}
