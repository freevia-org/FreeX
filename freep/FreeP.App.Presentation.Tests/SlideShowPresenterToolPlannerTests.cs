using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowPresenterToolPlannerTests
{
    [Fact]
    public void PlanRecordingTiming_ModelsRehearseAndRecordWithoutLocalCapture()
    {
        var rehearse = SlideShowPresenterToolPlanner.PlanRecordingTiming(
            SlideShowTimingIntent.RehearseTimings,
            SlideShowRecordingMediaIntent.None);

        rehearse.ShouldTrackElapsed.Should().BeTrue();
        rehearse.ShouldTrackPerSlideTimings.Should().BeTrue();
        rehearse.ShouldPersistTimings.Should().BeFalse();
        rehearse.NarrationCapture.IsDeferred.Should().BeFalse();
        rehearse.MediaCapture.IsDeferred.Should().BeFalse();

        var record = SlideShowPresenterToolPlanner.PlanRecordingTiming(
            SlideShowTimingIntent.RecordTimings,
            SlideShowRecordingMediaIntent.NarrationAndMedia);

        record.ShouldTrackPerSlideTimings.Should().BeTrue();
        record.ShouldPersistTimings.Should().BeTrue();
        record.IsNarrationRequested.Should().BeTrue();
        record.IsMediaCaptureRequested.Should().BeTrue();
        record.NarrationCapture.IsDeferred.Should().BeTrue();
        record.MediaCapture.IsDeferred.Should().BeTrue();
        record.NarrationCapture.Reason.Should().Contain("deferred");
        record.MediaCapture.Reason.Should().Contain("deferred");
    }

    [Theory]
    [InlineData(SlideShowPresenterPointerMode.Arrow, false, false, false)]
    [InlineData(SlideShowPresenterPointerMode.LaserPointer, false, true, false)]
    [InlineData(SlideShowPresenterPointerMode.Pen, true, false, false)]
    [InlineData(SlideShowPresenterPointerMode.Highlighter, true, false, false)]
    [InlineData(SlideShowPresenterPointerMode.Eraser, false, false, true)]
    public void PlanPointerInk_MapsPointerModesToSharedInkIntent(
        SlideShowPresenterPointerMode mode,
        bool usesInk,
        bool usesLaser,
        bool usesEraser)
    {
        var plan = SlideShowPresenterToolPlanner.PlanPointerInk(
            mode,
            "00aaee",
            inkThicknessDip: 128,
            SlideShowInkRetentionDecision.ClearInk);

        plan.PointerMode.Should().Be(mode);
        plan.UsesInkStroke.Should().Be(usesInk);
        plan.UsesLaserOverlay.Should().Be(usesLaser);
        plan.UsesEraser.Should().Be(usesEraser);
        plan.InkState.ColorHex.Should().Be("#00AAEE");
        plan.InkState.ThicknessDip.Should().Be(SlideShowPresenterToolPlanner.MaxInkThicknessDip);
        plan.InkRetentionDecision.Should().Be(SlideShowInkRetentionDecision.ClearInk);
    }

    [Fact]
    public void PlanPointerInk_UsesSensibleDefaultsWhenInputIsInvalid()
    {
        var highlighter = SlideShowPresenterToolPlanner.PlanPointerInk(
            SlideShowPresenterPointerMode.Highlighter,
            "not-a-color",
            inkThicknessDip: 0,
            SlideShowInkRetentionDecision.KeepInk);

        highlighter.InkState.ColorHex.Should().Be("#FFFF00");
        highlighter.InkState.ThicknessDip.Should().Be(12);
        highlighter.InkState.Opacity.Should().Be(0.45);
    }

    [Fact]
    public void BuildPlan_CombinesRecordingAndPointerIntent()
    {
        var plan = SlideShowPresenterToolPlanner.BuildPlan(
            SlideShowTimingIntent.RecordTimings,
            SlideShowRecordingMediaIntent.Narration,
            SlideShowPresenterPointerMode.Pen,
            "#123456",
            4,
            SlideShowInkRetentionDecision.KeepInk);

        plan.Recording.TimingIntent.Should().Be(SlideShowTimingIntent.RecordTimings);
        plan.Recording.NarrationCapture.IsDeferred.Should().BeTrue();
        plan.PointerInk.PointerMode.Should().Be(SlideShowPresenterPointerMode.Pen);
        plan.PointerInk.InkState.ColorHex.Should().Be("#123456");
        plan.PointerInk.InkRetentionDecision.Should().Be(SlideShowInkRetentionDecision.KeepInk);
    }
}
