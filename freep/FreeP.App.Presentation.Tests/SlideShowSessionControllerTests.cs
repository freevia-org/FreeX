using FreeP.App.Compositor;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowSessionControllerTests
{
    [Fact]
    public void SessionController_CoordinatesTimingRecordingInkAndCloseState()
    {
        var presentation = MakePresentation(2);
        var route = SlideShowCustomShowPlanner.BuildFullPresentationRoute(presentation, startIndex: 0);
        var started = new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero);
        var session = new SlideShowSessionController(
            presentation,
            route,
            started,
            new SlideShowDeterministicRecordingCaptureBackend("focused test"));

        session.ApplyPresenterToolIntent(
            SlideShowTimingIntent.RecordTimings,
            SlideShowRecordingMediaIntent.NarrationAndMedia,
            SlideShowPresenterPointerMode.Pen,
            "#112233",
            6,
            SlideShowInkRetentionDecision.KeepInk,
            currentRouteSlideIndex: 0,
            nowUtc: started);
        session.BeginInkStroke(new SlideShowInkPoint(10, 20));
        session.EndInkStroke(new SlideShowInkPoint(30, 40));
        session.MoveToSlide(1, started.AddMilliseconds(1500));
        session.Close(started.AddMilliseconds(2500));

        session.IsClosed.Should().BeTrue();
        session.CurrentPresentationSlideIndex.Should().Be(1);
        session.TimingRecorderState.RecordedTimings.Should().HaveCount(2);
        session.TimingRecorderState.RecordedTimings[0].AdvanceAfterMs.Should().Be(1500);
        session.TimingRecorderState.RecordedTimings[1].AdvanceAfterMs.Should().Be(1000);
        presentation.Slides[0].Transition!.AdvanceAfterMs.Should().Be(1500);
        session.RecordingExecutionState.Segments.Should().HaveCount(2);
        session.InkExecutionState.CommittedStrokes.Should().ContainSingle();
        session.InkExecutionState.CommittedStrokes[0].SlideIndex.Should().Be(0);
    }

    [Fact]
    public void SessionController_UsesCurrentRouteIndexWhenApplyingToolIntent()
    {
        var presentation = MakePresentation(3);
        var route = SlideShowCustomShowPlanner.BuildCustomShowRoute(
            presentation,
            new SlideShowCustomSlideSequence(
                "Review",
                new[] { presentation.Slides[2].Id, presentation.Slides[0].Id }),
            startIndex: 0);
        var started = new DateTimeOffset(2026, 7, 15, 11, 0, 0, TimeSpan.Zero);
        var session = new SlideShowSessionController(
            presentation,
            route,
            started,
            SlideShowHostCapabilityRecordingCaptureBackend.Deferred("focused test"));

        session.ApplyPresenterToolIntent(
            currentRouteSlideIndex: 0,
            nowUtc: started,
            pointerMode: SlideShowPresenterPointerMode.Highlighter,
            inkColorHex: "#FFEE00",
            inkThicknessDip: 8,
            inkRetentionDecision: SlideShowInkRetentionDecision.KeepInk,
            timingIntent: SlideShowTimingIntent.None,
            mediaIntent: SlideShowRecordingMediaIntent.None);

        session.CurrentPresentationSlideIndex.Should().Be(2);
        session.InkExecutionState.SlideIndex.Should().Be(0);
        session.ToolPlan.PointerInk.InkState.ColorHex.Should().Be("#FFEE00");
    }

    [Fact]
    public void RevealHiddenSlide_InkDrawnOnRevealedSlide_PersistsToThatSlideNotTheUnderlyingRouteSlide()
    {
        var presentation = MakePresentation(3);
        presentation.Slides[1].IsHidden = true;
        var route = SlideShowCustomShowPlanner.BuildFullPresentationRoute(presentation, startIndex: 0);
        route.SourceSlideIndices.Should().Equal(new[] { 0, 2 });

        var started = new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);
        var session = new SlideShowSessionController(
            presentation,
            route,
            started,
            new SlideShowDeterministicRecordingCaptureBackend("focused test"));

        session.ApplyPresenterToolIntent(
            SlideShowTimingIntent.None,
            SlideShowRecordingMediaIntent.None,
            SlideShowPresenterPointerMode.Pen,
            "#112233",
            6,
            SlideShowInkRetentionDecision.KeepInk,
            currentRouteSlideIndex: 0,
            nowUtc: started);

        // Presenter is on route slide 0 (source slide 0, presentation.Slides[0]) and reveals the
        // hidden slide (source slide 1, presentation.Slides[1]) via the 'H' key / a hyperlink.
        var revealed = session.RevealNextHiddenSlide();
        revealed.Should().BeSameAs(presentation.Slides[1]);
        session.DisplaySourceSlideIndex.Should().Be(1);

        // Draws a pen annotation on the revealed hidden slide, then advances back into the
        // normal route and closes the show with ink retained.
        session.BeginInkStroke(new SlideShowInkPoint(10, 20));
        session.EndInkStroke(new SlideShowInkPoint(30, 40));
        session.InkExecutionState.CommittedStrokes.Should().ContainSingle();
        session.InkExecutionState.CommittedStrokes[0].SlideIndex.Should().Be(
            SlideShowInkExecutionPlanner.EncodeHiddenSlideInkIndex(1));

        session.Close(started.AddMilliseconds(500));

        presentation.Slides[1].Shapes.Should().ContainSingle(
            shape => shape.Kind == SlideShapeKind.Ink,
            "the ink was drawn while the hidden slide was on screen, so it must land on that slide");
        presentation.Slides[0].Shapes.Should().NotContain(
            shape => shape.Kind == SlideShapeKind.Ink,
            "the presenter never drew on the underlying route slide directly");
        presentation.Slides[2].Shapes.Should().NotContain(shape => shape.Kind == SlideShapeKind.Ink);
    }

    [Fact]
    public void RevealHiddenSlide_ThenAdvanceAndDraw_PersistsSecondStrokeToTheNewRouteSlide()
    {
        // Sibling coverage: ordinary (non-reveal) navigation and ink attribution must keep
        // working after the hidden-slide reveal encoding is introduced.
        var presentation = MakePresentation(3);
        presentation.Slides[1].IsHidden = true;
        var route = SlideShowCustomShowPlanner.BuildFullPresentationRoute(presentation, startIndex: 0);

        var started = new DateTimeOffset(2026, 8, 17, 9, 30, 0, TimeSpan.Zero);
        var session = new SlideShowSessionController(
            presentation,
            route,
            started,
            new SlideShowDeterministicRecordingCaptureBackend("focused test"));

        session.ApplyPresenterToolIntent(
            SlideShowTimingIntent.None,
            SlideShowRecordingMediaIntent.None,
            SlideShowPresenterPointerMode.Pen,
            "#112233",
            6,
            SlideShowInkRetentionDecision.KeepInk,
            currentRouteSlideIndex: 0,
            nowUtc: started);

        session.RevealNextHiddenSlide();
        session.BeginInkStroke(new SlideShowInkPoint(10, 20));
        session.EndInkStroke(new SlideShowInkPoint(30, 40));

        // Advancing to the next route slide (source slide 2, route index 1) leaves the
        // hidden-slide reveal and returns to ordinary route-indexed ink attribution.
        session.MoveToSlide(1, started.AddMilliseconds(200));
        session.BeginInkStroke(new SlideShowInkPoint(50, 60));
        session.EndInkStroke(new SlideShowInkPoint(70, 80));
        session.Close(started.AddMilliseconds(500));

        presentation.Slides[1].Shapes.Should().ContainSingle(shape => shape.Kind == SlideShapeKind.Ink);
        presentation.Slides[2].Shapes.Should().ContainSingle(shape => shape.Kind == SlideShapeKind.Ink);
        presentation.Slides[0].Shapes.Should().NotContain(shape => shape.Kind == SlideShapeKind.Ink);
    }

    [Fact]
    public void SetPointerModeAndInkColor_DuringActiveNarrationRecording_DoNotRestartTheRecordingSegment()
    {
        var presentation = MakePresentation(2);
        var route = SlideShowCustomShowPlanner.BuildFullPresentationRoute(presentation, startIndex: 0);
        var started = new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);
        var session = new SlideShowSessionController(
            presentation,
            route,
            started,
            new SlideShowDeterministicRecordingCaptureBackend("focused test"));

        // Presenter starts narrating slide 0.
        session.ApplyPresenterToolIntent(
            SlideShowTimingIntent.RecordTimings,
            SlideShowRecordingMediaIntent.Narration,
            SlideShowPresenterPointerMode.Arrow,
            "#000000",
            4,
            SlideShowInkRetentionDecision.KeepInk,
            currentRouteSlideIndex: 0,
            nowUtc: started);

        session.RecordingExecutionState.IsSessionActive.Should().BeTrue();
        session.RecordingExecutionState.CurrentSlideStartedAtUtc.Should().Be(started);

        // Presenter annotates mid-narration: switches to the pen tool 900ms in.
        session.SetPointerMode(SlideShowPresenterPointerMode.Pen, started.AddMilliseconds(900));

        session.RecordingExecutionState.IsSessionActive.Should().BeTrue();
        session.RecordingExecutionState.CurrentSlideStartedAtUtc.Should().Be(started);
        session.RecordingExecutionState.Segments.Should().BeEmpty();

        // Presenter then changes ink colour 1200ms in, still narrating the same slide.
        session.ApplyPresenterToolIntent(
            SlideShowTimingIntent.RecordTimings,
            SlideShowRecordingMediaIntent.Narration,
            SlideShowPresenterPointerMode.Pen,
            "#FF0000",
            4,
            SlideShowInkRetentionDecision.KeepInk,
            currentRouteSlideIndex: 0,
            nowUtc: started.AddMilliseconds(1200));

        session.RecordingExecutionState.IsSessionActive.Should().BeTrue();
        session.RecordingExecutionState.CurrentSlideStartedAtUtc.Should().Be(started);
        session.RecordingExecutionState.Segments.Should().BeEmpty();

        // Presenter finally advances off slide 0 at the 2000ms mark.
        session.MoveToSlide(1, started.AddMilliseconds(2000));

        session.RecordingExecutionState.Segments.Should().ContainSingle(
            "the tool changes must not split slide 0's narration into truncated fragments");
        var segment = session.RecordingExecutionState.Segments[0];
        segment.SlideIndex.Should().Be(0);
        segment.DurationMs.Should().Be(2000);
        segment.MediaArtifacts.Should().ContainSingle(
            artifact => artifact.Kind == SlideShowRecordingMediaArtifactKind.NarrationAudio,
            "the tool changes must not produce duplicate narration artifacts for the same slide");
    }

    private static Presentation MakePresentation(int slideCount)
    {
        var presentation = Presentation.CreateEmpty();
        while (presentation.Slides.Count < slideCount)
        {
            presentation.Slides.Add(new Slide { Title = $"Slide {presentation.Slides.Count + 1}" });
        }

        return presentation;
    }
}
