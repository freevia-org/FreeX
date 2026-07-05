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
        moved.Segments[0].Should().Match<SlideShowRecordingSlideSegment>(segment =>
            segment.SlideIndex == 0 &&
            segment.StartedAtUtc == started &&
            segment.EndedAtUtc == started.AddMilliseconds(2600) &&
            segment.DurationMs == 2600 &&
            segment.MediaIntent == SlideShowRecordingMediaIntent.Narration &&
            segment.NarrationRequested &&
            !segment.CameraRequested &&
            segment.NarrationCaptured &&
            !segment.CameraCaptured);
        moved.Segments[0].MediaArtifacts.Should().Equal(
            new SlideShowRecordingMediaArtifact(
                SlideShowRecordingMediaArtifactKind.NarrationAudio,
                SlideIndex: 0,
                IsCaptured: true,
                IsDeferred: false,
                "slide-001-narration.m4a",
                "audio/mp4",
                "Narration audio captured for slide 1"));
        moved.LastActions.Select(action => action.Kind).Should().Equal(
            SlideShowRecordingExecutionActionKind.StopNarrationCapture,
            SlideShowRecordingExecutionActionKind.LeaveSlide,
            SlideShowRecordingExecutionActionKind.EnterSlide,
            SlideShowRecordingExecutionActionKind.StartNarrationCapture);
    }

    [Fact]
    public void MoveToSlide_WithDeterministicBackend_StoresPersistableCapturedArtifacts()
    {
        var started = new DateTimeOffset(2026, 7, 5, 13, 0, 0, TimeSpan.Zero);
        var plan = SlideShowPresenterToolPlanner.BuildPlan(
            SlideShowTimingIntent.RecordTimings,
            SlideShowRecordingMediaIntent.NarrationAndMedia);
        var backend = new SlideShowDeterministicRecordingCaptureBackend(
            "Deterministic evidence backend",
            "ppt/media/freep-recordings");
        var state = SlideShowRecordingExecutionPlanner.CreateState(
            plan,
            currentSlideIndex: 0,
            started,
            backend);

        var moved = SlideShowRecordingExecutionPlanner.MoveToSlide(
            state,
            slideIndex: 1,
            started.AddMilliseconds(3450));

        moved.HostCapabilities.HostName.Should().Be("Deterministic evidence backend");
        moved.IsNarrationCaptureActive.Should().BeTrue();
        moved.IsCameraCaptureActive.Should().BeTrue();
        moved.LastActions.Should().OnlyContain(action => !action.IsDeferred);

        var segment = moved.Segments.Should().ContainSingle().Subject;
        segment.NarrationCaptured.Should().BeTrue();
        segment.CameraCaptured.Should().BeTrue();
        segment.MediaArtifacts.Should().HaveCount(2);
        segment.MediaArtifacts.Should().OnlyContain(artifact =>
            artifact.IsCaptured &&
            !artifact.IsDeferred &&
            artifact.IsPersistable &&
            artifact.PayloadBytes != null &&
            artifact.PayloadBytes.Length > 0 &&
            artifact.PayloadBytes.Length == artifact.ContentLengthBytes &&
            artifact.ContentLengthBytes > 0 &&
            artifact.ContentSha256.Length == 64);
        segment.MediaArtifacts.Select(artifact => artifact.PackagePath).Should().Equal(
            "ppt/media/freep-recordings/slide-001-narration.m4a",
            "ppt/media/freep-recordings/slide-001-camera.mp4");
        segment.MediaArtifacts.Select(artifact => artifact.ContentType).Should().Equal(
            "audio/mp4",
            "video/mp4");
        segment.MediaArtifacts.Select(artifact => artifact.StatusText).Should().OnlyContain(
            text => text.Contains("Deterministic evidence backend") &&
                text.Contains("ppt/media/freep-recordings"));
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

    [Fact]
    public void EndSession_EmitsDeferredMediaArtifactDescriptorsWhenCaptureAdaptersAreMissing()
    {
        var started = new DateTimeOffset(2026, 7, 4, 9, 0, 0, TimeSpan.Zero);
        var plan = SlideShowPresenterToolPlanner.BuildPlan(
            SlideShowTimingIntent.RecordTimings,
            SlideShowRecordingMediaIntent.NarrationAndMedia);
        var state = SlideShowRecordingExecutionPlanner.CreateState(
            plan,
            currentSlideIndex: 2,
            started,
            SlideShowRecordingHostCapabilities.Deferred("Avalonia slideshow"));

        var ended = SlideShowRecordingExecutionPlanner.EndSession(
            state,
            started.AddMilliseconds(1500));

        ended.Segments.Should().ContainSingle();
        ended.Segments[0].MediaArtifacts.Should().Equal(
            new SlideShowRecordingMediaArtifact(
                SlideShowRecordingMediaArtifactKind.NarrationAudio,
                SlideIndex: 2,
                IsCaptured: false,
                IsDeferred: true,
                "slide-003-narration.m4a",
                "audio/mp4",
                "Avalonia slideshow: Recording capture adapter is not registered for this host."),
            new SlideShowRecordingMediaArtifact(
                SlideShowRecordingMediaArtifactKind.CameraVideo,
                SlideIndex: 2,
                IsCaptured: false,
                IsDeferred: true,
                "slide-003-camera.mp4",
                "video/mp4",
                "Avalonia slideshow: Recording capture adapter is not registered for this host."));
    }
}
