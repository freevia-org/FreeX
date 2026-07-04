using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowRecordingReviewPlannerTests
{
    [Fact]
    public void BuildPlan_DescribesCompletedSegmentsAndDeferredArtifacts()
    {
        var presentation = MakePresentation("Intro", "Demo");
        var started = new DateTimeOffset(2026, 7, 4, 11, 0, 0, TimeSpan.Zero);
        var presenterPlan = SlideShowPresenterToolPlanner.BuildPlan(
            SlideShowTimingIntent.RecordTimings,
            SlideShowRecordingMediaIntent.NarrationAndMedia);
        var recording = SlideShowRecordingExecutionPlanner.CreateState(
            presenterPlan,
            currentSlideIndex: 0,
            started,
            SlideShowRecordingHostCapabilities.Deferred("WPF slideshow"));
        recording = SlideShowRecordingExecutionPlanner.MoveToSlide(
            recording,
            slideIndex: 1,
            started.AddMilliseconds(2200));

        var plan = SlideShowRecordingReviewPlanner.BuildPlan(presentation, recording);

        plan.HostName.Should().Be("WPF slideshow");
        plan.TimingIntent.Should().Be(SlideShowTimingIntent.RecordTimings);
        plan.IsSessionActive.Should().BeTrue();
        plan.CompletedSegmentCount.Should().Be(1);
        plan.TotalRecordedDurationMs.Should().Be(2200);
        plan.CanApplyRecordedTimings.Should().BeTrue();
        plan.DeferredMediaArtifactCount.Should().Be(2);
        plan.CapturedMediaArtifactCount.Should().Be(0);
        plan.TimingMutations.Should().Equal(
            new SlideShowSlideTimingMutation(
                SlideIndex: 0,
                AdvanceAfterMs: 2200,
                ShouldPersist: true,
                TimingIntent: SlideShowTimingIntent.RecordTimings));

        var row = plan.Rows.Should().ContainSingle().Subject;
        row.Should().Match<SlideShowRecordingReviewRow>(reviewRow =>
            reviewRow.SlideIndex == 0 &&
            reviewRow.SlideTitle == "Intro" &&
            reviewRow.DurationMs == 2200 &&
            reviewRow.MediaIntent == SlideShowRecordingMediaIntent.NarrationAndMedia &&
            reviewRow.TimingStatus == SlideShowRecordingReviewTimingStatus.WillPersist &&
            reviewRow.TimingWillPersist);
        row.MediaArtifacts.Select(artifact => artifact.Kind).Should().Equal(
            SlideShowRecordingMediaArtifactKind.NarrationAudio,
            SlideShowRecordingMediaArtifactKind.CameraVideo);
        row.MediaArtifacts.Should().OnlyContain(artifact => artifact.IsDeferred);
        row.EvidenceLines.Should().Contain(line => line.Contains("timing will persist"));
        plan.EvidenceLines.Should().Contain(line => line.Contains("1 recorded timing mutation"));
    }

    [Fact]
    public void ApplyRecordedTimings_PersistsOnlyReviewTimingMutations()
    {
        var presentation = MakePresentation("Intro", "Demo");
        presentation.Slides[0].Transition = new SlideTransition
        {
            Kind = TransitionKind.Fade,
            DurationMs = 700
        };
        var started = new DateTimeOffset(2026, 7, 4, 11, 0, 0, TimeSpan.Zero);
        var presenterPlan = SlideShowPresenterToolPlanner.BuildPlan(SlideShowTimingIntent.RecordTimings);
        var recording = SlideShowRecordingExecutionPlanner.CreateState(
            presenterPlan,
            currentSlideIndex: 0,
            started);
        recording = SlideShowRecordingExecutionPlanner.MoveToSlide(
            recording,
            slideIndex: 1,
            started.AddMilliseconds(1750));
        var plan = SlideShowRecordingReviewPlanner.BuildPlan(presentation, recording);

        SlideShowRecordingReviewPlanner.ApplyRecordedTimings(presentation, plan);

        presentation.Slides[0].Transition.Should().NotBeNull();
        presentation.Slides[0].Transition!.Kind.Should().Be(TransitionKind.Fade);
        presentation.Slides[0].Transition!.DurationMs.Should().Be(700);
        presentation.Slides[0].Transition!.AdvanceAfterMs.Should().Be(1750);
        presentation.Slides[1].Transition.Should().BeNull();

        var applied = SlideShowRecordingReviewPlanner.BuildPlan(presentation, recording);
        applied.CanApplyRecordedTimings.Should().BeFalse();
        applied.Rows.Single().TimingStatus.Should().Be(SlideShowRecordingReviewTimingStatus.AlreadyApplied);
    }

    [Fact]
    public void BuildPlan_RehearseTimingsReportsPreviewOnlyRows()
    {
        var presentation = MakePresentation("Intro");
        var started = new DateTimeOffset(2026, 7, 4, 11, 0, 0, TimeSpan.Zero);
        var presenterPlan = SlideShowPresenterToolPlanner.BuildPlan(SlideShowTimingIntent.RehearseTimings);
        var recording = SlideShowRecordingExecutionPlanner.CreateState(
            presenterPlan,
            currentSlideIndex: 0,
            started);
        recording = SlideShowRecordingExecutionPlanner.EndSession(
            recording,
            started.AddMilliseconds(900));

        var plan = SlideShowRecordingReviewPlanner.BuildPlan(presentation, recording);

        plan.CanApplyRecordedTimings.Should().BeFalse();
        plan.TimingMutations.Should().BeEmpty();
        plan.Rows.Should().ContainSingle().Which.TimingStatus
            .Should().Be(SlideShowRecordingReviewTimingStatus.PreviewOnly);
    }

    private static Presentation MakePresentation(params string[] titles)
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();
        foreach (var title in titles)
        {
            presentation.Slides.Add(new Slide { Title = title });
        }

        return presentation;
    }
}
