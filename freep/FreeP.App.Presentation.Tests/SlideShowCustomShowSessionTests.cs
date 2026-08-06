using FluentAssertions;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Compositor.Tests;

public sealed class SlideShowCustomShowSessionTests
{
    [Fact]
    public void Session_BuildsLaunchAndNamedRoutesFromCurrentEditorState()
    {
        var editor = CreateEditor();
        var session = new SlideShowCustomShowSession(() => editor);
        editor.SelectSlide(1);
        session.Create("Review", new[] { "slide-2", "slide-1" }).Succeeded.Should().BeTrue();

        session.BuildLaunchPlan().CurrentSlideIndex.Should().Be(1);
        session.TryBuildLaunchRoute(fromStart: false, animationStartIndex: 3, out var launchRoute)
            .Should().BeTrue();
        launchRoute.StartIndex.Should().Be(1);
        launchRoute.AnimationStartIndex.Should().Be(3);

        session.TryBuildNamedRoute("Review", startIndex: 1, out var namedRoute).Should().BeTrue();
        namedRoute.CustomShowName.Should().Be("Review");
        namedRoute.Slides.Select(slide => slide.Id).Should().Equal("slide-2", "slide-1");
        namedRoute.StartIndex.Should().Be(1);
    }

    [Fact]
    public void Session_AppliesDialogMutationsThroughUndoableEditorBoundary()
    {
        var editor = CreateEditor();
        var session = new SlideShowCustomShowSession(() => editor);

        var result = session.ApplyMutation(
            SlideShowCustomShowDialogMutationRequest.Create(
                "Audience",
                new[] { "slide-1", "slide-2" }));

        result.Succeeded.Should().BeTrue();
        session.BuildAuthoringPlan().CustomShows.Should().ContainSingle();
        var dialogPlan = session.BuildDialogPlan(new SlideShowCustomShowSessionState());
        dialogPlan.SelectedShow!.Name.Should().Be("Audience");
        dialogPlan.SelectedSlides.Select(slide => slide.SlideId)
            .Should().Equal("slide-1", "slide-2");

        session.MoveSlide(0, 0, "slide-1", 1).Succeeded.Should().BeTrue();
        editor.Presentation.CustomShows[0].SlideIds.Should().Equal("slide-2", "slide-1");

        editor.Undo();
        editor.Presentation.CustomShows[0].SlideIds.Should().Equal("slide-1", "slide-2");
        editor.Undo();
        editor.Presentation.CustomShows.Should().BeEmpty();
    }

    private static EditingSession CreateEditor()
    {
        var presentation = Presentation.CreateEmpty();
        presentation.Slides.Clear();
        presentation.Slides.Add(new Slide { Id = "slide-1", Title = "Opening" });
        presentation.Slides.Add(new Slide { Id = "slide-2", Title = "Details" });
        return new EditingSession(presentation, new PresentationCommandBus(presentation));
    }
}
