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

    [Fact]
    public void PlanRecordingTiming_UsesHostReadinessForAvailableCaptureStreams()
    {
        var readiness = SlideShowRecordingCaptureAdapterReadiness.FromDevices(
            "Windows slideshow",
            "Windows Runtime capture",
            new[]
            {
                new SlideShowRecordingCaptureDeviceDescriptor(
                    SlideShowRecordingCaptureDeviceKind.Microphone,
                    "mic-1",
                    "Microphone",
                    IsDefault: true,
                    IsAvailable: true,
                    "audio/mp4"),
                new SlideShowRecordingCaptureDeviceDescriptor(
                    SlideShowRecordingCaptureDeviceKind.Camera,
                    "camera-1",
                    "Camera",
                    IsDefault: true,
                    IsAvailable: true,
                    "video/mp4"),
            });

        var plan = SlideShowPresenterToolPlanner.PlanRecordingTiming(
            SlideShowTimingIntent.RecordTimings,
            SlideShowRecordingMediaIntent.NarrationAndMedia,
            readiness);

        plan.NarrationCapture.IsAvailable.Should().BeTrue();
        plan.NarrationCapture.IsDeferred.Should().BeFalse();
        plan.MediaCapture.IsAvailable.Should().BeTrue();
        plan.MediaCapture.IsDeferred.Should().BeFalse();
        plan.NarrationCapture.Reason.Should().BeEmpty();
        plan.StatusText.Should().Be("Record timings with narration and camera capture");
    }

    [Fact]
    public void PlanRecordingTiming_ReportsHostPermissionFailureInsteadOfGenericDeferral()
    {
        var readiness = SlideShowRecordingCaptureAdapterReadiness.FromDevices(
            "Avalonia slideshow",
            "Windows Runtime capture",
            Array.Empty<SlideShowRecordingCaptureDeviceDescriptor>(),
            requiresUserPermission: true,
            unavailableReason: "Camera permission was denied by the operating system.");

        var plan = SlideShowPresenterToolPlanner.PlanRecordingTiming(
            SlideShowTimingIntent.RecordTimings,
            SlideShowRecordingMediaIntent.NarrationAndMedia,
            readiness);

        plan.NarrationCapture.IsDeferred.Should().BeTrue();
        plan.MediaCapture.IsDeferred.Should().BeTrue();
        plan.NarrationCapture.Reason.Should().Contain("Camera permission was denied");
        plan.NarrationCapture.Reason.Should().Contain("Permission may be required");
        plan.MediaCapture.Reason.Should().Be(plan.NarrationCapture.Reason);
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

    [Fact]
    public void BuildPlan_EmitsSharedWorkflowActionsForRecordingAndInkExecutionIntent()
    {
        var plan = SlideShowPresenterToolPlanner.BuildPlan(
            SlideShowTimingIntent.RecordTimings,
            SlideShowRecordingMediaIntent.NarrationAndMedia,
            SlideShowPresenterPointerMode.Highlighter,
            "#ffee00",
            8,
            SlideShowInkRetentionDecision.ClearInk);

        plan.WorkflowActions.Select(action => action.Kind).Should().Equal(
            new[]
            {
                SlideShowPresenterWorkflowActionKind.StartElapsedClock,
                SlideShowPresenterWorkflowActionKind.TrackPerSlideTiming,
                SlideShowPresenterWorkflowActionKind.PersistPerSlideTiming,
                SlideShowPresenterWorkflowActionKind.RequestNarrationCapture,
                SlideShowPresenterWorkflowActionKind.RequestMediaCapture,
                SlideShowPresenterWorkflowActionKind.SelectPointerMode,
                SlideShowPresenterWorkflowActionKind.ConfigureInkStroke,
                SlideShowPresenterWorkflowActionKind.ClearInkOnExit,
            });
        plan.WorkflowActions
            .Where(action => action.IsDeferred)
            .Select(action => action.Kind)
            .Should().Equal(
                SlideShowPresenterWorkflowActionKind.RequestNarrationCapture,
                SlideShowPresenterWorkflowActionKind.RequestMediaCapture);
        plan.WorkflowActions.Single(action =>
                action.Kind == SlideShowPresenterWorkflowActionKind.ConfigureInkStroke)
            .StatusText.Should().Contain("#FFEE00");
    }

    [Fact]
    public void BuildPlan_EmitsSharedCommandStatesForPresenterRecordingPointerAndInkChoices()
    {
        var plan = SlideShowPresenterToolPlanner.BuildPlan(
            SlideShowTimingIntent.RecordTimings,
            SlideShowRecordingMediaIntent.NarrationAndMedia,
            SlideShowPresenterPointerMode.Highlighter,
            "#ffee00",
            8,
            SlideShowInkRetentionDecision.ClearInk);

        plan.CommandStates.Select(command => command.CommandId).Should().Equal(
            SlideShowPresenterToolPlanner.RehearseTimingsCommandId,
            SlideShowPresenterToolPlanner.RecordTimingsCommandId,
            SlideShowPresenterToolPlanner.NarrationCommandId,
            SlideShowPresenterToolPlanner.NarrationAndMediaCommandId,
            SlideShowPresenterToolPlanner.ArrowPointerCommandId,
            SlideShowPresenterToolPlanner.LaserPointerCommandId,
            SlideShowPresenterToolPlanner.PenPointerCommandId,
            SlideShowPresenterToolPlanner.HighlighterPointerCommandId,
            SlideShowPresenterToolPlanner.EraserPointerCommandId,
            SlideShowPresenterToolPlanner.KeepInkCommandId,
            SlideShowPresenterToolPlanner.ClearInkCommandId);
        plan.CommandStates.Where(command => command.IsChecked).Select(command => command.CommandId)
            .Should().Equal(
                SlideShowPresenterToolPlanner.RecordTimingsCommandId,
                SlideShowPresenterToolPlanner.NarrationAndMediaCommandId,
                SlideShowPresenterToolPlanner.HighlighterPointerCommandId,
                SlideShowPresenterToolPlanner.ClearInkCommandId);
        plan.CommandStates.Single(command =>
                command.CommandId == SlideShowPresenterToolPlanner.NarrationAndMediaCommandId)
            .Should().Match<SlideShowPresenterCommandState>(command =>
                command.IsEnabled &&
                command.IsDeferred &&
                command.StatusText == SlideShowPresenterToolPlanner.DeferredCaptureReason);
        plan.CommandStates.Single(command =>
                command.CommandId == SlideShowPresenterToolPlanner.HighlighterPointerCommandId)
            .StatusText.Should().Be("Highlighter; clear ink on exit");
    }

    [Fact]
    public void TimingRecorder_RecordTimings_PersistsAdvanceAfterAndPreservesTransitionFields()
    {
        var started = new DateTimeOffset(2026, 7, 3, 10, 0, 0, TimeSpan.Zero);
        var state = SlideShowTimingRecorderPlanner.CreateState(0, started);
        var plan = SlideShowPresenterToolPlanner.BuildPlan(SlideShowTimingIntent.RecordTimings);
        var pres = Presentation.CreateEmpty();
        pres.Slides[0].Transition = new SlideTransition
        {
            Kind = TransitionKind.Fade,
            Direction = TransitionDirection.Left,
            DurationMs = 700,
            AdvanceOnClick = false,
        };

        var result = SlideShowTimingRecorderPlanner.LeaveCurrentSlide(
            state,
            plan,
            started.AddMilliseconds(2500));
        SlideShowTimingRecorderPlanner.ApplyTimings(pres, result.Mutations);

        result.Mutations.Should().ContainSingle();
        result.Mutations[0].Should().Be(new SlideShowSlideTimingMutation(
            0,
            2500,
            ShouldPersist: true,
            SlideShowTimingIntent.RecordTimings));
        result.State.CurrentSlideIndex.Should().BeNull();
        result.State.RecordedTimings.Should().ContainSingle();
        var transition = pres.Slides[0].Transition;
        transition.Should().NotBeNull();
        transition!.Kind.Should().Be(TransitionKind.Fade);
        transition.Direction.Should().Be(TransitionDirection.Left);
        transition.DurationMs.Should().Be(700);
        transition.AdvanceOnClick.Should().BeFalse();
        transition.AdvanceAfterMs.Should().Be(2500);
    }

    [Fact]
    public void TimingRecorder_RehearseTimings_TracksButDoesNotPersistAdvanceAfter()
    {
        var started = new DateTimeOffset(2026, 7, 3, 10, 0, 0, TimeSpan.Zero);
        var state = SlideShowTimingRecorderPlanner.CreateState(0, started);
        var plan = SlideShowPresenterToolPlanner.BuildPlan(SlideShowTimingIntent.RehearseTimings);
        var pres = Presentation.CreateEmpty();

        var result = SlideShowTimingRecorderPlanner.LeaveCurrentSlide(
            state,
            plan,
            started.AddMilliseconds(1750));
        SlideShowTimingRecorderPlanner.ApplyTimings(pres, result.Mutations);

        result.Mutations.Should().ContainSingle();
        result.Mutations[0].AdvanceAfterMs.Should().Be(1750);
        result.Mutations[0].ShouldPersist.Should().BeFalse();
        pres.Slides[0].Transition.Should().BeNull();
    }

    [Fact]
    public void TimingRecorder_ClampsElapsedMillisecondsBeforeMutation()
    {
        var started = new DateTimeOffset(2026, 7, 3, 10, 0, 0, TimeSpan.Zero);
        var plan = SlideShowPresenterToolPlanner.BuildPlan(SlideShowTimingIntent.RecordTimings);

        var negative = SlideShowTimingRecorderPlanner.LeaveCurrentSlide(
            SlideShowTimingRecorderPlanner.CreateState(0, started),
            plan,
            started.AddMilliseconds(-100));
        var tooLarge = SlideShowTimingRecorderPlanner.LeaveCurrentSlide(
            SlideShowTimingRecorderPlanner.CreateState(0, started),
            plan,
            started.AddDays(2));

        negative.Mutations[0].AdvanceAfterMs.Should().Be(SlideShowTimingRecorderPlanner.MinRecordedTimingMs);
        tooLarge.Mutations[0].AdvanceAfterMs.Should().Be(SlideShowTimingRecorderPlanner.MaxRecordedTimingMs);
    }
}
