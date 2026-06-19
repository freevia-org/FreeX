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
    [InlineData(ConditionalIconGlyphKind.Arrow, 2, 3,
        "Polygon Icon/Outline [8,0 8,13 2,13 8,16 14,13 8,13]")]
    [InlineData(ConditionalIconGlyphKind.Arrow, 1, 3,
        "Polygon Icon/Outline [0,8 13,8 13,2 16,8 13,14 13,8]")]
    [InlineData(ConditionalIconGlyphKind.Arrow, 0, 3,
        "Polygon Icon/Outline [8,16 8,3 2,3 8,0 14,3 8,3]")]
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

    [Fact]
    public void Build_Quarter_EmitsBackingDiscPieAndRing()
    {
        var ops = ConditionalIconGlyphGeometry.Build(ConditionalIconGlyphKind.Quarter, 0, 4, 0, 0, Size, Size);

        ops.Should().HaveCount(3);
        Describe(ops[0]).Should().Be("Ellipse White/Outline c=8,8 r=8,8");
        // index 0 of 4 → quarter sweep → end point at 3-o'clock, small arc.
        Describe(ops[1]).Should().Be("Pie Icon/None c=8,8 r=8,8 [8,0 16,8] largeArc=False");
        Describe(ops[2]).Should().Be("Ellipse None/Outline c=8,8 r=8,8");
    }

    [Fact]
    public void Build_Quarter_FullSweepUsesLargeArc()
    {
        var ops = ConditionalIconGlyphGeometry.Build(ConditionalIconGlyphKind.Quarter, 3, 4, 0, 0, Size, Size);
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

    [Fact]
    public void Build_Rating_IsTenPointStar()
    {
        var ops = ConditionalIconGlyphGeometry.Build(ConditionalIconGlyphKind.Rating, 0, 5, 0, 0, Size, Size);

        ops.Should().HaveCount(1);
        ops[0].Kind.Should().Be(CfGlyphPrimitiveKind.Polygon);
        ops[0].Fill.Should().Be(CfGlyphFill.Icon);
        ops[0].Stroke.Should().Be(CfGlyphStroke.Outline);
        ops[0].Points.Should().HaveCount(10);
        // First point sits at the top (12-o'clock), outer radius = 8.
        ops[0].Points[0].X.Should().BeApproximately(8, 1e-9);
        ops[0].Points[0].Y.Should().BeApproximately(0, 1e-9);
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
