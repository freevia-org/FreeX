using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowAnimationEffectTrackPlannerTests
{
    [Theory]
    [InlineData(AnimationPreset.Pulse, 2)]
    [InlineData(AnimationPreset.Grow, 2)]
    [InlineData(AnimationPreset.Spin, 1)]
    [InlineData(AnimationPreset.Spiral, 1)]
    [InlineData(AnimationPreset.Swivel, 2)]
    [InlineData(AnimationPreset.Teeter, 1)]
    [InlineData(AnimationPreset.Blink, 1)]
    [InlineData(AnimationPreset.FlashBulb, 1)]
    [InlineData(AnimationPreset.Flicker, 1)]
    [InlineData(AnimationPreset.Wave, 1)]
    [InlineData(AnimationPreset.ColorPulse, 1)]
    [InlineData(AnimationPreset.ChangeColor, 1)]
    [InlineData(AnimationPreset.ColorWave, 1)]
    [InlineData(AnimationPreset.GrowWithColor, 3)]
    [InlineData(AnimationPreset.Shimmer, 1)]
    public void Build_ProjectsSupportedEffectsIntoPortableScalarTracks(
        AnimationPreset preset,
        int expectedTrackCount)
    {
        var playback = Playback(preset);

        var plan = SlideShowAnimationEffectTrackPlanner.Build(playback);

        plan.Should().NotBeNull();
        plan!.EffectKind.Should().Be(playback.EffectKind);
        plan.DelayMs.Should().Be(25);
        plan.DurationMs.Should().Be(400);
        plan.Tracks.Should().HaveCount(expectedTrackCount);
        plan.Tracks.Should().OnlyContain(track =>
            track.KeyFrames.Count >= SlideShowAnimationEffectTrackPlanner.StoryboardFrameCount + 1);
    }

    [Fact]
    public void Sample_PreservesEstablishedFramePlannerContracts()
    {
        var blink = Playback(AnimationPreset.Blink);
        var teeter = Playback(AnimationPreset.Teeter);
        var wave = Playback(AnimationPreset.Wave);

        SlideShowAnimationEffectTrackPlanner.Sample(blink, 0.25).Opacity.Should().Be(0.15);
        SlideShowAnimationEffectTrackPlanner.Sample(teeter, 0.375).RotationDegrees
            .Should().BeApproximately(-10, 1e-9);
        SlideShowAnimationEffectTrackPlanner.Sample(wave, 0.125).TranslateXFactor
            .Should().BeApproximately(0.00625, 1e-9);
    }

    [Fact]
    public void TrackSampling_UsesDiscreteAndLinearInterpolationAsPlanned()
    {
        var blink = SlideShowAnimationEffectTrackPlanner.Build(Playback(AnimationPreset.Blink))!;
        var opacity = blink.FindTrack(SlideShowAnimationScalarPropertyKind.Opacity)!;
        SlideShowAnimationEffectTrackPlanner.Sample(opacity, 0.249).Should().Be(1);
        SlideShowAnimationEffectTrackPlanner.Sample(opacity, 0.25).Should().Be(0.15);

        var colorWave = SlideShowAnimationEffectTrackPlanner.Build(Playback(AnimationPreset.ColorWave))!;
        var waveOpacity = colorWave.FindTrack(SlideShowAnimationScalarPropertyKind.Opacity)!;
        SlideShowAnimationEffectTrackPlanner.Sample(waveOpacity, 0.25).Should().BeApproximately(0.65, 1e-9);
        colorWave.AddAuthoredColorOverlay.Should().BeTrue();
    }

    [Fact]
    public void Build_LeavesNativeOnlyEffectsWithoutScalarTracks()
    {
        SlideShowAnimationEffectTrackPlanner.Build(Playback(AnimationPreset.Wipe))
            .Should().BeNull();
        SlideShowAnimationEffectTrackPlanner.ResolveTimerStepCount(160).Should().Be(10);
    }

    private static SlideShowShapeAnimationPlaybackPlan Playback(AnimationPreset preset) =>
        SlideShowPlaybackPlanner.PlanShapeAnimation(
            new ShapeAnimation
            {
                ShapeId = 7,
                Kind = AnimationKind.Emphasis,
                Preset = preset,
                DurationMs = 400
            },
            startDelayMs: 25);
}
