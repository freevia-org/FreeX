using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Shapes;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Shapes;

/// <summary>
/// Unit tests for <see cref="ArrowheadGeometry"/>: scaling math, polygon shapes, and endpoint computation.
/// </summary>
public sealed class ArrowheadGeometryTests
{
    // ── ScaleArrowhead ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(DrawingArrowheadSize.Small,  DrawingArrowheadSize.Small,  3.0, 4.0)]
    [InlineData(DrawingArrowheadSize.Medium, DrawingArrowheadSize.Medium, 3.0, 3.5)]
    [InlineData(DrawingArrowheadSize.Large,  DrawingArrowheadSize.Large,  7.0, 10.0)]
    public void ScaleArrowhead_Triangle_ReturnsCorrectFactors(
        DrawingArrowheadSize w,
        DrawingArrowheadSize len,
        double expectedHalfWidthFactor,
        double expectedLengthFactor)
    {
        var arrowhead = new DrawingArrowhead(DrawingArrowheadType.Triangle, w, len);
        const double stroke = 2.0;

        var (halfWidth, length) = ArrowheadGeometry.ScaleArrowhead(arrowhead, stroke);

        halfWidth.Should().BeApproximately(stroke * expectedHalfWidthFactor / 2.0, 1e-9);
        length.Should().BeApproximately(stroke * expectedLengthFactor, 1e-9);
    }

    [Fact]
    public void ScaleArrowhead_MediumNonTriangle_RetainsEstablishedFactors()
    {
        var arrowhead = new DrawingArrowhead(
            DrawingArrowheadType.Arrow,
            DrawingArrowheadSize.Medium,
            DrawingArrowheadSize.Medium);

        var (halfWidth, length) = ArrowheadGeometry.ScaleArrowhead(arrowhead, strokeWidth: 2.0);

        halfWidth.Should().BeApproximately(5.0, 1e-9);
        length.Should().BeApproximately(14.0, 1e-9);
    }

    [Fact]
    public void ScaleArrowhead_ClampsZeroStrokeToOne()
    {
        var arrowhead = new DrawingArrowhead(DrawingArrowheadType.Arrow, DrawingArrowheadSize.Medium, DrawingArrowheadSize.Medium);
        // stroke=0 should clamp to 1
        var (halfWidth, length) = ArrowheadGeometry.ScaleArrowhead(arrowhead, strokeWidth: 0.0);
        halfWidth.Should().BeApproximately(5.0 / 2.0, 1e-9,  "uses clamp-to-1 stroke");
        length.Should().BeApproximately(7.0, 1e-9, "uses clamp-to-1 stroke");
    }

    // ── PolygonPoints — None / Oval return value ──────────────────────────────

    [Fact]
    public void PolygonPoints_NoneArrowhead_ReturnsEmpty()
    {
        var pts = ArrowheadGeometry.PolygonPoints(DrawingArrowhead.None, new LayoutPoint(0, 0), 0, 2.0);
        pts.Should().BeEmpty();
    }

    [Fact]
    public void PolygonPoints_OvalArrowhead_ReturnsEmpty()
    {
        // Oval is handled via OvalCenter; PolygonPoints returns empty.
        var oval = new DrawingArrowhead(DrawingArrowheadType.Oval);
        var pts = ArrowheadGeometry.PolygonPoints(oval, new LayoutPoint(0, 0), 0, 2.0);
        pts.Should().BeEmpty();
    }

    // ── PolygonPoints — Triangle ─────────────────────────────────────────────

    [Fact]
    public void PolygonPoints_Triangle_HasThreeVerticesAndTipAtOriginPointingRight()
    {
        var arrowhead = new DrawingArrowhead(DrawingArrowheadType.Triangle, DrawingArrowheadSize.Medium, DrawingArrowheadSize.Medium);
        var tip = new LayoutPoint(0, 0);
        const double dirRight = 0.0; // pointing right (+X)
        const double stroke = 2.0;

        var pts = ArrowheadGeometry.PolygonPoints(arrowhead, tip, dirRight, stroke);

        pts.Should().HaveCount(3);
        // First point is the tip
        pts[0].X.Should().BeApproximately(0, 1e-9);
        pts[0].Y.Should().BeApproximately(0, 1e-9);

        // The two base points should be to the LEFT of the tip (negative X) and symmetric in Y
        pts[1].X.Should().BeLessThan(0);
        pts[2].X.Should().BeLessThan(0);
        pts[1].Y.Should().BeApproximately(-pts[2].Y, 1e-9, "base points are symmetric about the line axis");
    }

    [Fact]
    public void PolygonPoints_Triangle_PointingDown()
    {
        var arrowhead = new DrawingArrowhead(DrawingArrowheadType.Triangle, DrawingArrowheadSize.Medium, DrawingArrowheadSize.Medium);
        var tip = new LayoutPoint(100, 100);
        const double dirDown = Math.PI / 2; // pointing down (+Y)
        const double stroke = 1.0;

        var pts = ArrowheadGeometry.PolygonPoints(arrowhead, tip, dirDown, stroke);

        pts.Should().HaveCount(3);
        // Tip is exactly at provided point
        pts[0].Should().BeEquivalentTo(tip);
        // Base points should be ABOVE tip (smaller Y) when direction is down
        pts[1].Y.Should().BeLessThan(tip.Y);
        pts[2].Y.Should().BeLessThan(tip.Y);
        // Base points symmetric about the vertical axis through tip
        pts[1].X.Should().BeApproximately(tip.X + (tip.X - pts[2].X), 1e-9,
            "base points are symmetric about the vertical axis");
    }

    // ── PolygonPoints — Arrow ────────────────────────────────────────────────

    [Fact]
    public void PolygonPoints_Arrow_HasFourVerticesWithIndentedBase()
    {
        var arrowhead = new DrawingArrowhead(DrawingArrowheadType.Arrow, DrawingArrowheadSize.Medium, DrawingArrowheadSize.Medium);
        var tip = new LayoutPoint(50, 50);
        const double dirRight = 0.0;
        const double stroke = 2.0;

        var pts = ArrowheadGeometry.PolygonPoints(arrowhead, tip, dirRight, stroke);

        // Arrow has 4 vertices: tip, upper-base, indent, lower-base
        pts.Should().HaveCount(4);
        pts[0].Should().BeEquivalentTo(tip);
        // Indent point (index 2) should be between the tip and the full base (negative X for right-pointing)
        pts[2].X.Should().BeInRange(pts[1].X, pts[0].X); // indent X is between base and tip
    }

    // ── PolygonPoints — Diamond ──────────────────────────────────────────────

    [Fact]
    public void PolygonPoints_Diamond_HasFourVerticesStraddlingTip()
    {
        var arrowhead = new DrawingArrowhead(DrawingArrowheadType.Diamond, DrawingArrowheadSize.Medium, DrawingArrowheadSize.Medium);
        var tip = new LayoutPoint(0, 0);
        const double dirRight = 0.0;
        const double stroke = 2.0;

        var pts = ArrowheadGeometry.PolygonPoints(arrowhead, tip, dirRight, stroke);

        pts.Should().HaveCount(4);
        // For a rightward diamond, front point (index 0) is to the right of origin, back (index 2) to the left
        pts[0].X.Should().BeGreaterThan(0, "front point extends forward");
        pts[2].X.Should().BeLessThan(0, "back point extends backward");
        // Side points (1 & 3) are at origin X, displaced in Y
        pts[1].X.Should().BeApproximately(0, 1e-9);
        pts[3].X.Should().BeApproximately(0, 1e-9);
    }

    // ── OvalCenter ───────────────────────────────────────────────────────────

    [Fact]
    public void OvalCenter_PointingRight_CenterBehindTip()
    {
        var arrowhead = new DrawingArrowhead(DrawingArrowheadType.Oval, DrawingArrowheadSize.Medium, DrawingArrowheadSize.Medium);
        var tip = new LayoutPoint(100, 50);
        const double dirRight = 0.0;
        const double stroke = 2.0;

        var (center, radius) = ArrowheadGeometry.OvalCenter(arrowhead, tip, dirRight, stroke);

        radius.Should().BeGreaterThan(0);
        // Center should be to the LEFT of the tip (negative X offset) for a rightward arrow
        center.X.Should().BeApproximately(tip.X - radius, 1e-9);
        center.Y.Should().BeApproximately(tip.Y, 1e-9);
    }

    // ── LineEndpoints ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(DrawingShapeKind.Line,            false, false, 0, 0, 200, 100)]
    [InlineData(DrawingShapeKind.ElbowConnector,  false, false, 0, 0, 200, 100)]
    public void LineEndpoints_NoFlip_ReturnsBoundingCorners(
        DrawingShapeKind kind,
        bool flipH,
        bool flipV,
        double expStartX,
        double expStartY,
        double expEndX,
        double expEndY)
    {
        var (start, end, dir) = ArrowheadGeometry.LineEndpoints(0, 0, 200, 100, flipH, flipV, kind);

        start.X.Should().BeApproximately(expStartX, 1e-9);
        start.Y.Should().BeApproximately(expStartY, 1e-9);
        end.X.Should().BeApproximately(expEndX, 1e-9);
        end.Y.Should().BeApproximately(expEndY, 1e-9);

        // Direction should be arctan(100/200) ≈ 0.4636 rad
        var expected = Math.Atan2(100, 200);
        dir.Should().BeApproximately(expected, 1e-9);
    }

    [Fact]
    public void LineEndpoints_FlipHorizontal_MirrorsStartEnd()
    {
        var (noFlipStart, noFlipEnd, _) = ArrowheadGeometry.LineEndpoints(0, 0, 200, 100, flipHorizontal: false, flipVertical: false, DrawingShapeKind.Line);
        var (flipStart, flipEnd, _)     = ArrowheadGeometry.LineEndpoints(0, 0, 200, 100, flipHorizontal: true,  flipVertical: false, DrawingShapeKind.Line);

        // FlipH should mirror X about the center (100)
        flipStart.X.Should().BeApproximately(200 - noFlipStart.X, 1e-9);
        flipEnd.X.Should().BeApproximately(200 - noFlipEnd.X, 1e-9);
        flipStart.Y.Should().BeApproximately(noFlipStart.Y, 1e-9);
        flipEnd.Y.Should().BeApproximately(noFlipEnd.Y, 1e-9);
    }

    [Fact]
    public void LineEndpoints_FlipVertical_MirrorsStartEnd()
    {
        var (noFlipStart, noFlipEnd, _) = ArrowheadGeometry.LineEndpoints(0, 0, 200, 100, flipHorizontal: false, flipVertical: false, DrawingShapeKind.Line);
        var (flipStart, flipEnd, _)     = ArrowheadGeometry.LineEndpoints(0, 0, 200, 100, flipHorizontal: false, flipVertical: true,  DrawingShapeKind.Line);

        flipStart.X.Should().BeApproximately(noFlipStart.X, 1e-9);
        flipEnd.X.Should().BeApproximately(noFlipEnd.X, 1e-9);
        flipStart.Y.Should().BeApproximately(100 - noFlipStart.Y, 1e-9);
        flipEnd.Y.Should().BeApproximately(100 - noFlipEnd.Y, 1e-9);
    }

    [Fact]
    public void LineEndpoints_CurvedConnector_ReturnsGeometryEndpoints()
    {
        var (start, end, _) = ArrowheadGeometry.LineEndpoints(0, 0, 200, 100, flipHorizontal: false, flipVertical: false, DrawingShapeKind.CurvedConnector);

        start.Should().Be(new LayoutPoint(0, 0));
        end.Should().Be(new LayoutPoint(200, 100));
    }

    [Fact]
    public void LineEndpoints_DirectionRadians_PointsFromStartToEnd()
    {
        // Horizontal rightward line
        var (start, end, dir) = ArrowheadGeometry.LineEndpoints(0, 0, 100, 0, false, false, DrawingShapeKind.Line);
        dir.Should().BeApproximately(0.0, 1e-9, "horizontal rightward line has direction=0");

        // Horizontal leftward line (flipH)
        var (_, _, dirFlipped) = ArrowheadGeometry.LineEndpoints(0, 0, 100, 0, flipHorizontal: true, flipVertical: false, DrawingShapeKind.Line);
        dirFlipped.Should().BeApproximately(Math.PI, 1e-9, "leftward line has direction=π");
    }
}
