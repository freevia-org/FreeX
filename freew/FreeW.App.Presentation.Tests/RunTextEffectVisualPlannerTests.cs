using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class RunTextEffectVisualPlannerTests
{
    private static readonly ThemeColorScheme Scheme = DocumentTheme.Default.ColorScheme;
    private const double DipPerPoint = 96.0 / 72.0;

    [Fact]
    public void NoEffects_OrOnlyTypographyOr3D_PlanNothingDrawable()
    {
        RunTextEffectVisualPlanner.Build(null, Scheme).Should().BeNull();
        RunTextEffectVisualPlanner.Build(new RunTextEffects { ContextualAlternates = "<alts/>" }, Scheme).Should().BeNull();
        RunTextEffectVisualPlanner.Build(new RunTextEffects { Scene3D = "<scene/>", Props3D = "<props/>" }, Scheme).Should().BeNull();
        RunTextEffectVisualPlanner.Build(new RunTextEffects { Glow = "<not-an-effect/>" }, Scheme).Should().BeNull();
    }

    [Fact]
    public void SolidFill_ResolvesThemeColourAndTransparency_WithoutNeedingAnOverlay()
    {
        var plan = RunTextEffectVisualPlanner.Build(
            ((RunTextEffects?)null).WithFill(TextFillEffect.Solid(TextEffectColor.FromScheme("accent4", 50))),
            Scheme)!;

        plan.Fill!.Solid!.Value.RgbHex.Should().Be("#FFC000");
        plan.Fill.Solid.Value.Opacity.Should().BeApproximately(0.5, 0.01);
        plan.NeedsOverlay.Should().BeFalse();
    }

    [Fact]
    public void GradientFill_KeepsOrderedStopsAndAngle()
    {
        var plan = RunTextEffectVisualPlanner.Build(
            ((RunTextEffects?)null).WithFill(TextFillEffect.LinearGradient(
                90,
                new TextGradientStop(100000, TextEffectColor.FromRgb("0000FF")),
                new TextGradientStop(0, TextEffectColor.FromRgb("FF0000")))),
            Scheme)!;

        plan.Fill!.Solid.Should().BeNull();
        plan.Fill.AngleDegrees.Should().Be(90);
        plan.Fill.IsRadial.Should().BeFalse();
        plan.Fill.Stops.Select(stop => (stop.Offset, stop.Color.RgbHex)).Should().Equal((0d, "#FF0000"), (1d, "#0000FF"));
    }

    [Fact]
    public void Outline_ConvertsWidthDashAndFill_AndInvisibleOutlinesAreDropped()
    {
        var plan = RunTextEffectVisualPlanner.Build(
            ((RunTextEffects?)null).WithOutline(new TextOutlineEffect
            {
                WidthEmu = TextEffectUnits.PointsToEmu(1.5),
                Fill = TextFillEffect.Solid(TextEffectColor.FromRgb("00B050")),
                Dash = "dashDot",
                Join = TextOutlineJoin.Miter,
            }),
            Scheme)!;

        plan.Outline!.WidthDip.Should().BeApproximately(1.5 * DipPerPoint, 1e-9);
        plan.Outline.Fill.Solid!.Value.RgbHex.Should().Be("#00B050");
        plan.Outline.DashPattern.Should().Equal(4, 3, 1, 3);
        plan.Outline.RoundCap.Should().BeTrue();
        plan.Outline.Join.Should().Be(TextOutlineJoin.Miter);
        plan.NeedsOverlay.Should().BeTrue();

        RunTextEffectVisualPlanner.Build(
            ((RunTextEffects?)null).WithOutline(new TextOutlineEffect { Fill = TextFillEffect.NoFill }),
            Scheme).Should().BeNull();
    }

    [Fact]
    public void Shadow_ConvertsPolarOffsetToDeviceVector()
    {
        var plan = RunTextEffectVisualPlanner.Build(
            ((RunTextEffects?)null).WithShadow(new TextShadowEffect
            {
                BlurRadiusEmu = TextEffectUnits.PointsToEmu(3),
                DistanceEmu = TextEffectUnits.PointsToEmu(6),
                DirectionAngle = TextEffectUnits.DegreesToAngle(90),
                Color = TextEffectColor.FromRgb("000000", 60),
            }),
            Scheme)!;

        plan.Shadow!.OffsetXDip.Should().BeApproximately(0, 1e-6);
        plan.Shadow.OffsetYDip.Should().BeApproximately(6 * DipPerPoint, 1e-6);
        plan.Shadow.BlurDip.Should().BeApproximately(3 * DipPerPoint, 1e-9);
        plan.Shadow.Color.Opacity.Should().BeApproximately(0.4, 0.01);
    }

    [Fact]
    public void GlowAndReflection_ConvertExtentsAndFade()
    {
        var plan = RunTextEffectVisualPlanner.Build(
            ((RunTextEffects?)null)
                .WithGlow(new TextGlowEffect(TextEffectUnits.PointsToEmu(8), TextEffectColor.FromScheme("accent5", 60)))
                .WithReflection(new TextReflectionEffect
                {
                    BlurRadiusEmu = 6350,
                    StartOpacity = 50000,
                    EndOpacity = 300,
                    EndPosition = 55000,
                    DistanceEmu = TextEffectUnits.PointsToEmu(4),
                }),
            Scheme)!;

        plan.Glow!.RadiusDip.Should().BeApproximately(8 * DipPerPoint, 1e-9);
        plan.Glow.Color.RgbHex.Should().Be("#5B9BD5");
        plan.Reflection!.StartOpacity.Should().Be(0.5);
        plan.Reflection.EndOpacity.Should().Be(0.003);
        plan.Reflection.EndOffset.Should().Be(0.55);
        plan.Reflection.DistanceDip.Should().BeApproximately(4 * DipPerPoint, 1e-9);
    }

    [Fact]
    public void HostileExtents_AreCapped_AndFullyTransparentEffectsDropped()
    {
        var plan = RunTextEffectVisualPlanner.Build(
            ((RunTextEffects?)null)
                .WithGlow(new TextGlowEffect(27273042316900, TextEffectColor.FromRgb("FF0000")))
                .WithShadow(new TextShadowEffect { Color = TextEffectColor.FromRgb("000000", 100) }),
            Scheme)!;

        plan.Glow!.RadiusDip.Should().Be(RunTextEffectVisualPlanner.MaxExtentDip);
        plan.Shadow.Should().BeNull();
    }
}
