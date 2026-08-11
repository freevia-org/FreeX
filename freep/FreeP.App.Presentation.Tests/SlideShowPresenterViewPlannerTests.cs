using FluentAssertions;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowPresenterViewPlannerTests
{
    [Fact]
    public void Build_FormatsCurrentNextNotesAndElapsedState()
    {
        var current = new Slide { Id = "current" };
        current.Title = "Quarterly review";
        var next = new Slide { Id = "next" };
        next.Title = "Launch plan";
        var state = new SlideShowPresenterState(
            new SlideShowHostState(3, 0, true, true, false, false, "Slide 1 of 3"),
            new SlideShowPresenterSlideState(0, 0, "current", current.Title, current),
            new SlideShowPresenterSlideState(1, 1, "next", next.Title, next),
            "Confirm the launch date.",
            DateTimeOffset.UtcNow.AddMinutes(-2),
            TimeSpan.FromSeconds(125),
            SlideShowPresenterDisplayIntent.FullScreen,
            SlideShowPresenterToolPlanner.BuildPlan());

        var plan = SlideShowPresenterViewPlanner.Build(state);

        plan.StatusText.Should().Be("Slide 1 of 3");
        plan.CurrentSlideLabel.Should().Be("Slide 1: Quarterly review");
        plan.NextSlideLabel.Should().Be("Slide 2: Launch plan");
        plan.NotesText.Should().Be("Confirm the launch date.");
        plan.ElapsedText.Should().Be("02:05");
        plan.CurrentSlide.Should().BeSameAs(current);
        plan.NextSlide.Should().BeSameAs(next);
        plan.HasNotes.Should().BeTrue();
        plan.HasNextSlide.Should().BeTrue();
        plan.CanGoBack.Should().BeFalse();
        plan.CanAdvance.Should().BeTrue();
        plan.PointerMode.Should().Be(SlideShowPresenterPointerMode.Arrow);
    }

    [Fact]
    public void Build_UsesExplicitEmptyStateLabelsAndClampsNegativeElapsed()
    {
        var state = new SlideShowPresenterState(
            new SlideShowHostState(0, -1, false, false, false, false, "No slides"),
            null,
            null,
            string.Empty,
            DateTimeOffset.UtcNow,
            TimeSpan.FromSeconds(-1),
            SlideShowPresenterDisplayIntent.FullScreen,
            SlideShowPresenterToolPlanner.BuildPlan());

        var plan = SlideShowPresenterViewPlanner.Build(state);

        plan.CurrentSlideLabel.Should().Be(SlideShowPresenterViewPlanner.NoCurrentSlideText);
        plan.NextSlideLabel.Should().Be(SlideShowPresenterViewPlanner.EndOfPresentationText);
        plan.NotesText.Should().Be(SlideShowPresenterViewPlanner.NoNotesText);
        plan.ElapsedText.Should().Be("00:00");
        plan.HasNotes.Should().BeFalse();
        plan.HasNextSlide.Should().BeFalse();
        plan.CanGoBack.Should().BeFalse();
        plan.CanAdvance.Should().BeFalse();
    }

    [Fact]
    public void Build_AllowsAdvanceForPendingAnimationOnLastSlide()
    {
        var slide = new Slide { Id = "last" };
        var state = new SlideShowPresenterState(
            new SlideShowHostState(1, 0, true, true, true, true, "Slide 1 of 1"),
            new SlideShowPresenterSlideState(0, 0, slide.Id, "Last", slide),
            null,
            string.Empty,
            DateTimeOffset.UtcNow,
            TimeSpan.Zero,
            SlideShowPresenterDisplayIntent.FullScreen,
            SlideShowPresenterToolPlanner.BuildPlan());

        SlideShowPresenterViewPlanner.Build(state).CanAdvance.Should().BeTrue();
    }

    [Fact]
    public void Build_ExposesRecordTimingsStateFromTheSharedToolPlan()
    {
        var state = new SlideShowPresenterState(
            new SlideShowHostState(1, 0, true, true, true, false, "Slide 1 of 1"),
            new SlideShowPresenterSlideState(0, 0, "slide", "Slide", new Slide { Id = "slide" }),
            null,
            string.Empty,
            DateTimeOffset.UtcNow,
            TimeSpan.Zero,
            SlideShowPresenterDisplayIntent.FullScreen,
            SlideShowPresenterToolPlanner.BuildPlan(SlideShowTimingIntent.RecordTimings));

        SlideShowPresenterViewPlanner.Build(state).IsRecordingTimings.Should().BeTrue();
    }

    [Fact]
    public void Build_ExposesRehearseTimingsSeparatelyFromRecording()
    {
        var state = new SlideShowPresenterState(
            new SlideShowHostState(1, 0, true, true, true, false, "Slide 1 of 1"),
            new SlideShowPresenterSlideState(0, 0, "slide", "Slide", new Slide { Id = "slide" }),
            null,
            string.Empty,
            DateTimeOffset.UtcNow,
            TimeSpan.Zero,
            SlideShowPresenterDisplayIntent.FullScreen,
            SlideShowPresenterToolPlanner.BuildPlan(SlideShowTimingIntent.RehearseTimings));

        var plan = SlideShowPresenterViewPlanner.Build(state);

        plan.IsRehearsingTimings.Should().BeTrue();
        plan.IsRecordingTimings.Should().BeFalse();
    }

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(65, "01:05")]
    [InlineData(3661, "01:01:01")]
    public void FormatElapsed_UsesClockStyleText(int seconds, string expected)
    {
        SlideShowPresenterViewPlanner.FormatElapsed(TimeSpan.FromSeconds(seconds))
            .Should().Be(expected);
    }
}
