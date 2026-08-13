namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowAnimationRendererRoutePlannerTests
{
    [Fact]
    public void Build_CoversEveryAuthoredEffectWithOneRendererPrimitive()
    {
        var expected = new Dictionary<SlideShowAnimationRendererRouteKind, SlideShowShapeAnimationEffectKind[]>
        {
            [SlideShowAnimationRendererRouteKind.Instant] =
                [SlideShowShapeAnimationEffectKind.Appear],
            [SlideShowAnimationRendererRouteKind.MotionPath] =
                [SlideShowShapeAnimationEffectKind.MotionPath],
            [SlideShowAnimationRendererRouteKind.Opacity] =
                [SlideShowShapeAnimationEffectKind.Fade],
            [SlideShowAnimationRendererRouteKind.Fly] =
                [SlideShowShapeAnimationEffectKind.FlyIn],
            [SlideShowAnimationRendererRouteKind.WipeMask] =
                [SlideShowShapeAnimationEffectKind.Wipe],
            [SlideShowAnimationRendererRouteKind.SplitMask] =
                [SlideShowShapeAnimationEffectKind.Split],
            [SlideShowAnimationRendererRouteKind.RandomBarsMask] =
                [SlideShowShapeAnimationEffectKind.RandomBars],
            [SlideShowAnimationRendererRouteKind.BlindsMask] =
                [SlideShowShapeAnimationEffectKind.Blinds],
            [SlideShowAnimationRendererRouteKind.BoxMask] =
                [SlideShowShapeAnimationEffectKind.Box],
            [SlideShowAnimationRendererRouteKind.CheckerboardMask] =
                [SlideShowShapeAnimationEffectKind.Checkerboard],
            [SlideShowAnimationRendererRouteKind.GeometricMask] =
            [
                SlideShowShapeAnimationEffectKind.Circle,
                SlideShowShapeAnimationEffectKind.Diamond,
                SlideShowShapeAnimationEffectKind.Plus,
                SlideShowShapeAnimationEffectKind.Strips,
                SlideShowShapeAnimationEffectKind.Wedge,
                SlideShowShapeAnimationEffectKind.Wheel,
            ],
            [SlideShowAnimationRendererRouteKind.DissolveMask] =
                [SlideShowShapeAnimationEffectKind.Dissolve],
            [SlideShowAnimationRendererRouteKind.Flash] =
                [SlideShowShapeAnimationEffectKind.Flash],
            [SlideShowAnimationRendererRouteKind.ScalarTrack] =
            [
                SlideShowShapeAnimationEffectKind.Spiral,
                SlideShowShapeAnimationEffectKind.Swivel,
                SlideShowShapeAnimationEffectKind.Pulse,
                SlideShowShapeAnimationEffectKind.GrowShrink,
                SlideShowShapeAnimationEffectKind.Spin,
                SlideShowShapeAnimationEffectKind.Teeter,
                SlideShowShapeAnimationEffectKind.Blink,
                SlideShowShapeAnimationEffectKind.FlashBulb,
                SlideShowShapeAnimationEffectKind.Flicker,
                SlideShowShapeAnimationEffectKind.ColorPulse,
                SlideShowShapeAnimationEffectKind.ColorWave,
                SlideShowShapeAnimationEffectKind.ChangeColor,
                SlideShowShapeAnimationEffectKind.GrowWithColor,
                SlideShowShapeAnimationEffectKind.Wave,
                SlideShowShapeAnimationEffectKind.Shimmer,
            ],
            [SlideShowAnimationRendererRouteKind.Trajectory] =
            [
                SlideShowShapeAnimationEffectKind.Bounce,
                SlideShowShapeAnimationEffectKind.Float,
                SlideShowShapeAnimationEffectKind.Swoop,
                SlideShowShapeAnimationEffectKind.Boomerang,
            ],
            [SlideShowAnimationRendererRouteKind.Peek] =
                [SlideShowShapeAnimationEffectKind.Peek],
            [SlideShowAnimationRendererRouteKind.Crawl] =
                [SlideShowShapeAnimationEffectKind.Crawl],
            [SlideShowAnimationRendererRouteKind.Zoom] =
                [SlideShowShapeAnimationEffectKind.Zoom],
            [SlideShowAnimationRendererRouteKind.TextStyle] =
            [
                SlideShowShapeAnimationEffectKind.ChangeFontStyle,
                SlideShowShapeAnimationEffectKind.Bold,
                SlideShowShapeAnimationEffectKind.Underline,
            ],
            [SlideShowAnimationRendererRouteKind.FontSize] =
                [SlideShowShapeAnimationEffectKind.ChangeFontSize],
            [SlideShowAnimationRendererRouteKind.LineColor] =
                [SlideShowShapeAnimationEffectKind.ChangeLineColor],
            [SlideShowAnimationRendererRouteKind.FillColor] =
                [SlideShowShapeAnimationEffectKind.ChangeFillColor],
        };

        var actual = Enum.GetValues<SlideShowShapeAnimationEffectKind>()
            .ToDictionary(
                effect => effect,
                effect => SlideShowAnimationRendererRoutePlanner.Build(effect, AnimationKind.Emphasis).Kind);

        actual.Keys.Should().BeEquivalentTo(expected.Values.SelectMany(effects => effects));
        foreach (var route in expected)
        {
            actual.Where(pair => pair.Value == route.Key).Select(pair => pair.Key)
                .Should().BeEquivalentTo(route.Value);
        }
    }

    [Fact]
    public void Build_CarriesInstantVisibilityRevealTimingAndUnknownFallback()
    {
        var hide = SlideShowAnimationRendererRoutePlanner.Build(
            SlideShowShapeAnimationEffectKind.Appear,
            AnimationKind.Exit,
            SlideShowAnimationRevealTiming.OnComplete);
        var fallback = SlideShowAnimationRendererRoutePlanner.Build(
            (SlideShowShapeAnimationEffectKind)int.MaxValue,
            AnimationKind.Exit);

        hide.Kind.Should().Be(SlideShowAnimationRendererRouteKind.Instant);
        hide.InstantVisibility.Should().Be(SlideShowAnimationInstantVisibilityKind.Hide);
        hide.RevealTiming.Should().Be(SlideShowAnimationRevealTiming.OnComplete);
        hide.IsFallback.Should().BeFalse();
        fallback.Kind.Should().Be(SlideShowAnimationRendererRouteKind.Instant);
        fallback.InstantVisibility.Should().Be(SlideShowAnimationInstantVisibilityKind.Show);
        fallback.IsFallback.Should().BeTrue();
    }
}
