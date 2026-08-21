using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Shapes;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Presentation.Tests.Shapes;

public sealed class ShapeGeometryBuilderTests
{
    private static readonly LayoutRect Bounds = new(100, 200, 80, 60);

    private static IEnumerable<LayoutPoint> AllPoints(ShapeContour contour)
    {
        yield return contour.Start;
        foreach (var segment in contour.Segments)
            yield return segment.End;
    }

    [Fact]
    public void ShapeGeometryBuilder_ComesFromSharedDrawingAssembly()
    {
        typeof(ShapeGeometryBuilder).Assembly.FullName.Should().Be(typeof(LayoutRect).Assembly.FullName);
        typeof(ShapeGeometry).Assembly.FullName.Should().Be(typeof(LayoutPoint).Assembly.FullName);
        typeof(ShapeGeometryBuilder).Namespace.Should().Be("Free.Shared.Drawing");
        typeof(ShapeGeometry).Namespace.Should().Be("Free.Shared.Drawing");
    }

    [Fact]
    public void PresentationShapeGeometrySources_RemainNeutralized()
    {
        var sharedRoot = TestWorkspaceFileLocator.FindDirectoryFromBaseDirectory("shared", "Free.Shared.Drawing");
        var shapesRoot = TestWorkspaceFileLocator.FindDirectoryFromBaseDirectory("src", "FreeX.App.Presentation", "Shapes");

        foreach (var sharedFile in new[]
        {
            "Geometry.cs",
            "DrawingShapeKind.cs",
            "DrawingShapeKindSupport.cs",
            "ShapeGeometry.cs",
            "ShapeGeometryBuilder.cs"
        })
        {
            File.Exists(Path.Combine(sharedRoot, sharedFile))
                .Should()
                .BeTrue($"{sharedFile} should remain owned by Free.Shared.Drawing");
        }

        Directory.EnumerateFiles(shapesRoot, "ShapeGeometry*.cs", SearchOption.TopDirectoryOnly)
            .Should()
            .BeEmpty("shape geometry files should not reappear under FreeX.App.Presentation.Shapes");

        var presentationShapeSources = Directory
            .EnumerateFiles(shapesRoot, "*.cs", SearchOption.TopDirectoryOnly)
            .Select(File.ReadAllText)
            .ToArray();

        presentationShapeSources.Should().NotContain(source => source.Contains("public static class ShapeGeometryBuilder", StringComparison.Ordinal));
        presentationShapeSources.Should().NotContain(source => source.Contains("public sealed record ShapeGeometry", StringComparison.Ordinal));
        presentationShapeSources.Should().NotContain(source => source.Contains("public sealed record ShapeContour", StringComparison.Ordinal));
        presentationShapeSources.Should().NotContain(source => source.Contains("public enum ShapeSegmentKind", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(DrawingShapeKind.Rectangle)]
    [InlineData(DrawingShapeKind.Triangle)]
    [InlineData(DrawingShapeKind.Ellipse)]
    public void DegenerateBounds_ReturnsEmpty(DrawingShapeKind kind)
    {
        ShapeGeometryBuilder.Build(kind, new LayoutRect(100, 200, 0, 60)).Contours.Should().BeEmpty();
        ShapeGeometryBuilder.Build(kind, new LayoutRect(100, 200, 80, 0)).Contours.Should().BeEmpty();
    }

    [Fact]
    public void Rectangle_HasFourCornerVertices()
    {
        var geometry = ShapeGeometryBuilder.Build(DrawingShapeKind.Rectangle, Bounds);

        geometry.Contours.Should().ContainSingle();
        var contour = geometry.Contours[0];
        contour.Closed.Should().BeTrue();
        contour.Filled.Should().BeTrue();
        AllPoints(contour).Should().BeEquivalentTo(new[]
        {
            new LayoutPoint(100, 200),
            new LayoutPoint(180, 200),
            new LayoutPoint(180, 260),
            new LayoutPoint(100, 260)
        });
    }

    [Fact]
    public void Triangle_HasThreeVerticesAtExpectedFractions()
    {
        var geometry = ShapeGeometryBuilder.Build(DrawingShapeKind.Triangle, Bounds);

        var contour = geometry.Contours.Single();
        AllPoints(contour).Should().BeEquivalentTo(new[]
        {
            new LayoutPoint(140, 200), // (0.5, 0)
            new LayoutPoint(180, 260), // (1, 1)
            new LayoutPoint(100, 260)  // (0, 1)
        });
    }

    [Fact]
    public void Diamond_HasFourVertices()
    {
        var contour = ShapeGeometryBuilder.Build(DrawingShapeKind.Diamond, Bounds).Contours.Single();

        AllPoints(contour).Should().HaveCount(4);
        AllPoints(contour).Should().Contain(new LayoutPoint(140, 200)); // top (0.5,0)
        AllPoints(contour).Should().Contain(new LayoutPoint(140, 260)); // bottom (0.5,1)
    }

    [Theory]
    [InlineData(DrawingShapeKind.Pentagon, 5)]
    [InlineData(DrawingShapeKind.Hexagon, 6)]
    [InlineData(DrawingShapeKind.Octagon, 7 + 1)]
    [InlineData(DrawingShapeKind.RightArrow, 7)]
    [InlineData(DrawingShapeKind.LeftRightArrow, 10)]
    public void Polygon_HasExpectedVertexCount(DrawingShapeKind kind, int expectedVertices)
    {
        var contour = ShapeGeometryBuilder.Build(kind, Bounds).Contours.Single();

        AllPoints(contour).Should().HaveCount(expectedVertices);
        contour.Closed.Should().BeTrue();
        contour.Segments.Should().OnlyContain(s => s.Kind == ShapeSegmentKind.Line);
    }

    [Fact]
    public void Star5_HasTenAlternatingVertices()
    {
        var contour = ShapeGeometryBuilder.Build(DrawingShapeKind.Star5, Bounds).Contours.Single();

        AllPoints(contour).Should().HaveCount(10);
        // First vertex is the top tip: (0.5, 0.5 - 0.5) -> center-x, top.
        contour.Start.X.Should().BeApproximately(140, 1e-9);
        contour.Start.Y.Should().BeApproximately(200, 1e-9);
    }

    [Fact]
    public void Star8_HasSixteenVertices()
    {
        var contour = ShapeGeometryBuilder.Build(DrawingShapeKind.Star8, Bounds).Contours.Single();

        AllPoints(contour).Should().HaveCount(16);
    }

    [Fact]
    public void Explosion_HasTwentyFourVertices()
    {
        var contour = ShapeGeometryBuilder.Build(DrawingShapeKind.Explosion, Bounds).Contours.Single();

        AllPoints(contour).Should().HaveCount(24);
    }

    [Fact]
    public void Line_IsOpenUnfilledTwoPointPath()
    {
        var contour = ShapeGeometryBuilder.Build(DrawingShapeKind.Line, Bounds).Contours.Single();

        contour.Closed.Should().BeFalse();
        contour.Filled.Should().BeFalse();
        AllPoints(contour).Should().HaveCount(2);
    }

    [Fact]
    public void ElbowConnector_IsOpenFourPointPath()
    {
        var contour = ShapeGeometryBuilder.Build(DrawingShapeKind.ElbowConnector, Bounds).Contours.Single();

        contour.Closed.Should().BeFalse();
        AllPoints(contour).Should().HaveCount(4);
    }

    [Fact]
    public void CurvedConnector_UsesABezierSegment()
    {
        var contour = ShapeGeometryBuilder.Build(DrawingShapeKind.CurvedConnector, Bounds).Contours.Single();

        contour.Closed.Should().BeFalse();
        contour.Segments.Should().ContainSingle();
        contour.Segments[0].Kind.Should().Be(ShapeSegmentKind.CubicBezier);
    }

    [Fact]
    public void RoundedRectangle_HasFourArcCorners()
    {
        var contour = ShapeGeometryBuilder.Build(DrawingShapeKind.RoundedRectangle, Bounds).Contours.Single();

        contour.Closed.Should().BeTrue();
        contour.Segments.Count(s => s.Kind == ShapeSegmentKind.Arc).Should().Be(4);
        contour.Segments.Count(s => s.Kind == ShapeSegmentKind.Line).Should().Be(4);
    }

    [Fact]
    public void Ellipse_IsTwoArcs()
    {
        var contour = ShapeGeometryBuilder.Build(DrawingShapeKind.Ellipse, Bounds).Contours.Single();

        contour.Segments.Should().HaveCount(2);
        contour.Segments.Should().OnlyContain(s => s.Kind == ShapeSegmentKind.Arc);
        // Start is left extreme: (cx-rx, cy) = (100, 230).
        contour.Start.Should().Be(new LayoutPoint(100, 230));
    }

    [Fact]
    public void FlowchartTerminator_IsRoundedRectangle()
    {
        var contour = ShapeGeometryBuilder.Build(DrawingShapeKind.FlowchartTerminator, Bounds).Contours.Single();

        contour.Segments.Count(s => s.Kind == ShapeSegmentKind.Arc).Should().Be(4);
    }

    [Fact]
    public void Multiply_IsTwoRotatedBars()
    {
        var geometry = ShapeGeometryBuilder.Build(DrawingShapeKind.MultiplySign, Bounds);

        geometry.Contours.Should().HaveCount(2);
        geometry.Contours.Should().OnlyContain(c => c.Closed && c.Segments.Count == 3);
    }

    [Fact]
    public void Equal_IsTwoHorizontalBars()
    {
        var geometry = ShapeGeometryBuilder.Build(DrawingShapeKind.EqualSign, Bounds);

        geometry.Contours.Should().HaveCount(2);
        geometry.Contours.Should().OnlyContain(c => c.Closed && c.Segments.Count == 3);
    }

    [Fact]
    public void NotEqual_IsTwoBarsPlusRotatedSlash()
    {
        var geometry = ShapeGeometryBuilder.Build(DrawingShapeKind.NotEqualSign, Bounds);

        geometry.Contours.Should().HaveCount(3);
    }

    [Fact]
    public void Divide_IsBarPlusTwoDots()
    {
        var geometry = ShapeGeometryBuilder.Build(DrawingShapeKind.DivideSign, Bounds);

        geometry.Contours.Should().HaveCount(3);
        // Two of the three contours are dots (elliptical arcs).
        geometry.Contours.Count(c => c.Segments.Any(s => s.Kind == ShapeSegmentKind.Arc)).Should().Be(2);
    }

    [Fact]
    public void FlowchartPredefinedProcess_IsRectanglePlusTwoGuideLines()
    {
        var geometry = ShapeGeometryBuilder.Build(DrawingShapeKind.FlowchartPredefinedProcess, Bounds);

        geometry.Contours.Should().HaveCount(3);
        geometry.Contours.Count(c => !c.Closed).Should().Be(2);
    }

    [Fact]
    public void Wave_MixesBezierAndLineSegments()
    {
        var contour = ShapeGeometryBuilder.Build(DrawingShapeKind.Wave, Bounds).Contours.Single();

        contour.Closed.Should().BeTrue();
        contour.Segments.Count(s => s.Kind == ShapeSegmentKind.CubicBezier).Should().Be(4);
        contour.Segments.Count(s => s.Kind == ShapeSegmentKind.Line).Should().Be(1);
    }

    [Fact]
    public void Callouts_HaveBodyPlusTail()
    {
        ShapeGeometryBuilder.Build(DrawingShapeKind.RoundedRectangularCallout, Bounds).Contours.Should().HaveCount(2);
        ShapeGeometryBuilder.Build(DrawingShapeKind.OvalCallout, Bounds).Contours.Should().HaveCount(2);
        ShapeGeometryBuilder.Build(DrawingShapeKind.LineCallout, Bounds).Contours.Should().HaveCount(2);
    }

    [Fact]
    public void NegativeWidth_NormalizesToSameGeometryAsPositiveBounds()
    {
        // Bounds expressed right-to-left should normalize to the same rectangle.
        var flipped = new LayoutRect(180, 200, -80, 60);
        var normal = ShapeGeometryBuilder.Build(DrawingShapeKind.Triangle, Bounds);
        var fromFlipped = ShapeGeometryBuilder.Build(DrawingShapeKind.Triangle, flipped);

        AllPoints(fromFlipped.Contours.Single())
            .Should().BeEquivalentTo(AllPoints(normal.Contours.Single()));
    }

    [Fact]
    public void NegativeHeight_NormalizesToSameGeometryAsPositiveBounds()
    {
        var flipped = new LayoutRect(100, 260, 80, -60);
        var normal = ShapeGeometryBuilder.Build(DrawingShapeKind.RightArrow, Bounds);
        var fromFlipped = ShapeGeometryBuilder.Build(DrawingShapeKind.RightArrow, flipped);

        AllPoints(fromFlipped.Contours.Single())
            .Should().BeEquivalentTo(AllPoints(normal.Contours.Single()));
    }

    [Fact]
    public void UnknownKind_FallsBackToRectangle()
    {
        var geometry = ShapeGeometryBuilder.Build((DrawingShapeKind)9999, Bounds);

        var contour = geometry.Contours.Single();
        AllPoints(contour).Should().HaveCount(4);
        contour.Closed.Should().BeTrue();
    }

    [Fact]
    public void FlowchartProcess_FallsBackToRectangle()
    {
        // FlowchartProcess has no dedicated case, so it uses the rectangle fallback.
        var contour = ShapeGeometryBuilder.Build(DrawingShapeKind.FlowchartProcess, Bounds).Contours.Single();

        AllPoints(contour).Should().HaveCount(4);
    }

    [Fact]
    public void CrossAndPlusSign_ProduceIdenticalGeometry()
    {
        var cross = ShapeGeometryBuilder.Build(DrawingShapeKind.Cross, Bounds);
        var plus = ShapeGeometryBuilder.Build(DrawingShapeKind.PlusSign, Bounds);

        var crossPoints = AllPoints(cross.Contours.Single());
        var plusPoints = AllPoints(plus.Contours.Single());
        crossPoints.Should().Equal(plusPoints);
        crossPoints.Should().HaveCount(12);
    }

    // ── Cylinder ────────────────────────────────────────────────────────────

    [Fact]
    public void Cylinder_HasTwoContours_BodyAndTopEllipse()
    {
        var geometry = ShapeGeometryBuilder.Build(DrawingShapeKind.Cylinder, Bounds);

        geometry.Contours.Should().HaveCount(2,
            "Cylinder must emit two contours: body outline + top ellipse cap");
    }

    [Fact]
    public void Cylinder_BodyContour_IsClosedAndFilled()
    {
        var body = ShapeGeometryBuilder.Build(DrawingShapeKind.Cylinder, Bounds).Contours[0];

        body.Closed.Should().BeTrue();
        body.Filled.Should().BeTrue();
    }

    [Fact]
    public void Cylinder_BodyContour_ContainsTwoStraightSidesAndTwoArcs()
    {
        var body = ShapeGeometryBuilder.Build(DrawingShapeKind.Cylinder, Bounds).Contours[0];

        // Body: start + LineTo(leftBottom) + ArcTo(rightBottom) + LineTo(rightTop) + ArcTo(start)
        body.Segments.Count(s => s.Kind == ShapeSegmentKind.Line).Should().Be(2,
            "body must have left-side and right-side straight lines");
        body.Segments.Count(s => s.Kind == ShapeSegmentKind.Arc).Should().Be(2,
            "body must have bottom (convex) and top (concave) arc segments");
    }

    [Fact]
    public void Cylinder_TopCap_IsTwoArcs_FullEllipse()
    {
        var cap = ShapeGeometryBuilder.Build(DrawingShapeKind.Cylinder, Bounds).Contours[1];

        cap.Segments.Should().HaveCount(2);
        cap.Segments.Should().OnlyContain(s => s.Kind == ShapeSegmentKind.Arc,
            "top cap must be a full ellipse (two half-arc segments)");
    }

    [Fact]
    public void Cylinder_IsRenderable()
    {
        DrawingShapeKindSupport.IsRenderable(DrawingShapeKind.Cylinder).Should().BeTrue();
    }

    [Fact]
    public void Cylinder_IsNotLineLike()
    {
        DrawingShapeKindSupport.IsLineLike(DrawingShapeKind.Cylinder).Should().BeFalse();
    }

    // ── CurvedConnector S-curve improvement ─────────────────────────────────

    [Fact]
    public void CurvedConnector_StartsAtTopLeft_EndsAtBottomRight()
    {
        var geometry = ShapeGeometryBuilder.Build(DrawingShapeKind.CurvedConnector, Bounds);
        var contour = geometry.Contours.Single();

        // Start: top-left corner (rect.Left, rect.Top) = (100, 200)
        contour.Start.Should().Be(new LayoutPoint(Bounds.Left, Bounds.Top));
        // End (segment.End): bottom-right corner (rect.Right, rect.Bottom) = (180, 260)
        contour.Segments.Single().End.Should().Be(new LayoutPoint(Bounds.Right, Bounds.Bottom));
    }

    [Fact]
    public void CurvedConnector_UsesHorizontalEndpointTangents()
    {
        var contour = ShapeGeometryBuilder.Build(DrawingShapeKind.CurvedConnector, Bounds).Contours.Single();
        var segment = contour.Segments.Single();

        segment.Control1.Should().Be(new LayoutPoint(Bounds.Left + Bounds.Width * 0.67, Bounds.Top));
        segment.Control2.Should().Be(new LayoutPoint(Bounds.Left + Bounds.Width * 0.33, Bounds.Bottom));
    }

    [Fact]
    public void CurvedConnector_IsOpenUnfilledSingleBezier()
    {
        var contour = ShapeGeometryBuilder.Build(DrawingShapeKind.CurvedConnector, Bounds).Contours.Single();

        contour.Closed.Should().BeFalse();
        contour.Filled.Should().BeFalse();
        contour.Segments.Should().ContainSingle();
        contour.Segments[0].Kind.Should().Be(ShapeSegmentKind.CubicBezier);
    }
}
