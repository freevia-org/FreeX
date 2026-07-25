namespace FreeP.App.Host.Tests;

/// <summary>
/// Tests that verify the shared <see cref="ShapeGeometryBuilder"/> produces correct geometry
/// for the Cylinder shape and the improved CurvedConnector S-curve (Wave 4 sync).
/// Also covers the <see cref="PptxShapeKindMap"/> Cylinder ↔ "can" preset mapping.
/// These tests guard against drift between the FreeX-private copy and the shared copy
/// consumed by FreeP.
/// </summary>
public sealed class SharedShapeGeometryBuilderTests
{
    private static readonly LayoutRect Bounds = new(100, 200, 80, 60);

    private static IEnumerable<LayoutPoint> AllPoints(ShapeContour contour)
    {
        yield return contour.Start;
        foreach (var segment in contour.Segments)
            yield return segment.End;
    }

    // ── Cylinder geometry ────────────────────────────────────────────────────

    [Fact]
    public void Cylinder_HasTwoContours_BodyAndTopEllipse()
    {
        var geometry = ShapeGeometryBuilder.Build(DrawingShapeKind.Cylinder, Bounds);

        geometry.Contours.Should().HaveCount(2,
            "Cylinder must emit two contours: body outline + top ellipse cap");
    }

    [Fact]
    public void Cylinder_DoesNotFallBackToRectangle()
    {
        // A rectangle fallback is a single closed 4-vertex contour.
        // Cylinder must have 2 contours — this is the key regression guard.
        var geometry = ShapeGeometryBuilder.Build(DrawingShapeKind.Cylinder, Bounds);

        geometry.Contours.Should().HaveCount(2,
            "Cylinder must not silently fall through to the Rectangle default branch");
    }

    [Fact]
    public void Cylinder_BodyContour_IsClosedAndFilled()
    {
        var body = ShapeGeometryBuilder.Build(DrawingShapeKind.Cylinder, Bounds).Contours[0];

        body.Closed.Should().BeTrue();
        body.Filled.Should().BeTrue();
    }

    [Fact]
    public void Cylinder_BodyContour_HasTwoStraightSidesAndTwoArcs()
    {
        var body = ShapeGeometryBuilder.Build(DrawingShapeKind.Cylinder, Bounds).Contours[0];

        body.Segments.Count(s => s.Kind == ShapeSegmentKind.Line).Should().Be(2,
            "body must have left-side and right-side straight lines");
        body.Segments.Count(s => s.Kind == ShapeSegmentKind.Arc).Should().Be(2,
            "body must have bottom (convex) and top (concave) arc segments");
    }

    [Fact]
    public void Cylinder_TopCap_IsFullEllipse_TwoArcs()
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

    [Fact]
    public void Chord_UsesPresetAnglesAndClosesBackToTheStart()
    {
        var geometry = ShapeGeometryBuilder.Build(
            DrawingShapeKind.Chord,
            new LayoutRect(0, 0, 100, 100),
            new Dictionary<string, double>
            {
                ["adj1"] = 1168272,
                ["adj2"] = 9631728
            });

        var contour = geometry.Contours.Should().ContainSingle().Subject;
        contour.Closed.Should().BeTrue();
        contour.Filled.Should().BeTrue();
        contour.Segments.Should().HaveCount(11);
        contour.Segments.Should().OnlyContain(segment => segment.Kind == ShapeSegmentKind.Line);
        contour.Segments[0].End.X.Should().BeApproximately(91.657, 0.001);
        contour.Segments[0].End.Y.Should().BeApproximately(77.653, 0.001);
        contour.Segments[^1].End.Should().Be(contour.Start);
    }

    [Fact]
    public void Chord_EqualAnglesProduceFullEllipse()
    {
        var geometry = ShapeGeometryBuilder.Build(
            DrawingShapeKind.Chord,
            new LayoutRect(0, 0, 100, 100),
            new Dictionary<string, double>
            {
                ["adj1"] = 16200000,
                ["adj2"] = 16200000
            });

        geometry.Contours.Should().ContainSingle();
        geometry.Contours[0].Segments.Should().OnlyContain(segment => segment.Kind == ShapeSegmentKind.Arc);
        geometry.Contours[0].Segments.Should().HaveCount(2);
    }

    [Fact]
    public void RoundedRectangle_UsesAuthoredAdjustmentWhenPresent()
    {
        var geometry = ShapeGeometryBuilder.Build(
            DrawingShapeKind.RoundedRectangle,
            Bounds,
            new Dictionary<string, double> { ["adj"] = 50000 });

        geometry.Contours.Should().ContainSingle();
        geometry.Contours[0].Start.Should().Be(new LayoutPoint(Bounds.Left + Bounds.Height / 2, Bounds.Top));
    }

    [Fact]
    public void RoundedRectangle_ClampsAuthoredAdjustmentToHalfDimension()
    {
        var geometry = ShapeGeometryBuilder.Build(
            DrawingShapeKind.RoundedRectangle,
            Bounds,
            new Dictionary<string, double> { ["adj"] = 90000 });

        geometry.Contours[0].Start.Should().Be(new LayoutPoint(Bounds.Left + Bounds.Height / 2, Bounds.Top));
    }

    [Fact]
    public void Triangle_UsesAuthoredApexAdjustmentWhenPresent()
    {
        var geometry = ShapeGeometryBuilder.Build(
            DrawingShapeKind.Triangle,
            Bounds,
            new Dictionary<string, double> { ["adj"] = 25000 });

        geometry.Contours.Should().ContainSingle();
        geometry.Contours[0].Start.Should().Be(new LayoutPoint(Bounds.Left + Bounds.Width / 4, Bounds.Top));
    }

    [Fact]
    public void Cylinder_EnumValue_Is44_NoRenumbering()
    {
        // Appended after HomePlate=43 — verify no renumbering occurred.
        ((int)DrawingShapeKind.Cylinder).Should().Be(44);
        ((int)DrawingShapeKind.HomePlate).Should().Be(43);
    }

    // ── CurvedConnector S-curve (Wave 4 improvement) ─────────────────────────

    [Fact]
    public void CurvedConnector_StartsAtTopLeft_EndsAtBottomRight()
    {
        var contour = ShapeGeometryBuilder.Build(DrawingShapeKind.CurvedConnector, Bounds).Contours.Single();

        // Improved S-curve: start at top-left (0,0), end at bottom-right (1,1).
        contour.Start.Should().Be(new LayoutPoint(Bounds.Left, Bounds.Top),
            "CurvedConnector must start at the top-left corner of the bounding box");
        contour.Segments.Single().End.Should().Be(new LayoutPoint(Bounds.Right, Bounds.Bottom),
            "CurvedConnector must end at the bottom-right corner of the bounding box");
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

    [Fact]
    public void CurvedConnector_StartAndEnd_AreExactCorners_NotOffset()
    {
        // Guard against the OLD stale control points: start=(0.05,0.18) end=(0.95,0.82).
        // The corrected version uses exact corners: start=(0,0) end=(1,1).
        var contour = ShapeGeometryBuilder.Build(DrawingShapeKind.CurvedConnector, Bounds).Contours.Single();

        contour.Start.X.Should().BeApproximately(Bounds.Left, 1e-9,
            "start X must be exactly rect.Left (0 fraction), not offset");
        contour.Start.Y.Should().BeApproximately(Bounds.Top, 1e-9,
            "start Y must be exactly rect.Top (0 fraction), not offset");

        var bezier = contour.Segments.Single();
        bezier.End.X.Should().BeApproximately(Bounds.Right, 1e-9,
            "end X must be exactly rect.Right (1 fraction), not offset");
        bezier.End.Y.Should().BeApproximately(Bounds.Bottom, 1e-9,
            "end Y must be exactly rect.Bottom (1 fraction), not offset");
    }

    // ── PptxShapeKindMap: Cylinder ↔ "can" preset ───────────────────────────

    [Fact]
    public void PptxShapeKindMap_FromPreset_Can_ReturnsCylinder()
    {
        // OOXML DrawingML preset for the cylinder (database) shape is "can".
        PptxShapeKindMap.FromPreset("can").Should().Be(DrawingShapeKind.Cylinder,
            "OOXML 'can' preset must map to DrawingShapeKind.Cylinder");
    }

    [Fact]
    public void PptxShapeKindMap_ToPreset_Cylinder_ReturnsCan()
    {
        PptxShapeKindMap.ToPreset(DrawingShapeKind.Cylinder).Should().Be("can",
            "DrawingShapeKind.Cylinder must round-trip back to OOXML preset 'can'");
    }

    [Fact]
    public void PptxShapeKindMap_Cylinder_RoundTrip_IsStable()
    {
        var kind = PptxShapeKindMap.FromPreset(PptxShapeKindMap.ToPreset(DrawingShapeKind.Cylinder));
        kind.Should().Be(DrawingShapeKind.Cylinder,
            "Cylinder prst→kind→prst→kind round-trip must be stable");
    }

    [Fact]
    public void PptxShapeKindMap_DelegatesPresetMappingToSharedDrawing()
    {
        var source = TestWorkspaceFileLocator.ReadAllText("freep", "FreeP.Core.IO", "PptxShapeKindMap.cs");

        source.Should().Contain("DrawingMlPresetGeometryMap.GetShapeKindOrDefault");
        source.Should().Contain("DrawingMlPresetGeometryMap.GetPreset");
        source.Should().NotContain("prst?.ToLowerInvariant() switch");
        source.Should().NotContain("DrawingShapeKind.Cylinder => \"can\"");
    }
}
