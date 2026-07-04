using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowPresenterSessionSummaryPlannerTests
{
    [Fact]
    public void BuildSummary_CombinesDeferredRecordingAndRetainedInkEvidence()
    {
        var started = new DateTimeOffset(2026, 7, 4, 9, 0, 0, TimeSpan.Zero);
        var presenterPlan = SlideShowPresenterToolPlanner.BuildPlan(
            SlideShowTimingIntent.RecordTimings,
            SlideShowRecordingMediaIntent.NarrationAndMedia,
            SlideShowPresenterPointerMode.Pen,
            "#336699",
            5,
            SlideShowInkRetentionDecision.KeepInk);
        var recording = SlideShowRecordingExecutionPlanner.CreateState(
            presenterPlan,
            currentSlideIndex: 0,
            started,
            SlideShowRecordingHostCapabilities.Deferred("Avalonia slideshow"));
        recording = SlideShowRecordingExecutionPlanner.MoveToSlide(
            recording,
            slideIndex: 1,
            started.AddMilliseconds(1800));
        var ink = SlideShowInkExecutionPlanner.End(
            SlideShowInkExecutionPlanner.Begin(
                SlideShowInkExecutionPlanner.CreateState(0, presenterPlan.PointerInk),
                new SlideShowInkPoint(10, 20)).State,
            new SlideShowInkPoint(30, 40)).State;
        var presentation = MakePresentation(2);

        var summary = SlideShowPresenterSessionSummaryPlanner.BuildSummary(
            recording,
            ink,
            presentation);

        summary.HostName.Should().Be("Avalonia slideshow");
        summary.Recording.Should().Match<SlideShowPresenterRecordingSessionSummary>(recordingSummary =>
            recordingSummary.IsSessionActive &&
            recordingSummary.CurrentSlideIndex == 1 &&
            recordingSummary.CompletedSegmentCount == 1 &&
            recordingSummary.TotalRecordedDurationMs == 1800 &&
            recordingSummary.NarrationRequestedSlideCount == 1 &&
            recordingSummary.NarrationDeferredSlideCount == 1 &&
            recordingSummary.CameraRequestedSlideCount == 1 &&
            recordingSummary.CameraDeferredSlideCount == 1 &&
            recordingSummary.CapturedMediaArtifactCount == 0 &&
            recordingSummary.DeferredMediaArtifactCount == 2);
        summary.Ink.Should().Match<SlideShowPresenterInkSessionSummary>(inkSummary =>
            inkSummary.RetentionDecision == SlideShowInkRetentionDecision.KeepInk &&
            inkSummary.CommittedStrokeCount == 1 &&
            inkSummary.ActiveStrokePointCount == 0 &&
            inkSummary.PersistableStrokeCount == 1 &&
            inkSummary.GeneratedInkSlideCount == 1 &&
            inkSummary.GeneratedInkStrokeCount == 1 &&
            !inkSummary.HasTransientLaserOverlay &&
            inkSummary.WillPersistInkOnExit);
        summary.EvidenceLines.Should().Contain(line => line.Contains("1 recording segment(s), 1800 ms"));
        summary.EvidenceLines.Should().Contain(line => line.Contains("2 recording media artifact(s) deferred"));
        summary.EvidenceLines.Should().Contain(line => line.Contains("1 persistable stroke(s), 1 generated slide part(s)"));
        presentation.Slides.Should().OnlyContain(slide => slide.Shapes.All(shape => shape.Kind != SlideShapeKind.Ink));
    }

    [Fact]
    public void BuildSummary_ClearInkAndLaserOnlyDoNotReportGeneratedRetention()
    {
        var presenterPlan = SlideShowPresenterToolPlanner.BuildPlan(
            pointerMode: SlideShowPresenterPointerMode.LaserPointer,
            inkRetentionDecision: SlideShowInkRetentionDecision.ClearInk);
        var recording = SlideShowRecordingExecutionPlanner.CreateState(
            presenterPlan,
            currentSlideIndex: 0,
            new DateTimeOffset(2026, 7, 4, 10, 0, 0, TimeSpan.Zero),
            SlideShowRecordingHostCapabilities.Deferred("WPF slideshow"));
        var ink = SlideShowInkExecutionPlanner.Begin(
            SlideShowInkExecutionPlanner.CreateState(0, presenterPlan.PointerInk),
            new SlideShowInkPoint(50, 60)).State;

        var summary = SlideShowPresenterSessionSummaryPlanner.BuildSummary(
            recording,
            ink,
            MakePresentation(1));

        summary.Recording.IsSessionActive.Should().BeFalse();
        summary.Ink.RetentionDecision.Should().Be(SlideShowInkRetentionDecision.ClearInk);
        summary.Ink.HasTransientLaserOverlay.Should().BeTrue();
        summary.Ink.GeneratedInkSlideCount.Should().Be(0);
        summary.Ink.GeneratedInkStrokeCount.Should().Be(0);
        summary.Ink.WillPersistInkOnExit.Should().BeFalse();
        summary.EvidenceLines.Should().Contain("Presenter ink: transient laser overlay is not retained");
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
