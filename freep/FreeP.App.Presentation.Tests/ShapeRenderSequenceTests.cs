using Free.Shared.Drawing;

namespace FreeP.App.Compositor.Tests;

public sealed class ShapeRenderSequenceTests
{
    private static readonly LayoutRect Bounds = new(10, 20, 90, 60);

    [Fact]
    public void Dispatch_PlainShape_UsesContoursThenFillAndOutline()
    {
        var steps = Run(Shape(effects: null));

        steps.Should().Equal("geometry:Contours", "draw:outline=True");
    }

    [Fact]
    public void Dispatch_OrdersEffectsBeforeFillAndBevelAfterIt()
    {
        var steps = Run(Shape(new ResolvedShapeEffects
        {
            HasOuterShadow = true,
            OuterShadowAlpha = 100,
            OuterShadowBlurDip = 0,
            OuterShadowDistDip = 4,
            BevelTop = new ResolvedBevel { WidthDip = 10, HeightDip = 10 },
            ContourWidthDip = 2,
        }));

        var draw = steps.IndexOf("draw:outline=True");
        steps.Take(draw).Should().Contain(step => step.StartsWith("shadow"));
        steps.IndexOf("bevel:4").Should().BeGreaterThan(draw);
        steps[^1].Should().StartWith("contour:2");
    }

    // Divergence 1: a shape with only a bottom bevel paints the bevel overlay (WPF fallback) on every host.
    [Fact]
    public void Dispatch_BottomOnlyBevel_FallsBackToBottomBevel()
    {
        var steps = Run(Shape(new ResolvedShapeEffects
        {
            BevelBottom = new ResolvedBevel { WidthDip = 10, HeightDip = 10 },
        }));

        steps.Should().Contain("bevel:4");
    }

    [Fact]
    public void Dispatch_TopBevelWinsOverBottomBevel()
    {
        var topOnly = ShapeRenderSequence.PlanBevelWedges(
            Bounds, new ResolvedBevel { WidthDip = 10, HeightDip = 10 }, -1);
        topOnly.Should().HaveCount(4);
    }

    // Divergence 2: relaxed inset has a rounded-rect face and a second shaded inner band of four wedges.
    [Fact]
    public void RelaxedInset_UsesRoundedRectGeometryAndInnerShadeBand()
    {
        var bevel = new ResolvedBevel { WidthDip = 10, HeightDip = 10, PresetName = "relaxedInset" };

        var wedges = ShapeRenderSequence.PlanBevelWedges(Bounds, bevel, -1);

        wedges.Should().HaveCount(8);
        wedges.Skip(4).Should().OnlyContain(w => w.Color == new SrgbColor(0, 0, 0) && w.Alpha == 110);

        var shape = Shape(new ResolvedShapeEffects { BevelTop = bevel, ExtrusionDepthDip = 26, Scene3dCameraPreset = "orthographicFront" }, shapeId: 4);
        var material = ShapeMaterialRenderPlanner.Plan(shape);
        material.Kind.Should().Be(ImportedShapeMaterialKind.RelaxedInset);
        var geometry = ShapeRenderSequence.PlanGeometry(shape, material);
        geometry.Kind.Should().Be(ShapeGeometryKind.RoundedRect);
        geometry.CornerRadiusDip.Should().BeApproximately(60 * 0.16, 1e-9);

        Run(shape).Should().Contain("geometry:RoundedRect").And.Contain("material:RelaxedInset");
    }

    [Fact]
    public void Wedges_AreTrapezoidsInPerimeterOrder()
    {
        var wedges = ShapeRenderSequence.PlanBevelWedges(
            new LayoutRect(0, 0, 100, 60), new ResolvedBevel { WidthDip = 10, HeightDip = 10 }, -1);
        // Default light is top-left: bevel 10 x 0.4 = 4 dip footprint.
        wedges[0].Should().Be(new ShapeBevelWedge(
            new(0, 0), new(100, 0), new(96, 4), new(4, 4), new SrgbColor(255, 255, 255), 120));
        wedges[1].P1.Should().Be(new LayoutPoint(4, 56));
        wedges[1].P3.Should().Be(new LayoutPoint(100, 60));
        wedges[1].Color.Should().Be(new SrgbColor(0, 0, 0));
        wedges[1].Alpha.Should().Be(110);
    }

    // Divergence 3: the soft-edge ring stroke is 20 percent of the planned width.
    [Fact]
    public void SoftEdge_StrokeWidthIsScaledByOuterSpread()
    {
        var shape = Shape(new ResolvedShapeEffects { HasSoftEdge = true, SoftEdgeRadiusDip = 10 },
            fill: new ResolvedFill.Solid(new SrgbColor(1, 2, 3)));
        var plan = ResolvedShapeEffectRenderPlanner.PlanOuterEffects(shape.Effects, shape.BoundsDip);
        plan.SoftEdgePasses.Should().NotBeEmpty();

        var steps = Run(shape);

        var expected = plan.SoftEdgePasses
            .Select(p => $"softedge:{p.StrokeWidthDip * 0.20:F3}:{p.Alpha}");
        steps.Where(s => s.StartsWith("softedge")).Should().Equal(expected);
        ShapeRenderSequence.SoftEdgeOuterSpreadScale.Should().Be(0.20);
    }

    // Divergence 4: reflection passes carry their own pass opacity.
    [Fact]
    public void Reflection_PassesCarryOpacityAndPivot()
    {
        var shape = Shape(new ResolvedShapeEffects
        {
            HasReflection = true,
            ReflectionAlpha = 128,
            ReflectionBlurDip = 4,
            ReflectionDistDip = 6,
            ReflectionScaleY = -1,
            ReflectionEndPos = 0.5,
        });
        var plan = ResolvedShapeEffectRenderPlanner.PlanOuterEffects(shape.Effects, shape.BoundsDip);
        plan.ReflectionPasses.Should().NotBeEmpty();

        var steps = Run(shape);

        var expected = plan.ReflectionPasses.Select(p =>
            $"reflection:{p.Opacity:F4}:{p.OffsetXDip:F3}:{p.OffsetYDip:F3}:pivot={plan.ReflectionPivotYDip:F3}:cx=55.000");
        steps.Where(s => s.StartsWith("reflection")).Should().Equal(expected);
        plan.ReflectionPasses.Should().Contain(p => p.Opacity > 0 && p.Opacity <= 1);
    }

    // Divergence 5: soft edge suppresses the outline stroke on elbow routes as well as on filled geometry.
    [Fact]
    public void ElbowRoute_SoftEdgeSuppressesOutlineStroke()
    {
        var route = new LayoutPoint[] { new(0, 0), new(10, 0), new(10, 10) };

        Run(Shape(new ResolvedShapeEffects { HasSoftEdge = true }, route: route))
            .Should().Contain("route:3:outline=False");
        Run(Shape(effects: null, route: route))
            .Should().Contain("route:3:outline=True").And.NotContain(s => s.StartsWith("draw"));
        Run(Shape(new ResolvedShapeEffects { HasSoftEdge = true }))
            .Should().Contain("draw:outline=False");
    }

    [Fact]
    public void ElbowRoute_NeedsTwoPointsOtherwiseUsesGeometry()
    {
        Run(Shape(effects: null, route: new LayoutPoint[] { new(0, 0) }))
            .Should().Contain("draw:outline=True");
    }

    [Fact]
    public void Shadow_IsSuppressedForUnfilledTextButGlowStays()
    {
        var effects = new ResolvedShapeEffects
        {
            HasOuterShadow = true,
            OuterShadowAlpha = 100,
            OuterShadowDistDip = 4,
            HasGlow = true,
            GlowAlpha = 100,
            GlowRadiusDip = 4,
        };
        var withText = Shape(effects, withText: true);

        var steps = Run(withText);

        steps.Should().NotContain(s => s.StartsWith("shadow"));
        steps.Should().Contain(s => s.StartsWith("glow"));
    }

    [Fact]
    public void Dispatch_RejectsNullShape()
    {
        var sink = new RecordingSink([]);

        var act = () => ShapeRenderSequence.Dispatch(null!, ref sink);

        act.Should().Throw<ArgumentNullException>();
    }

    // Divergence 6: snap guides span the adorner's extent, not +-5000.
    [Fact]
    public void SnapGuides_AreClippedToTheAdornerBounds()
    {
        var transform = new SlideTransformCore(2, 10, 20, 960, 540);
        var guides = new[]
        {
            new SnapGuideLine { IsHorizontal = true, Position = 100 },
            new SnapGuideLine { IsHorizontal = false, Position = 50 },
        };

        var segments = SnapGuideRenderPlanner.Plan(guides, transform, 800, 600);

        segments.Should().Equal(
            new SnapGuideSegment(new LayoutPoint(0, 220), new LayoutPoint(800, 220)),
            new SnapGuideSegment(new LayoutPoint(110, 0), new LayoutPoint(110, 600)));
        segments.Should().OnlyContain(s =>
            Math.Abs(s.Start.X) <= 800 && Math.Abs(s.End.X) <= 800 && Math.Abs(s.End.Y) <= 600);
    }

    [Fact]
    public void SnapGuides_EmptyListPlansNothing()
    {
        SnapGuideRenderPlanner.Plan([], SlideTransformCore.Identity, 10, 10).Should().BeEmpty();
    }

    private static DrawOp.Shape Shape(
        ResolvedShapeEffects? effects,
        uint shapeId = 0,
        ResolvedFill? fill = null,
        IReadOnlyList<LayoutPoint>? route = null,
        bool withText = false)
    {
        var contour = new ShapeContour(
            new LayoutPoint(Bounds.X, Bounds.Y),
            [
                ShapeSegment.LineTo(new LayoutPoint(Bounds.Right, Bounds.Y)),
                ShapeSegment.LineTo(new LayoutPoint(Bounds.Right, Bounds.Bottom)),
                ShapeSegment.LineTo(new LayoutPoint(Bounds.X, Bounds.Bottom)),
            ],
            Closed: true,
            Filled: true);
        return new DrawOp.Shape
        {
            ShapeId = shapeId,
            BoundsDip = Bounds,
            Geometry = new ShapeGeometry([contour]),
            Fill = fill ?? (withText
                ? ResolvedFill.None.Instance
                : new ResolvedFill.Solid(new SrgbColor(0x64, 0x32, 0x1E))),
            Effects = effects,
            ElbowRouteDip = route,
            Text = withText ? new ResolvedTextLayout() : null,
        };
    }

    private static List<string> Run(DrawOp.Shape shape)
    {
        var steps = new List<string>();
        var sink = new RecordingSink(steps);
        ShapeRenderSequence.Dispatch(shape, ref sink);
        return steps;
    }

    private readonly struct RecordingSink(List<string> steps) : IShapeRenderSink
    {
        public void UseGeometry(ShapeGeometrySource geometry) => steps.Add($"geometry:{geometry.Kind}");

        public void FillShadow(FreeP.Core.Model.ShapeShadowPass pass) => steps.Add($"shadow:{pass.Alpha}");

        public void StrokeGlow(FreeP.Core.Model.ShapeGlowPass pass) => steps.Add($"glow:{pass.Alpha}");

        public void StrokeSoftEdge(FreeP.Core.Model.ShapeSoftEdgePass pass) =>
            steps.Add($"softedge:{pass.StrokeWidthDip:F3}:{pass.Alpha}");

        public void DrawReflection(ShapeReflectionStep step) =>
            steps.Add($"reflection:{step.Opacity:F4}:{step.OffsetXDip:F3}:{step.OffsetYDip:F3}:pivot={step.PivotYDip:F3}:cx={step.CenterXDip:F3}");

        public void FillExtrusion(ShapeMaterialRenderPlan material) => steps.Add("extrusion");

        public void StrokeElbowRoute(IReadOnlyList<LayoutPoint> route, bool strokeOutline) =>
            steps.Add($"route:{route.Count}:outline={strokeOutline}");

        public void DrawGeometry(bool strokeOutline) => steps.Add($"draw:outline={strokeOutline}");

        public void PaintMaterial(ShapeMaterialRenderPlan material) => steps.Add($"material:{material.Kind}");

        public void PaintBevel(IReadOnlyList<ShapeBevelWedge> wedges) => steps.Add($"bevel:{wedges.Count}");

        public void StrokeContour(SrgbColor color, double widthDip) => steps.Add($"contour:{widthDip}");
    }
}
