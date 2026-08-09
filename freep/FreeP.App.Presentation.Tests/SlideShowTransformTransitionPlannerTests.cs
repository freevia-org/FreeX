namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowTransformTransitionPlannerTests
{
    [Theory]
    [InlineData(SlideShowTransitionPlaybackActionKind.Zoom)]
    [InlineData(SlideShowTransitionPlaybackActionKind.Pan)]
    [InlineData(SlideShowTransitionPlaybackActionKind.Gallery)]
    [InlineData(SlideShowTransitionPlaybackActionKind.Conveyor)]
    [InlineData(SlideShowTransitionPlaybackActionKind.Window)]
    public void Build_ProjectsTransformTransitionsIntoNormalizedSurfaceStates(
        SlideShowTransitionPlaybackActionKind actionKind)
    {
        var plan = SlideShowTransformTransitionPlanner.Build(Playback(actionKind));

        plan.ActionKind.Should().Be(actionKind);
        plan.ResolveIncoming(1, 960, 540).Scale.Should().Be(1);
    }

    [Fact]
    public void Gallery_ResolvesPairedIncomingAndOutgoingGeometry()
    {
        var plan = SlideShowTransformTransitionPlanner.Build(
            Playback(SlideShowTransitionPlaybackActionKind.Gallery));

        var incoming = plan.ResolveIncoming(0, 1000, 500);
        incoming.Scale.Should().Be(SlideShowPlaybackPlanner.GalleryStartScale);
        incoming.TranslateX.Should().Be(550);
        incoming.TranslateY.Should().Be(-275);

        var outgoing = plan.ResolveOutgoing(1, 1000, 500);
        outgoing.Scale.Should().Be(SlideShowPlaybackPlanner.GalleryOutgoingEndScale);
        outgoing.TranslateX.Should().Be(550);
        outgoing.TranslateY.Should().Be(-275);
    }

    [Fact]
    public void Conveyor_OwnsCrossAxisTiltAndOutgoingState()
    {
        var plan = SlideShowTransformTransitionPlanner.Build(
            Playback(
                SlideShowTransitionPlaybackActionKind.Conveyor,
                incomingOffsetX: 1,
                incomingOffsetY: 0));

        var incoming = plan.ResolveIncoming(0, 1000, 500);
        incoming.TranslateX.Should().Be(1000);
        incoming.TranslateY.Should().Be(-40);
        incoming.RotationDegrees.Should().Be(-SlideShowPlaybackPlanner.ConveyorTiltDegrees);

        var outgoing = plan.ResolveOutgoing(1, 1000, 500);
        outgoing.RotationDegrees.Should().Be(SlideShowPlaybackPlanner.ConveyorTiltDegrees);

        var vertical = SlideShowTransformTransitionPlanner.Build(
            Playback(
                SlideShowTransitionPlaybackActionKind.Conveyor,
                incomingOffsetX: 0,
                incomingOffsetY: -1)).ResolveIncoming(0, 1000, 500);
        vertical.TranslateX.Should().Be(-80);
        vertical.TranslateY.Should().Be(-500);
        vertical.RotationDegrees.Should().Be(-SlideShowPlaybackPlanner.ConveyorTiltDegrees);
    }

    [Fact]
    public void Window_OwnsScaleAndClipOpeningProgress()
    {
        var plan = SlideShowTransformTransitionPlanner.Build(
            Playback(SlideShowTransitionPlaybackActionKind.Window));

        var midpoint = plan.ResolveIncoming(0.5, 960, 540);
        midpoint.Scale.Should().BeApproximately(
            (SlideShowPlaybackPlanner.WindowStartScale + 1) / 2,
            1e-9);
        midpoint.ClipOpening.Should().BeApproximately(
            (SlideShowPlaybackPlanner.WindowInitialOpenFactor + 1) / 2,
            1e-9);
    }

    private static SlideShowTransitionPlaybackPlan Playback(
        SlideShowTransitionPlaybackActionKind actionKind,
        double incomingOffsetX = 1,
        double incomingOffsetY = -1) =>
        new(
            actionKind,
            DurationMs: 400,
            IncomingOffsetX: incomingOffsetX,
            IncomingOffsetY: incomingOffsetY,
            SourceKind: SlideShowTransitionPlaybackKind.Zoom,
            SplitHorizontal: false,
            SplitOut: false,
            BlindsHorizontal: false,
            RandomBarsHorizontal: false,
            StripsSlopeDown: false,
            WheelSpokeCount: 4,
            WheelReverse: false,
            ZoomIn: true,
            BoxExpandsFromCenter: false,
            ResolvedKind: TransitionKind.Fade,
            RandomSeed: null,
            EffectiveTransition: new SlideTransition { Kind = TransitionKind.Fade });
}
