using System.Globalization;
using System.Text;

using FluentAssertions;

using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.ConditionalFormatting;

namespace FreeX.App.Presentation.Tests.ConditionalFormatting;

/// <summary>
/// Point-level golden tests for the shared icon-glyph geometry emitter. These pin the exact
/// primitive ops (kinds, fills, strokes and coordinates) so the desktop renderer and the
/// cross-platform port — which both translate these ops — keep drawing identical shapes.
/// </summary>
public sealed class ConditionalIconGlyphGeometryTests
{
    // A 16×16 rect at the origin keeps the golden strings readable.
    private const double Size = 16d;

    [Theory]
    // Arrow direction: index 0 = worst = DOWN; index count-1 = best = UP; middle = RIGHT (3-bucket).
    [InlineData(ConditionalIconGlyphKind.Arrow, 0, 3,
        "Polygon Icon/Outline [8,16 16,8.8 11.2,8.8 11.2,0 4.8,0 4.8,8.8 0,8.8]")]
    [InlineData(ConditionalIconGlyphKind.Arrow, 1, 3,
        "Polygon Icon/Outline [0,4.8 8.8,4.8 8.8,0 16,8 8.8,16 8.8,11.2 0,11.2]")]
    [InlineData(ConditionalIconGlyphKind.Arrow, 2, 3,
        "Polygon Icon/Outline [8,0 16,7.2 11.2,7.2 11.2,16 4.8,16 4.8,7.2 0,7.2]")]
    [InlineData(ConditionalIconGlyphKind.TrafficLight, 0, 3,
        "Ellipse Icon/Outline c=8,8 r=8,8")]
    [InlineData(ConditionalIconGlyphKind.Box, 0, 3,
        "Box Icon/Outline x=2.24,2.24 16,16->11.52,11.52")]
    [InlineData(ConditionalIconGlyphKind.Box, 2, 3,
        "Box Icon/Outline x=0,0 16,16->16,16")]
    public void Build_SingleOpGlyphs_MatchGolden(ConditionalIconGlyphKind kind, int index, int count, string expected)
    {
        var ops = ConditionalIconGlyphGeometry.Build(kind, index, count, 0, 0, Size, Size);
        ops.Should().HaveCount(1);
        Describe(ops[0]).Should().Be(expected);
    }

    [Fact]
    public void Build_Flag_EmitsOpenPoleThenFilledBanner()
    {
        var ops = ConditionalIconGlyphGeometry.Build(ConditionalIconGlyphKind.Flag, 0, 3, 0, 0, Size, Size);

        ops.Should().HaveCount(2);
        Describe(ops[0]).Should().Be("Polyline None/Outline [4,16 4,0]");
        Describe(ops[1]).Should().Be("Polygon Icon/Outline [4,1.28 16,2.88 13.12,7.36 4,6.08]");
    }

    // R54-render-cf-icon-databar-4-1: "5 Quarters" (IconSetStyle "5Quarters") is the only real Excel
    // Quarters gallery preset (see ConditionalFormatPresetGalleryPlanner.cs / ConditionalFormatPresetFactory.cs)
    // -- its per-cell IconCount is always 5 (ViewportConditionalFormatEvaluator.Thresholds.cs'
    // GetIconSetCount takes the leading style digit, clamped 3..5). These golden tests previously
    // pinned an unrealistic iconCount=4, which happened to mask the bucket-index off-by-one bug in
    // QuarterGlyph's sweep-fraction formula; they now exercise the real count=5 so the fractions
    // (0, 1/4, 2/4, 3/4, 4/4 = 0%, 25%, 50%, 75%, 100%) match what Excel actually renders, with the
    // worst bucket (index 0) a fully EMPTY circle (zero sweep), not a 20%-filled pie slice.
    [Fact]
    public void Build_Quarter_Index0_IsFullyEmpty()
    {
        var ops = ConditionalIconGlyphGeometry.Build(ConditionalIconGlyphKind.Quarter, 0, 5, 0, 0, Size, Size);

        ops.Should().HaveCount(3);
        Describe(ops[0]).Should().Be("Ellipse White/Outline c=8,8 r=8,8");
        // index 0 of 5 (worst bucket) → 0/4 = 0% sweep → fully empty pie (start point == end point).
        Describe(ops[1]).Should().Be("Pie Icon/None c=8,8 r=8,8 [8,0 8,0] largeArc=False");
        Describe(ops[2]).Should().Be("Ellipse None/Outline c=8,8 r=8,8");
    }

    [Fact]
    public void Build_Quarter_Index1_IsQuarterSweep()
    {
        var ops = ConditionalIconGlyphGeometry.Build(ConditionalIconGlyphKind.Quarter, 1, 5, 0, 0, Size, Size);

        // index 1 of 5 → 1/4 = 25% sweep → end point at 3-o'clock, small arc.
        Describe(ops[1]).Should().Be("Pie Icon/None c=8,8 r=8,8 [8,0 16,8] largeArc=False");
    }

    [Fact]
    public void Build_Quarter_LastIndex_FullSweepUsesLargeArc()
    {
        var ops = ConditionalIconGlyphGeometry.Build(ConditionalIconGlyphKind.Quarter, 4, 5, 0, 0, Size, Size);
        Describe(ops[1]).Should().Contain("largeArc=True");
    }

    [Fact]
    public void Build_SignDanger_IsCircleWithWhiteCross()
    {
        var ops = ConditionalIconGlyphGeometry.Build(ConditionalIconGlyphKind.Sign, 0, 3, 0, 0, Size, Size);

        ops.Should().HaveCount(3);
        Describe(ops[0]).Should().Be("Ellipse Icon/Outline c=8,8 r=8,8");
        Describe(ops[1]).Should().Be("Line None/WhiteThin [4.48,4.48 11.52,11.52]");
        Describe(ops[2]).Should().Be("Line None/WhiteThin [11.52,4.48 4.48,11.52]");
    }

    [Fact]
    public void Build_SignWarning_IsTriangleStemAndDot()
    {
        var ops = ConditionalIconGlyphGeometry.Build(ConditionalIconGlyphKind.Sign, 1, 3, 0, 0, Size, Size);

        ops.Should().HaveCount(3);
        Describe(ops[0]).Should().Be("Polygon Icon/Outline [8,0 16,16 0,16]");
        Describe(ops[1]).Should().Be("Line None/WhiteThin [8,4.8 8,9.92]");
        Describe(ops[2]).Should().Be("Ellipse White/None c=8,12 r=0.9,0.9");
    }

    [Fact]
    public void Build_SignGood_IsCircleWithWhiteCheck()
    {
        var ops = ConditionalIconGlyphGeometry.Build(ConditionalIconGlyphKind.Sign, 2, 3, 0, 0, Size, Size);

        ops.Should().HaveCount(3);
        Describe(ops[0]).Should().Be("Ellipse Icon/Outline c=8,8 r=8,8");
        Describe(ops[1]).Should().Be("Line None/WhiteMedium [4.48,8.96 7.04,11.52]");
        Describe(ops[2]).Should().Be("Line None/WhiteMedium [7.04,11.52 12.16,4.8]");
    }

    [Fact]
    public void Build_SymbolDanger_IsDiamondWithWhiteCross()
    {
        var ops = ConditionalIconGlyphGeometry.Build(ConditionalIconGlyphKind.Symbol, 0, 3, 0, 0, Size, Size);

        ops.Should().HaveCount(3);
        Describe(ops[0]).Should().Be("Polygon Icon/Outline [8,0 16,8 8,16 0,8]");
        Describe(ops[1]).Should().Be("Line None/WhiteThin [5.12,5.12 10.88,10.88]");
        Describe(ops[2]).Should().Be("Line None/WhiteThin [10.88,5.12 5.12,10.88]");
    }

    // ── Star (ConditionalIconGlyphKind.Star) ──────────────────────────────────

    [Fact]
    public void Build_Star_EmitsStarFillFractionWithCorrectPoints()
    {
        // Star kind at index 2 of 5 → fill fraction = 2/4 = 0.5.
        var ops = ConditionalIconGlyphGeometry.Build(ConditionalIconGlyphKind.Star, 2, 5, 0, 0, Size, Size);

        ops.Should().HaveCount(1);
        ops[0].Kind.Should().Be(CfGlyphPrimitiveKind.StarFillFraction);
        ops[0].Fill.Should().Be(CfGlyphFill.Icon);
        ops[0].Stroke.Should().Be(CfGlyphStroke.Outline);
        ops[0].Points.Should().HaveCount(10);
        // RadiusX carries the fill fraction: 2 / (5-1) = 0.5.
        ops[0].RadiusX.Should().BeApproximately(0.5, 1e-9);
        // First point sits at the top (12-o'clock), outer radius = 8.
        ops[0].Points[0].X.Should().BeApproximately(8, 1e-9);
        ops[0].Points[0].Y.Should().BeApproximately(0, 1e-9);
    }

    [Fact]
    public void Build_Star_Index0_IsEmptyStar()
    {
        var ops = ConditionalIconGlyphGeometry.Build(ConditionalIconGlyphKind.Star, 0, 5, 0, 0, Size, Size);
        ops[0].RadiusX.Should().BeApproximately(0d, 1e-9, "index 0 = worst = empty star");
    }

    [Fact]
    public void Build_Star_LastIndex_IsFullStar()
    {
        var ops = ConditionalIconGlyphGeometry.Build(ConditionalIconGlyphKind.Star, 4, 5, 0, 0, Size, Size);
        ops[0].RadiusX.Should().BeApproximately(1d, 1e-9, "index count-1 = best = fully filled star");
    }

    // ── Rating bars (ConditionalIconGlyphKind.Rating) ─────────────────────────

    [Fact]
    public void Build_Rating_EmitsBarBoxOps_CountMatchesIconCount()
    {
        // 5Rating icon set: 5 bars.
        var ops = ConditionalIconGlyphGeometry.Build(ConditionalIconGlyphKind.Rating, 2, 5, 0, 0, Size, Size);

        ops.Should().HaveCount(5, "one box per bar column");
        foreach (var op in ops)
            op.Kind.Should().Be(CfGlyphPrimitiveKind.Box);
    }

    [Fact]
    public void Build_Rating_Index0_AllBarsEmpty()
    {
        // Worst bucket: index 0 → 0 bars filled (all outline-only).
        var ops = ConditionalIconGlyphGeometry.Build(ConditionalIconGlyphKind.Rating, 0, 5, 0, 0, Size, Size);

        // index 0 fills bars up to and including index 0 → bar 0 only is filled.
        // (Each bar i is filled when i <= iconIndex.)
        ops[0].Fill.Should().Be(CfGlyphFill.Icon, "bar 0 is filled at iconIndex 0");
        ops[1].Fill.Should().Be(CfGlyphFill.None, "bar 1 is empty at iconIndex 0");
        ops[4].Fill.Should().Be(CfGlyphFill.None, "bar 4 is empty at iconIndex 0");
    }

    [Fact]
    public void Build_Rating_LastIndex_AllBarsFilled()
    {
        // Best bucket: index 4 → all 5 bars filled.
        var ops = ConditionalIconGlyphGeometry.Build(ConditionalIconGlyphKind.Rating, 4, 5, 0, 0, Size, Size);

        foreach (var op in ops)
            op.Fill.Should().Be(CfGlyphFill.Icon, "all bars filled at max index");
    }

    [Fact]
    public void Build_Rating_BarsAreBottomAlignedAndIncreaseInHeight()
    {
        // Bar heights should increase left to right (bar 0 shortest, bar count-1 tallest).
        var ops = ConditionalIconGlyphGeometry.Build(ConditionalIconGlyphKind.Rating, 4, 5, 0, 0, Size, Size);

        for (var i = 1; i < ops.Count; i++)
            ops[i].Rect.Height.Should().BeGreaterThan(ops[i - 1].Rect.Height,
                $"bar {i} should be taller than bar {i - 1}");

        // The tallest bar should fill the full height.
        ops[^1].Rect.Height.Should().BeApproximately(Size, 1e-9, "last bar is full height");
    }

    // ── Arrow direction ───────────────────────────────────────────────────────

    [Theory]
    // 3-arrow: index 0 = worst = DOWN (tip points to bottom); index 2 = best = UP (tip at top).
    [InlineData(0, 3, 8d, 16d)]   // DOWN: tip Y at bottom (16)
    [InlineData(2, 3, 8d,  0d)]   // UP: tip Y at top (0)
    public void Build_Arrow_3Count_IndexZeroIsDown_LastIsUp(int index, int count, double expectedTipX, double expectedTipY)
    {
        var ops = ConditionalIconGlyphGeometry.Build(ConditionalIconGlyphKind.Arrow, index, count, 0, 0, Size, Size);
        ops.Should().HaveCount(1);
        // The tip is the first point of the arrow polygon.
        ops[0].Points[0].X.Should().BeApproximately(expectedTipX, 1e-9);
        ops[0].Points[0].Y.Should().BeApproximately(expectedTipY, 1e-9);
    }

    [Theory]
    // 5-arrow: 0=DOWN, 1=DOWN-DIAG, 2=RIGHT, 3=UP-DIAG, 4=UP.
    // Check: the point with the extreme x/y coordinate (tip) is in the expected direction.
    [InlineData(0, 5, false, false, true, false)]   // DOWN: y-max point is at bottom
    [InlineData(2, 5, true, false, false, false)]   // RIGHT: x-max point is at right edge
    [InlineData(4, 5, false, true, false, false)]   // UP: y-min point is at top edge (y=0)
    public void Build_Arrow_5Count_DirectionMapping(int index, int count,
        bool hasPointAtRightEdge, bool hasPointAtTopEdge, bool hasPointAtBottomEdge, bool unused)
    {
        var ops = ConditionalIconGlyphGeometry.Build(ConditionalIconGlyphKind.Arrow, index, count, 0, 0, Size, Size);
        ops.Should().HaveCount(1);
        var points = ops[0].Points;
        if (hasPointAtRightEdge)
            points.Any(p => Math.Abs(p.X - Size) < 1e-9).Should().BeTrue("right arrow should have a vertex at x=Size");
        if (hasPointAtTopEdge)
            points.Any(p => Math.Abs(p.Y) < 1e-9).Should().BeTrue("up arrow should have a vertex at y=0");
        if (hasPointAtBottomEdge)
            points.Any(p => Math.Abs(p.Y - Size) < 1e-9).Should().BeTrue("down arrow should have a vertex at y=Size");
        _ = unused;
    }

    [Fact]
    public void Build_OffsetRect_TranslatesAllCoordinates()
    {
        var ops = ConditionalIconGlyphGeometry.Build(ConditionalIconGlyphKind.TrafficLight, 0, 3, 10, 20, Size, Size);
        Describe(ops[0]).Should().Be("Ellipse Icon/Outline c=18,28 r=8,8");
    }

    private static string Describe(CfGlyphOp op)
    {
        var sb = new StringBuilder();
        sb.Append(op.Kind).Append(' ').Append(op.Fill).Append('/').Append(op.Stroke);
        switch (op.Kind)
        {
            case CfGlyphPrimitiveKind.Ellipse:
                sb.Append(" c=").Append(P(op.Center)).Append(" r=").Append(N(op.RadiusX)).Append(',').Append(N(op.RadiusY));
                break;
            case CfGlyphPrimitiveKind.Box:
                sb.Append(" x=").Append(N(op.Rect.X)).Append(',').Append(N(op.Rect.Y))
                  .Append(' ').Append(N(Size)).Append(',').Append(N(Size))
                  .Append("->").Append(N(op.Rect.Width)).Append(',').Append(N(op.Rect.Height));
                break;
            case CfGlyphPrimitiveKind.Pie:
                sb.Append(" c=").Append(P(op.Center)).Append(" r=").Append(N(op.RadiusX)).Append(',').Append(N(op.RadiusY))
                  .Append(' ').Append(Points(op.Points)).Append(" largeArc=").Append(op.LargeArc);
                break;
            case CfGlyphPrimitiveKind.StarFillFraction:
                sb.Append(" fraction=").Append(N(op.RadiusX)).Append(' ').Append(Points(op.Points));
                break;
            default:
                sb.Append(' ').Append(Points(op.Points));
                break;
        }

        return sb.ToString();
    }

    private static string Points(IReadOnlyList<LayoutPoint> points) =>
        "[" + string.Join(" ", points.Select(P)) + "]";

    private static string P(LayoutPoint p) => N(p.X) + "," + N(p.Y);

    private static string N(double value) =>
        Math.Round(value, 6).ToString(CultureInfo.InvariantCulture);
}
