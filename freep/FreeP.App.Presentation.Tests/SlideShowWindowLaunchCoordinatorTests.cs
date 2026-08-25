using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowWindowLaunchCoordinatorTests
{
    [Fact]
    public void TryLaunch_ComposesCaptionTimingAndNativeWindowCallbacks()
    {
        var editor = CreateEditor();
        var mediaShape = new SlideShape
        {
            Id = 42,
            Kind = SlideShapeKind.Media,
            Media = new MediaInfo { IsVideo = true },
        };
        editor.CurrentSlide!.Shapes.Add(mediaShape);
        editor.Select(mediaShape.Id);
        var customShows = new SlideShowCustomShowSession(() => editor);
        SlideShowWindowLaunchPlan? createdPlan = null;
        SlideShowTimingIntent? appliedTiming = null;
        FakeWindow? shownWindow = null;
        var coordinator = new SlideShowWindowLaunchCoordinator<FakeWindow>(
            customShows,
            () => editor.Presentation,
            () => 3,
            editor.SetSlideNotesText,
            plan =>
            {
                createdPlan = plan;
                return new FakeWindow();
            },
            (_, intent) => appliedTiming = intent,
            window => shownWindow = window);

        coordinator.TryLaunch(
                fromStart: true,
                SlideShowTimingIntent.RecordTimings,
                animationStartIndex: 2)
            .Should().BeTrue();

        createdPlan.Should().NotBeNull();
        createdPlan!.Presentation.Should().BeSameAs(editor.Presentation);
        createdPlan.PlaybackRoute.AnimationStartIndex.Should().Be(2);
        createdPlan.PreferredCaptionSlideIndex.Should().Be(0);
        createdPlan.PreferredCaptionShapeId.Should().Be(mediaShape.Id);
        createdPlan.PreferredCaptionTrackIndex.Should().Be(3);
        createdPlan.SetSlideNotesText.Should().NotBeNull();
        appliedTiming.Should().Be(SlideShowTimingIntent.RecordTimings);
        shownWindow.Should().NotBeNull();
    }

    [Fact]
    public void TryLaunchNamed_RejectsMissingShowWithoutCreatingNativeWindow()
    {
        var editor = CreateEditor();
        var customShows = new SlideShowCustomShowSession(() => editor);
        var createCount = 0;
        var coordinator = new SlideShowWindowLaunchCoordinator<FakeWindow>(
            customShows,
            () => editor.Presentation,
            () => null,
            editor.SetSlideNotesText,
            _ =>
            {
                createCount++;
                return new FakeWindow();
            },
            (_, _) => { },
            _ => { });

        coordinator.TryLaunchNamed("Missing").Should().BeFalse();
        createCount.Should().Be(0);
    }

    [Fact]
    public void TryLaunchReadingView_UsesCurrentSlideAndForcesNonPersistentBrowseWindow()
    {
        var editor = CreateEditor();
        editor.SelectSlide(1);
        var customShows = new SlideShowCustomShowSession(() => editor);
        SlideShowWindowLaunchPlan? createdPlan = null;
        var coordinator = new SlideShowWindowLaunchCoordinator<FakeWindow>(
            customShows,
            () => editor.Presentation,
            () => null,
            editor.SetSlideNotesText,
            plan =>
            {
                createdPlan = plan;
                return new FakeWindow();
            },
            (_, _) => { },
            _ => { });

        coordinator.TryLaunchReadingView().Should().BeTrue();

        createdPlan.Should().NotBeNull();
        createdPlan!.PlaybackRoute.StartIndex.Should().Be(1);
        createdPlan.ForceBrowseWindow.Should().BeTrue();
        editor.Presentation.ShowType.Should().Be(PresentationShowType.PresentedBySpeaker);
    }

    private static EditingSession CreateEditor()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();
        presentation.Slides.Add(new Slide { Id = "slide-1", Title = "Opening" });
        presentation.Slides.Add(new Slide { Id = "slide-2", Title = "Details" });
        return new EditingSession(presentation, new PresentationCommandBus(presentation));
    }

    private sealed class FakeWindow;
}
