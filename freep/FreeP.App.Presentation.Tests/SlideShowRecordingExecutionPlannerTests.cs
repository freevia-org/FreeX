using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowRecordingExecutionPlannerTests
{
    [Fact]
    public void CreateState_WhenRecordingRequested_StartsSessionAndCaptureActions()
    {
        var started = new DateTimeOffset(2026, 7, 4, 9, 0, 0, TimeSpan.Zero);
        var plan = SlideShowPresenterToolPlanner.BuildPlan(
            SlideShowTimingIntent.RecordTimings,
            SlideShowRecordingMediaIntent.NarrationAndMedia);
        var capabilities = new SlideShowRecordingHostCapabilities(
            "Test host",
            CanCaptureNarration: true,
            CanCaptureCamera: true,
            UnavailableReason: string.Empty);

        var state = SlideShowRecordingExecutionPlanner.CreateState(
            plan,
            currentSlideIndex: 1,
            started,
            capabilities);

        state.IsSessionActive.Should().BeTrue();
        state.CurrentSlideIndex.Should().Be(1);
        state.CurrentSlideStartedAtUtc.Should().Be(started);
        state.IsNarrationCaptureActive.Should().BeTrue();
        state.IsCameraCaptureActive.Should().BeTrue();
        state.LastActions.Select(action => action.Kind).Should().Equal(
            SlideShowRecordingExecutionActionKind.StartSession,
            SlideShowRecordingExecutionActionKind.EnterSlide,
            SlideShowRecordingExecutionActionKind.StartNarrationCapture,
            SlideShowRecordingExecutionActionKind.StartCameraCapture);
        state.LastActions.Should().OnlyContain(action => !action.IsDeferred);
    }

    [Fact]
    public void MoveToSlide_StoresCompletedSegmentAndBeginsNextSlide()
    {
        var started = new DateTimeOffset(2026, 7, 4, 9, 0, 0, TimeSpan.Zero);
        var plan = SlideShowPresenterToolPlanner.BuildPlan(
            SlideShowTimingIntent.RecordTimings,
            SlideShowRecordingMediaIntent.Narration);
        var capabilities = new SlideShowRecordingHostCapabilities(
            "Test host",
            CanCaptureNarration: true,
            CanCaptureCamera: false,
            UnavailableReason: "camera unavailable");
        var state = SlideShowRecordingExecutionPlanner.CreateState(
            plan,
            currentSlideIndex: 0,
            started,
            capabilities);

        var moved = SlideShowRecordingExecutionPlanner.MoveToSlide(
            state,
            slideIndex: 1,
            started.AddMilliseconds(2600));

        moved.CurrentSlideIndex.Should().Be(1);
        moved.Segments.Should().ContainSingle();
        moved.Segments[0].Should().Be(new SlideShowRecordingSlideSegment(
            0,
            started,
            started.AddMilliseconds(2600),
            2600,
            SlideShowRecordingMediaIntent.Narration,
            NarrationRequested: true,
            CameraRequested: false,
            NarrationCaptured: true,
            CameraCaptured: false));
        moved.LastActions.Select(action => action.Kind).Should().Equal(
            SlideShowRecordingExecutionActionKind.StopNarrationCapture,
            SlideShowRecordingExecutionActionKind.LeaveSlide,
            SlideShowRecordingExecutionActionKind.EnterSlide,
            SlideShowRecordingExecutionActionKind.StartNarrationCapture);
    }

    [Fact]
    public void CreateState_DeferredHostCapabilitiesEmitCaptureUnavailableActions()
    {
        var started = new DateTimeOffset(2026, 7, 4, 9, 0, 0, TimeSpan.Zero);
        var plan = SlideShowPresenterToolPlanner.BuildPlan(
            SlideShowTimingIntent.RecordTimings,
            SlideShowRecordingMediaIntent.NarrationAndMedia);

        var state = SlideShowRecordingExecutionPlanner.CreateState(
            plan,
            currentSlideIndex: 0,
            started,
            SlideShowRecordingHostCapabilities.Deferred("WPF"));

        state.IsNarrationCaptureActive.Should().BeFalse();
        state.IsCameraCaptureActive.Should().BeFalse();
        state.LastActions.Where(action => action.IsDeferred)
            .Select(action => action.Kind)
            .Should().Equal(
                SlideShowRecordingExecutionActionKind.CaptureUnavailable,
                SlideShowRecordingExecutionActionKind.CaptureUnavailable);
        state.LastActions.Where(action => action.IsDeferred)
            .Should().OnlyContain(action => action.StatusText.Contains("WPF"));
    }

    [Fact]
    public void EndSession_FinalizesCurrentSlideAndStopsSession()
    {
        var started = new DateTimeOffset(2026, 7, 4, 9, 0, 0, TimeSpan.Zero);
        var plan = SlideShowPresenterToolPlanner.BuildPlan(SlideShowTimingIntent.RecordTimings);
        var state = SlideShowRecordingExecutionPlanner.CreateState(
            plan,
            currentSlideIndex: 0,
            started);

        var ended = SlideShowRecordingExecutionPlanner.EndSession(
            state,
            started.AddMilliseconds(1250));

        ended.IsSessionActive.Should().BeFalse();
        ended.CurrentSlideIndex.Should().BeNull();
        ended.CurrentSlideStartedAtUtc.Should().BeNull();
        ended.Segments.Should().ContainSingle();
        ended.Segments[0].DurationMs.Should().Be(1250);
        ended.LastActions.Select(action => action.Kind).Should().Equal(
            SlideShowRecordingExecutionActionKind.LeaveSlide,
            SlideShowRecordingExecutionActionKind.StopSession);
    }

    [Fact]
    public void ApplyToolPlan_RestartsSessionWhenRecordingIntentChanges()
    {
        var started = new DateTimeOffset(2026, 7, 4, 9, 0, 0, TimeSpan.Zero);
        var rehearse = SlideShowPresenterToolPlanner.BuildPlan(SlideShowTimingIntent.RehearseTimings);
        var recordWithNarration = SlideShowPresenterToolPlanner.BuildPlan(
            SlideShowTimingIntent.RecordTimings,
            SlideShowRecordingMediaIntent.Narration);
        var state = SlideShowRecordingExecutionPlanner.CreateState(
            rehearse,
            currentSlideIndex: 0,
            started);

        var restarted = SlideShowRecordingExecutionPlanner.ApplyToolPlan(
            state,
            recordWithNarration,
            currentSlideIndex: 0,
            started.AddMilliseconds(500));

        restarted.IsSessionActive.Should().BeTrue();
        restarted.RecordingPlan.MediaIntent.Should().Be(SlideShowRecordingMediaIntent.Narration);
        restarted.Segments.Should().ContainSingle();
        restarted.Segments[0].DurationMs.Should().Be(500);
        restarted.LastActions.Select(action => action.Kind).Should().Equal(
            SlideShowRecordingExecutionActionKind.StartSession,
            SlideShowRecordingExecutionActionKind.EnterSlide,
            SlideShowRecordingExecutionActionKind.CaptureUnavailable);
    }
}
