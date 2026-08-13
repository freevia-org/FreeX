using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class RendererRecipePlannerTests
{
    [Fact]
    public void GradientPlanner_ExpandsLinearLightStopsAndPreservesLinearAlphaPositions()
    {
        var gradient = new ResolvedFill.Gradient(
            [
                new ResolvedFill.ResolvedGradientStop(0, new SrgbColor(0, 0, 0), 10),
                new ResolvedFill.ResolvedGradientStop(1, new SrgbColor(255, 128, 64), 250)
            ],
            GradientKind.Linear,
            0);

        var stops = GradientFillRenderPlanner.ExpandStops(gradient, easePositions: true);

        stops.Should().HaveCount(17);
        stops[4].Position.Should().BeApproximately(0.25, 0.000001);
        stops[4].Alpha.Should().Be(70);
        stops[4].Color.Should().Be(GradientColorInterpolation.InterpolateLinearLight(
            gradient.Stops[0].Color,
            gradient.Stops[1].Color,
            GradientColorInterpolation.EasePowerPointPosition(0.25)));
        stops[^1].Should().Be(new GradientRenderStop(1, new SrgbColor(255, 128, 64), 250));
    }

    [Fact]
    public void GradientPlanner_EncodesCenteredAndAvaloniaTextEndpointProfiles()
    {
        var centered = GradientFillRenderPlanner.PlanLinearEndpoints(90);
        var avaloniaText = GradientFillRenderPlanner.PlanLinearEndpoints(
            135,
            GradientEndpointProfile.AvaloniaTextCorners);

        centered.Start.X.Should().BeApproximately(0.5, 0.000001);
        centered.Start.Y.Should().BeApproximately(0, 0.000001);
        centered.End.X.Should().BeApproximately(0.5, 0.000001);
        centered.End.Y.Should().BeApproximately(1, 0.000001);
        avaloniaText.Should().Be(new LinearGradientEndpointPlan(
            new GradientRenderPoint(1, 0),
            new GradientRenderPoint(0, 1)));
    }

    [Fact]
    public void PatternPlanner_PreservesWpfVectorCrossAndInverseDotRecipes()
    {
        var cross = PatternFillRenderPlanner.Plan(
                "cross",
                PatternFillRendererProfile.WpfVector)
            .Should().BeOfType<PatternFillRenderPlan.VectorTile>().Subject;
        var inverseDots = PatternFillRenderPlanner.Plan(
                "pct60",
                PatternFillRendererProfile.WpfVector)
            .Should().BeOfType<PatternFillRenderPlan.VectorTile>().Subject;

        cross.Width.Should().Be(8);
        cross.Height.Should().Be(8);
        cross.Primitives.Should().HaveCount(3);
        cross.Primitives.Skip(1).Should().OnlyContain(
            primitive => primitive.Color == PatternFillColorRole.Foreground);
        inverseDots.Primitives[0].Color.Should().Be(PatternFillColorRole.Foreground);
        inverseDots.Primitives.Skip(1).Should().HaveCount(3).And.OnlyContain(
            primitive => primitive is PatternFillVectorPrimitive.Ellipse
                && primitive.Color == PatternFillColorRole.Background);
    }

    [Fact]
    public void PatternPlanner_PreservesAvaloniaPixelMasksAndCrossTileSize()
    {
        var checker = PatternFillRenderPlanner.Plan(
                "pct40",
                PatternFillRendererProfile.AvaloniaPixel)
            .Should().BeOfType<PatternFillRenderPlan.PixelTile>().Subject;
        var cross = PatternFillRenderPlanner.Plan(
                "cross",
                PatternFillRendererProfile.AvaloniaPixel)
            .Should().BeOfType<PatternFillRenderPlan.PixelTile>().Subject;

        checker.Width.Should().Be(6);
        checker.Pixels.Count(role => role == PatternFillColorRole.Foreground).Should().Be(18);
        cross.Width.Should().Be(8);
        cross.Pixels.Count(role => role == PatternFillColorRole.Foreground).Should().Be(15);
    }

    [Fact]
    public void ShapeAutoFitPlanner_RequestsNativeMeasurementsAndGrowsEligibleShape()
    {
        var shape = new DrawOp.Shape
        {
            BoundsDip = new LayoutRect(10, 20, 100, 50),
            Text = new ResolvedTextLayout
            {
                AutoFitKind = TextAutoFitKind.Shape,
                Anchor = VerticalAnchor.Middle,
                InsetLeftDip = 5,
                InsetRightDip = 7,
                InsetTopDip = 4,
                InsetBottomDip = 4,
                Paragraphs =
                [
                    new ResolvedParagraph
                    {
                        Runs = [new ResolvedRun { Text = "Measured" }],
                        SpaceBeforePt = 1.5,
                        SpaceAfterPt = 1.5
                    }
                ]
            }
        };
        ShapeAutoFitMeasurementRequest request = default;

        var bounds = ShapeAutoFitRenderPlanner.Plan(shape, value =>
        {
            request = value;
            return 80;
        });

        request.ParagraphIndex.Should().Be(0);
        request.MaximumWidthDip.Should().Be(88);
        request.Wrap.Should().BeTrue();
        request.AutoFitKind.Should().Be(TextAutoFitKind.Shape);
        bounds.Should().Be(new LayoutRect(10, -1, 100, 92));
    }

    [Fact]
    public void ShapeAutoFitPlanner_OwnsGrowthTransformsForNativeRenderers()
    {
        var shape = new DrawOp.Shape
        {
            BoundsDip = new LayoutRect(10, 20, 100, 50),
            Text = new ResolvedTextLayout
            {
                AutoFitKind = TextAutoFitKind.Shape,
                Anchor = VerticalAnchor.Middle,
                InsetTopDip = 4,
                InsetBottomDip = 4,
                Paragraphs =
                [
                    new ResolvedParagraph
                    {
                        Runs = [new ResolvedRun { Text = "Measured" }],
                        SpaceBeforePt = 1.5,
                        SpaceAfterPt = 1.5,
                    },
                ],
            },
        };

        var plan = ShapeAutoFitRenderPlanner.PlanRender(shape, _ => 80);

        plan.Bounds.Should().Be(new LayoutRect(10, -1, 100, 92));
        plan.RenderTransform.Should().Be(ShapeAffineTransform.Identity);
        plan.GeometryTransform.M11.Should().Be(1);
        plan.GeometryTransform.M22.Should().BeApproximately(1.84, 0.000001);
        plan.GeometryTransform.OffsetY.Should().BeApproximately(-37.8, 0.000001);
    }

    [Fact]
    public void ShapeAutoFitPlanner_SkipsNativeMeasurementForTransformedShape()
    {
        var shape = new DrawOp.Shape
        {
            BoundsDip = new LayoutRect(1, 2, 30, 40),
            RotationDeg = 5,
            Text = new ResolvedTextLayout { AutoFitKind = TextAutoFitKind.Shape }
        };

        var bounds = ShapeAutoFitRenderPlanner.Plan(
            shape,
            _ => throw new InvalidOperationException("Ineligible shapes must not be measured."));

        bounds.Should().Be(shape.BoundsDip);
    }

    [Fact]
    public void ShapeEffectPlanner_CalibratesImportedShapeShadowOnlyWhenBoundsAreProvided()
    {
        var effects = new ResolvedShapeEffects
        {
            HasOuterShadow = true,
            OuterShadowColor = new SrgbColor(0x40, 0x40, 0x40),
            OuterShadowAlpha = 153,
            OuterShadowBlurDip = 8,
            OuterShadowDistDip = 11.31,
            OuterShadowDirDeg = 45
        };

        var shapePlan = ResolvedShapeEffectRenderPlanner.PlanOuterEffects(
            effects,
            new LayoutRect(0, 0, 100, 100));
        var nonShapePlan = ResolvedShapeEffectRenderPlanner.PlanOuterEffects(effects);

        shapePlan.ShadowPasses.Should().HaveCount(33);
        shapePlan.ShadowPasses[0].Alpha.Should().Be(15);
        shapePlan.ShadowPasses[^1].Alpha.Should().Be(153);
        nonShapePlan.ShadowPasses[0].Alpha.Should().Be(30);
    }

    [Fact]
    public void ViewZoomPlanner_ComposesCenteredPercentStageTransform()
    {
        var transform = PresentationViewZoomPlanner.PlanStageTransform(
            renderWidth: 1000,
            renderHeight: 800,
            slideWidthDip: 100,
            slideHeightDip: 50,
            state: new PresentationViewZoomState(PresentationViewZoomMode.Percent, 150));

        transform.Scale.Should().Be(15);
        transform.OffsetX.Should().Be(-250);
        transform.OffsetY.Should().Be(25);
        transform.SlideWidthDip.Should().Be(100);
        transform.SlideHeightDip.Should().Be(50);
    }
}
